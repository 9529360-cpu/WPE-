using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 风险引擎 - 负责风险指标计算和压力测试
/// </summary>
public sealed class RiskEngine
{
    private readonly DataCacheService _cacheService;
    private readonly SemaphoreSlim _calculationLock = new(1, 1);

    public RiskEngine(DataCacheService cacheService)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    /// <summary>
    /// 生成风险报告
    /// </summary>
    public async Task<RiskReport> GenerateReportAsync(
        IEnumerable<PositionSnapshot> positions,
        IEnumerable<RiskRule> rules,
        string benchmarkSymbol,
        decimal accountEquity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(rules);

        await _calculationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var positionList = positions.ToList();
            if (positionList.Count == 0)
            {
                return new RiskReport
                {
                    Metrics = Array.Empty<RiskMetrics>(),
                    BreachedRules = Array.Empty<RiskRule>(),
                    StressTests = Array.Empty<StressTestResult>()
                };
            }

            IReadOnlyList<double> returns = await _cacheService.LoadReturnsAsync(benchmarkSymbol, 500).ConfigureAwait(false);
            RiskMetrics[] riskMetrics = positionList.Select(p => CalculateMetrics(p, returns, accountEquity)).ToArray();

            RiskRule[] breached = rules.Where(r => r.IsActive && riskMetrics.Any(m => EvaluateRule(m, r))).ToArray();
            StressTestResult[] stress = BuildStressTests(positionList, returns).ToArray();

            return new RiskReport
            {
                Metrics = riskMetrics,
                BreachedRules = breached,
                StressTests = stress
            };
        }
        finally
        {
            _calculationLock.Release();
        }
    }

    private static RiskMetrics CalculateMetrics(PositionSnapshot position, IReadOnlyList<double> returns, decimal accountEquity)
    {
        // Fix type mismatch: cast MarkPrice (decimal) to double before multiplication
        double[] pnlSeries = returns.Select(r => (double)position.PositionAmt * (double)position.MarkPrice * r).ToArray();
        double var99 = HistoricalVaR(pnlSeries, 0.99);
        double cvar = ConditionalVaR(pnlSeries, 0.99);
        double volatility = returns.Count > 0 ? Math.Sqrt(returns.Average(r => r * r) * 252) : 0;
        double drawdown = MaxDrawdown(pnlSeries);
        decimal exposure = Math.Abs(position.PositionAmt * position.MarkPrice);
        decimal leverage = accountEquity == 0 ? 0 : (decimal)exposure / accountEquity;
        double liquidation = position.EntryPrice == 0 ? 0 : (double)position.EntryPrice * (1 - Math.Sign(position.PositionAmt) * 1.0 / (double)position.Leverage);
        double kelly = returns.Count > 0 ? KellyFraction(returns) : 0;

        return new RiskMetrics
        {
            Symbol = position.Symbol,
            ValueAtRisk = (decimal)var99,
            ConditionalVaR = (decimal)cvar,
            Volatility = (decimal)volatility,
            MaxDrawdown = (decimal)drawdown,
            Exposure = (decimal)exposure,
            Leverage = leverage,
            KellyFraction = (decimal)kelly,
            LiquidationPrice = (decimal)liquidation,
            CalculatedAt = DateTime.UtcNow
        };
    }

    private static bool EvaluateRule(RiskMetrics metrics, RiskRule rule)
    {
        return rule.Comparator switch
        {
            ">" => metrics.ValueAtRisk > rule.Threshold,
            ">=" => metrics.ValueAtRisk >= rule.Threshold,
            "<" => metrics.ValueAtRisk < rule.Threshold,
            "<=" => metrics.ValueAtRisk <= rule.Threshold,
            "LEV>" => metrics.Leverage > rule.Threshold,
            "DD>" => metrics.MaxDrawdown > rule.Threshold,
            _ => false
        };
    }

    private static IEnumerable<StressTestResult> BuildStressTests(IEnumerable<PositionSnapshot> positions, IReadOnlyList<double> returns)
    {
        var scenarios = new Dictionary<string, double>
        {
            ["Flash Crash -15%"] = -0.15,
            ["Volatility Spike +8σ"] = returns.Count > 0 ? returns.Average(r => Math.Abs(r)) * 8 : -0.08,
            ["Mean Reversion +5%"] = 0.05
        };

        foreach (KeyValuePair<string, double> scenario in scenarios)
        {
            double pnl = positions.Sum(p => (double)p.PositionAmt * (double)p.MarkPrice * scenario.Value);
            double margin = positions.Sum(p => (double)p.MaintenanceMargin);
            yield return new StressTestResult
            {
                Scenario = scenario.Key,
                PortfolioPnl = (decimal)pnl,
                MarginUsage = (decimal)margin,
                BreachProbability = returns.Count > 0 ? (decimal)returns.Count(r => r < scenario.Value) / returns.Count : 0
            };
        }
    }

    private static double HistoricalVaR(IReadOnlyList<double> pnl, double confidence)
    {
        if (pnl.Count == 0)
        {
            return 0;
        }

        double[] sorted = pnl.OrderBy(x => x).ToArray();
        int index = (int)Math.Floor((1 - confidence) * sorted.Length);
        index = Math.Clamp(index, 0, sorted.Length - 1);
        return -sorted[index];
    }

    private static double ConditionalVaR(IReadOnlyList<double> pnl, double confidence)
    {
        if (pnl.Count == 0)
        {
            return 0;
        }

        double threshold = HistoricalVaR(pnl, confidence);
        double[] tail = pnl.Where(x => -x >= threshold).ToArray();
        return tail.Length == 0 ? threshold : tail.Average(x => -x);
    }

    private static double MaxDrawdown(IReadOnlyList<double> pnl)
    {
        double peak = 0;
        double trough = 0;
        double maxDrawdown = 0;

        foreach (double value in pnl)
        {
            peak = Math.Max(peak + value, 0);
            trough = Math.Min(trough + value, peak);
            maxDrawdown = Math.Max(maxDrawdown, peak - trough);
        }

        return maxDrawdown;
    }

    private static double KellyFraction(IReadOnlyList<double> returns)
    {
        if (returns.Count == 0)
        {
            return 0;
        }

        double[] positive = returns.Where(r => r > 0).ToArray();
        if (positive.Length == 0)
        {
            return 0;
        }

        double winProb = (double)positive.Length / returns.Count;
        double avgWin = positive.Average();
        double avgLoss = returns.Where(r => r <= 0).Select(Math.Abs).DefaultIfEmpty().Average();
        if (avgLoss == 0)
        {
            return 0;
        }

        double odds = avgWin / avgLoss;
        return winProb - (1 - winProb) / odds;
    }
}
