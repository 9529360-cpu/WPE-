using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Models.Configuration;
using 币安量化机器人.Services.Risk;

namespace 币安量化机器人.Services;

/// <summary>
/// 风险引擎 - 负责风险指标计算和压力测试
/// </summary>
public sealed class RiskEngine
{
    private readonly DataCacheService _cacheService;
    private readonly SemaphoreSlim _calculationLock = new(1, 1);

    // 用于短时间内重复下单检测: key = accountId+symbol, value = last order timestamp
    private readonly ConcurrentDictionary<string, DateTime> _recentOrderTimestamps = new();

    // 账户峰值净值缓存: key = accountId, value = (peak, timestamp)
    private readonly ConcurrentDictionary<string, (decimal Peak, DateTime Timestamp)> _peakCache = new();
    private TimeSpan PeakCacheTtl => TimeSpan.FromMinutes(ServiceLocator.RiskConfig.PeakCacheTtlMinutes);

    public RiskEngine(DataCacheService cacheService)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    /// <summary>
    /// 预下单检查: 基于配置校验单笔占比/日亏/最小间隔/白名单黑名单
    /// </summary>
    public async Task<TradePermitResult> PreTradeCheckAsync(OrderRequest request, TradingAccount account, CancellationToken ct = default)
    {
        if (request == null)
        {
            return TradePermitResult.Deny("无效订单请求");
        }

        if (account == null)
        {
            return TradePermitResult.Deny("无效账户");
        }

        RiskConfig cfg = ConfigurationService.GetRiskConfig();

        // 检查白名单
        if (cfg.EnableWhitelist && cfg.WhitelistSymbols?.Length > 0)
        {
            if (!cfg.WhitelistSymbols.Contains(request.Symbol?.ToUpperInvariant()))
            {
                var reason = "交易对不在白名单中";
                _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.Whitelist", Services.Observability.AlertSeverity.Warning, $"拒单: {request.Symbol} - {reason}");
                return TradePermitResult.Deny(reason);
            }
        }

        // 检查黑名单
        if (cfg.EnableBlacklist && cfg.BlacklistSymbols?.Length > 0)
        {
            if (cfg.BlacklistSymbols.Contains(request.Symbol?.ToUpperInvariant()))
            {
                var reason = "交易对在黑名单中";
                _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.Blacklist", Services.Observability.AlertSeverity.Warning, $"拒单: {request.Symbol} - {reason}");
                return TradePermitResult.Deny(reason);
            }
        }

        // 检查最小下单间隔
        try
        {
            string key = $"{account.Id}:{request.Symbol?.ToUpperInvariant()}";
            DateTime now = DateTime.UtcNow;
            if (_recentOrderTimestamps.TryGetValue(key, out var last))
            {
                int elapsedMs = (int)(now - last).TotalMilliseconds;
                if (elapsedMs < cfg.MinOrderIntervalMs)
                {
                    var reason = $"短时间内重复下单 (间隔 {elapsedMs}ms < {cfg.MinOrderIntervalMs}ms)";
                    _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.RateLimit", Services.Observability.AlertSeverity.Warning, $"拒单: {request.Symbol} - {reason}");
                    return TradePermitResult.Deny(reason);
                }
            }
        }
        catch { /* ignore timestamp checks on error */ }

        // 检查单笔资金占比
        try
        {
            decimal net = account.NetValue;
            if (net <= 0)
            {
                var reason = "账户净值不足";
                _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.NoNet", Services.Observability.AlertSeverity.Error, $"拒单: {request.Symbol} - {reason}");
                return TradePermitResult.Deny(reason);
            }

            decimal proposedValue = request.Price * request.Quantity;
            decimal allowed = net * (decimal)cfg.MaxSingleTradePct;
            if (proposedValue > allowed)
            {
                var reason = $"单笔下单超出最大允许占比 ({proposedValue:F4} > {allowed:F4})";
                _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.SingleSize", Services.Observability.AlertSeverity.Warning, $"拒单: {request.Symbol} - {reason}");
                return TradePermitResult.Deny(reason);
            }
        }
        catch { /* ignore value checks on error */ }

        // 检查最大总仓位占比
        try
        {
            if (cfg.EnableMaxPositionLimit)
            {
                decimal totalPositionsValue = account.PositionValue;
                decimal net = account.NetValue;
                decimal maxTotal = net * (decimal)cfg.MaxTotalPositionSize;
                if (totalPositionsValue > maxTotal)
                {
                    var reason = $"总持仓超出阈值 ({totalPositionsValue:F4} > {maxTotal:F4})";
                    _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.TotalPosition", Services.Observability.AlertSeverity.Warning, $"拒单: {request.Symbol} - {reason}");
                    return TradePermitResult.Deny(reason);
                }
            }
        }
        catch { }

        // 检查当日亏损阈值
        try
        {
            if (cfg.EnableDailyLossLimit)
            {
                decimal dailyLossLimit = account.InitialBalance * (decimal)cfg.MaxDailyLossPct;
                // account.TodayPnL 为正表示盈利, 负表示亏损
                if (account.TodayPnL <= -dailyLossLimit)
                {
                    var reason = $"已达到当日亏损上限: {account.TodayPnL:F4} <= -{dailyLossLimit:F4}";
                    // 触发告警（异步不阻塞）
                    _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny", Services.Observability.AlertSeverity.Warning, $"拒单: {reason}");
                    return TradePermitResult.Deny(reason);
                }
            }
        }
        catch { }

        // 检查最大回撤保护（基于初始资金的简化计算）
        try
        {
            if (cfg.EnableMaxDrawdownProtection)
            {
                // 使用历史峰值计算回撤（更准确）
                decimal current = account.NetValue;
                try
                {
                    decimal peak = await GetAccountPeakEquityAsync(account, ct).ConfigureAwait(false);
                    if (peak > 0)
                    {
                        decimal drawdownPct = (peak - current) / peak;
                        if (drawdownPct >= (decimal)cfg.MaxDrawdownThreshold)
                        {
                            var reason = $"账户回撤超出阈值 ({drawdownPct:P2} >= {cfg.MaxDrawdownThreshold:P2})";
                            _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.Drawdown", Services.Observability.AlertSeverity.Critical, $"拒单: {request.Symbol} - {reason}");
                            return TradePermitResult.Deny(reason);
                        }
                    }
                }
                catch
                {
                    // 如果无法获取历史峰值, 回退到使用初始资金的简化计算
                    decimal initial = account.InitialBalance;
                    if (initial > 0)
                    {
                        decimal drawdownPct = (initial - current) / initial;
                        if (drawdownPct >= (decimal)cfg.MaxDrawdownThreshold)
                        {
                            var reason = $"账户回撤超出阈值 ({drawdownPct:P2} >= {cfg.MaxDrawdownThreshold:P2})";
                            _ = ServiceLocator.Observability.TriggerAlert("Risk.Deny.Drawdown", Services.Observability.AlertSeverity.Critical, $"拒单: {request.Symbol} - {reason}");
                            return TradePermitResult.Deny(reason);
                        }
                    }
                }
            }
        }
        catch { }

        return TradePermitResult.Permit();
    }

