using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Application.Backtesting;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models; // for BacktestRequest
using 币安量化机器人.Models;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 策略生命周期管理：评估运行中策略表现，触发回测验证与自动替换
/// </summary>
public sealed class StrategyLifecycleManager
{
    private readonly StrategyPortfolioManager _portfolio;
    private readonly AI.AIStrategyGenerator _generator;
    private readonly EnhancedBacktestEngine _backtest;
    private readonly EventBus _eventBus;

    private DateTime _lastTick = DateTime.MinValue;

    public StrategyLifecycleManager(
        StrategyPortfolioManager portfolio,
        AI.AIStrategyGenerator generator,
        EnhancedBacktestEngine backtest,
        EventBus eventBus)
    {
        _portfolio = portfolio;
        _generator = generator;
        _backtest = backtest;
        _eventBus = eventBus;
    }

    /// <summary>
    /// 定期评估与替换（建议每5-10分钟调用一次）
    /// </summary>
    public async Task TickAsync(SystemState state, CancellationToken ct)
    {
        // 5分钟节流
        if ((DateTime.UtcNow - _lastTick) < TimeSpan.FromMinutes(5))
        {
            return;
        }
        _lastTick = DateTime.UtcNow;

        var cfg = ServiceLocator.TradingConfig.Autopilot;
        var running = _portfolio.GetRunningStrategies();
        if (running.Count == 0)
        {
            return;
        }

        foreach (var inst in running.ToList())
        {
            double minSharpe = cfg.Optimization.MinSharpe; // corrected path
            bool underSharpe = inst.SharpeRatio < minSharpe;
            bool overDrawdown = inst.MaxDrawdown >= cfg.DrawdownStopThreshold;
            bool stagnate = inst.TotalReturn < 0; // Simplified: cumulative drawdown
            if (!(underSharpe || overDrawdown || stagnate))
            {
                continue;
            }

            try
            {
                // 生成候选策略（同类、同标的优先）
                string symbol = inst.Symbols.FirstOrDefault() ?? "BTCUSDT";
                var candidate = await _generator.GenerateAsync(new[] { symbol }, ct);
                candidate.AccountType = inst.AccountType;

                // 回测最近14天
                DateTime end = DateTime.UtcNow;
                DateTime start = end.AddDays(-14);
                var strat = ServiceLocator.StrategyFactory.CreateFromInstance(candidate);
                var req = new BacktestRequest(symbol, start, end, strat);
                var res = await ServiceLocator.EnhancedBacktest.RunAsync(req, ct);

                // 验证门槛
                bool passSharpe = res.Sharpe >= cfg.Optimization.MinSharpe;
                bool passDD = res.MaxDrawdown <= cfg.DrawdownStopThreshold;
                bool win = res.WinRate >= ServiceLocator.TradingConfig.MinConfidence;
                bool better = res.Sharpe > inst.SharpeRatio || res.Volatility < inst.Volatility;

                if (passSharpe && passDD && win && better)
                {
                    // 替换：停止旧策略，添加新策略并启动
                    _portfolio.StopStrategy(inst.Id);
                    _portfolio.AddStrategy(candidate);
                    await _portfolio.StartStrategyAsync(candidate.Id, inst.AccountType == AccountType.Live ? StrategyStage.LiveRunning : StrategyStage.PaperRunning);

                    // 更新首次快照
                    _portfolio.UpdatePerformanceSnapshot(candidate.Id, (decimal)res.NetProfit, res.Sharpe, res.MaxDrawdown, 0, 0, res.Volatility);

                    // 发布替换事件
                    await _eventBus.PublishAsync(new StrategyReplacementEvent
                    {
                        OldStrategyId = inst.Id,
                        NewStrategyId = candidate.Id,
                        Symbol = symbol,
                        Reason = "自动替换：性能退化",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[StrategyLifecycle] 替换流程失败: {Name}", inst.Name);
            }
        }
    }
}
