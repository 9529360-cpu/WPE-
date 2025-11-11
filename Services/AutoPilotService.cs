using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;
using 币安量化机器人.Models.Configuration; // ensure BacktestConfig is in scope

namespace 币安量化机器人.Services;

public sealed class AutoPilotService
{
    private readonly SystemReadyService _ready;
    private readonly AIStrategyGenerator _generator;
    private readonly StrategyPortfolioManager _portfolio;
    private readonly AutoTradingController _autoTrader;

    private bool _enabled;
    private CancellationTokenSource? _cts;

    // 限制
    public int MaxStrategiesSim { get; set; } = 5;
    public int MaxStrategiesLive { get; set; } = 3;
    public double MaxTotalWeight { get; set; } = 1.0;

    // 配置驱动门槛
    public bool EnableLiveAutopilot { get; set; } = false; // 单独控制实盘自动驾驶
    private DateTime _lastOptimization = DateTime.UtcNow;
    public TimeSpan OptimizationInterval { get; set; } = TimeSpan.FromHours(4);

    // 连续回撤计数器
    private readonly Dictionary<string, int> _consecutiveDrawdowns = new();

    public AutoPilotService(SystemReadyService ready, AIStrategyGenerator generator, StrategyPortfolioManager portfolio, AutoTradingController autoTrader)
    {
        _ready = ready; _generator = generator; _portfolio = portfolio; _autoTrader = autoTrader;
        _ready.ReadyStateChanged += OnReadyChanged;
        ConfigurationService.ConfigurationChanged += ApplyConfig;
        ApplyConfig();
    }

    private void ApplyConfig()
    {
        var cfg = ServiceLocator.TradingConfig;
        EnableLiveAutopilot = cfg.Autopilot.LiveEnabled;
        OptimizationInterval = TimeSpan.FromMinutes(cfg.Autopilot.Optimization.PeriodMinutes);
    }

    public bool Enabled => _enabled;
    public void Enable() { _enabled = true; _cts?.Cancel(); _cts = new CancellationTokenSource(); _ = RunAsync(_cts.Token); }
    public void Disable() { _enabled = false; _cts?.Cancel(); }