    /// <summary>
    /// 下单后记录: 更新最近下单时间与统计（简单实现，更多持久化可以写入 DB）
    /// </summary>
    public async Task PostTradeRecordAsync(OrderResponse? response, OrderRequest request, TradingAccount account)
    {
        try
        {
            string key = $"{account.Id}:{request.Symbol?.ToUpperInvariant()}";
            _recentOrderTimestamps[key] = DateTime.UtcNow;

            // 持久化记录拒单或成交信息
            try
            {
                var history = ServiceLocator.OrderHistory;
                if (response == null)
                {
                    await history.RecordRejectedOrderAsync(request, "Rejected by risk engine", account).ConfigureAwait(false);
                }
                else
                {
                    await history.RecordOrderPlacedAsync(response).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "将交易记录写入 OrderHistory 失败");
            }

            // 如果有响应并且成交，更新账户统计（仅内存）
            if (response != null && (string.Equals(response.Status, "FILLED", StringComparison.OrdinalIgnoreCase) || string.Equals(response.Status, "PARTIALLY_FILLED", StringComparison.OrdinalIgnoreCase)))
            {
                account.TotalTrades++;
                account.LastTradeAt = DateTime.UtcNow;
            }

            // 更新峰值缓存：如果当前净值超过缓存峰值则更新
            try
            {
                decimal currentNet = account.NetValue;
                if (!_peakCache.TryGetValue(account.Id, out var entry) || currentNet > entry.Peak)
                {
                    _peakCache[account.Id] = (currentNet, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "更新峰值缓存失败");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "PostTradeRecordAsync 记录失败");
        }

        await Task.CompletedTask;
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

    /// <summary>
    /// 计算账户历史峰值净值（基于每日 PnL 快照重建 equity 序列）
    /// </summary>
    private async Task<decimal> GetAccountPeakEquityAsync(TradingAccount account, CancellationToken ct)
    {
        if (account == null)
        {
            return 0m;
        }

        try
        {
            // Try cache first
            if (_peakCache.TryGetValue(account.Id, out var cached))
            {
                if (DateTime.UtcNow - cached.Timestamp < PeakCacheTtl)
                {
                    return cached.Peak;
                }
            }

            // Load recent daily pnl snapshots (days default 30)
            var daily = await _cacheService.LoadDailyPnlAsync(90).ConfigureAwait(false);
            if (daily == null || daily.Count == 0)
            {
                // cache initial balance as peak to avoid repeated DB calls
                _peakCache[account.Id] = (account.InitialBalance, DateTime.UtcNow);
                return account.InitialBalance;
            }

            // Assume daily entries ordered by date ascending; build cumulative equity starting from initial balance
            decimal equity = account.InitialBalance;
            decimal peak = equity;
            foreach (var snap in daily.OrderBy(d => d.Date))
            {
                // snap.RealizedPnl expected as double
                equity += (decimal)snap.RealizedPnl;
                if (equity > peak)
                {
                    peak = equity;
                }
            }

            // Also consider current net value
            if (account.NetValue > peak)
            {
                peak = account.NetValue;
            }

            // store peak in cache
            _peakCache[account.Id] = (peak, DateTime.UtcNow);

            return peak;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "GetAccountPeakEquityAsync 计算峰值失败，回退到初始资金");
            return account.InitialBalance;
        }
    }
}