    private (double minWinRate, double maxDrawdown, double minSharpe) GetGate(AccountType acct)
    {
        var cfg = ServiceLocator.TradingConfig;
        if (acct == AccountType.Live)
        {
            return (Math.Clamp(cfg.MinConfidence + 0.15, 0, 1), ServiceLocator.RiskConfig.MaxDrawdownThreshold, Math.Max(1.0, cfg.Autopilot.Optimization.MinSharpe));
        }
        return (Math.Clamp(cfg.MinConfidence + 0.10, 0, 1), 0.20, Math.Max(0.8, cfg.Autopilot.Optimization.MinSharpe - 0.2));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var thresholds = ServiceLocator.TradingConfig.Autopilot;
        while (!ct.IsCancellationRequested && _enabled)
        {
            if (!_ready.TradingReady)
            {
                await Task.Delay(1000, ct);
                continue;
            }
            try
            {
                await _autoTrader.StartAsync(new[] { "BTCUSDT" }, AccountType.Simulated);
            }
            catch { }

            _portfolio.AdjustWeightsByRisk();
            ApplyExitRules(thresholds);

            var gateSim = GetGate(AccountType.Simulated);
            await EnsureStrategiesAsync(AccountType.Simulated, MaxStrategiesSim, gateSim, ct);

            if (EnableLiveAutopilot)
            {
                var gateLive = GetGate(AccountType.Live);
                await EnsureStrategiesAsync(AccountType.Live, MaxStrategiesLive, gateLive, ct);
            }

            if (DateTime.UtcNow - _lastOptimization > OptimizationInterval)
            {
                await OptimizeRunningStrategiesAsync(ct);
                _lastOptimization = DateTime.UtcNow;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    private void ApplyExitRules(AutopilotConfig thresholds)
    {
        foreach (var s in _portfolio.GetRunningStrategies().ToList())
        {
            // 权重跌破阈值 -> 停止
            if (s.Weight <= thresholds.WeightStopThreshold)
            {
                LogService.Info("[AutoPilot] 停止策略(权重过低): {Name} w={Weight:F2}", s.Name, s.Weight);
                _portfolio.StopStrategy(s.Id);
                continue;
            }

            // 连续回撤统计
            if (s.TotalReturn < 0)
            {
                _consecutiveDrawdowns[s.Id] = _consecutiveDrawdowns.TryGetValue(s.Id, out var c) ? c + 1 : 1;
            }
            else
            {
                _consecutiveDrawdowns[s.Id] = 0;
            }

            // 连续回撤触发或回撤超过阈值
            if (_consecutiveDrawdowns.TryGetValue(s.Id, out int cnt) && cnt >= thresholds.ConsecutiveDrawdownLimit)
            {
                LogService.Warning("[AutoPilot] 连续回撤触发停止: {Name} cnt={Cnt}", s.Name, cnt);
                _portfolio.StopStrategy(s.Id);
                continue;
            }
            if (s.MaxDrawdown >= thresholds.DrawdownStopThreshold)
            {
                LogService.Warning("[AutoPilot] 回撤超阈值停止: {Name} dd={DD:P1}", s.Name, s.MaxDrawdown);
                _portfolio.StopStrategy(s.Id);
            }
        }
    }

    private async Task EnsureStrategiesAsync(AccountType targetAccount, int maxStrategies, (double minWinRate, double maxDrawdown, double minSharpe) gate, CancellationToken ct)
    {
        var list = _portfolio.GetStrategies(targetAccount);
        if (list.Count >= maxStrategies)
        {
            return;
        }

        var s = await _generator.GenerateAsync(new[] { "BTCUSDT", "ETHUSDT" }, ct);
        s.AccountType = targetAccount;

        // 构建策略并回测
        ITradingStrategy strat = ServiceLocator.StrategyFactory.CreateFromInstance(s);
        DateTime end = DateTime.UtcNow;
        DateTime start = end.AddDays(-14);
        var request = new BacktestRequest(s.Symbols.First(), start, end, strat);
        BacktestResult result = await ServiceLocator.EnhancedBacktest.RunAsync(request, ct);

        // KPI 门槛
        bool pass = result.WinRate >= gate.minWinRate && result.MaxDrawdown <= gate.maxDrawdown && result.Sharpe >= gate.minSharpe;
        if (!pass)
        {
            return;
        }

        _portfolio.AddStrategy(s);
        await _portfolio.StartStrategyAsync(s.Id, targetAccount == AccountType.Live ? StrategyStage.LiveRunning : StrategyStage.PaperRunning);

        // 保存最佳模板
        var template = new StrategyTemplate(s.Name, s.Type, s.Symbols, s.AccountType, s.Parameters, new TemplateMetrics(result.WinRate, result.MaxDrawdown, result.Sharpe, result.ProfitFactor), DateTime.UtcNow);
        ServiceLocator.StrategyTemplates.SaveTemplate(template);

        // 更新首次绩效快照（包含真实波动率）
        _portfolio.UpdatePerformanceSnapshot(s.Id, (decimal)result.NetProfit, result.Sharpe, result.MaxDrawdown, 0, 0, result.Volatility);
    }

    private async Task OptimizeRunningStrategiesAsync(CancellationToken ct)
    {
        var running = _portfolio.GetRunningStrategies();
        var optCfg = ServiceLocator.TradingConfig.Autopilot.Optimization;
        foreach (var inst in running)
        {
            _portfolio.UpdateProgress(inst.Id, inst.ProcessedBars, inst.TargetBars, StrategyStage.Optimizing);
            ITradingStrategy strat = ServiceLocator.StrategyFactory.CreateFromInstance(inst);
            DateTime end = DateTime.UtcNow;
            DateTime start = end.AddDays(-7);
            var paramSpace = new Dictionary<string, IReadOnlyList<double>>
            {
                ["entry_z_score"] = new List<double> {1.2,1.5,1.8},
                ["stop_multiplier"] = new List<double> {1.5,2,2.5},
                ["base_quantity"] = new List<double> {1,2,3}
            };
            var optimizer = ServiceLocator.WalkForward;
            await foreach (var wf in optimizer.OptimizeAsync(strat, inst.Symbols.First(), start, end, TimeSpan.FromDays(3), TimeSpan.FromDays(1), paramSpace, ct))
            {
                if (wf.WalkForwardTestResult != null && wf.Optimization != null)
                {
                    var r = wf.WalkForwardTestResult;
                    // 精确拷贝参数字典
                    inst.Parameters = wf.Optimization.Parameters.Values.ToDictionary(k => k.Key, v => v.Value);

                    // 判断改进：收益/Sharpe/波动率
                    bool improvedSharpe = r.Sharpe > inst.SharpeRatio + optCfg.MinPerfImprPct;
                    bool lowerVol = r.Volatility <= inst.Volatility * (1 - optCfg.MinVolatilityDropPct);
                    bool passSharpe = r.Sharpe >= optCfg.MinSharpe;
                    if (passSharpe && (improvedSharpe || lowerVol))
                    {
                        _portfolio.UpdatePerformanceSnapshot(inst.Id, inst.TotalReturn, r.Sharpe, r.MaxDrawdown, inst.TotalTrades, inst.WinningTrades, r.Volatility);
                    }
                }
            }
            _portfolio.UpdateProgress(inst.Id, inst.ProcessedBars, inst.TargetBars, inst.AccountType == AccountType.Live ? StrategyStage.LiveRunning : StrategyStage.PaperRunning);
        }
    }

    private void OnReadyChanged()
    {
        if (_enabled && _ready.TradingReady)
        {
            _ = RunAsync(_cts?.Token ?? CancellationToken.None);
        }
    }
}
