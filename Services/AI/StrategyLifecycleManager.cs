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
    private readonly object _tickLock = new();

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
        ct.ThrowIfCancellationRequested();

        // 5分钟节流 (线程安全)
        lock (_tickLock)
        {
            if ((DateTime.UtcNow - _lastTick) < TimeSpan.FromMinutes(5))
            {
                return;
            }
            _lastTick = DateTime.UtcNow;
        }

        var cfg = ServiceLocator.TradingConfig?.Autopilot;
        if (cfg == null)
        {
            LogService.Warning("[StrategyLifecycle] Autopilot 配置未就绪，跳过 Tick");
            return;
        }

        var running = _portfolio.GetRunningStrategies();
        if (running == null || running.Count == 0)
        {
            return;
        }

        // ensure backtest and factory are available before performing replacements
        var backtestEngine = ServiceLocator.EnhancedBacktest;
        var strategyFactory = ServiceLocator.StrategyFactory;
        if (backtestEngine == null || strategyFactory == null)
        {
            LogService.Warning("[StrategyLifecycle] EnhancedBacktest or StrategyFactory not available, skipping replacements");
        }

        foreach (var inst in running.ToList())
        {
            ct.ThrowIfCancellationRequested();

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
                string symbol = inst.Symbols?.FirstOrDefault() ?? "BTCUSDT";
                var candidate = await _generator.GenerateAsync(new[] { symbol }, ct).ConfigureAwait(false);
                if (candidate == null)
                {
                    LogService.Warning("[StrategyLifecycle] 未生成候选策略: {Strategy}", inst.Id);
                    continue;
                }

                candidate.AccountType = inst.AccountType;

                if (backtestEngine == null || strategyFactory == null)
                {
                    LogService.Warning("[StrategyLifecycle] 无法执行回测/部署，因为运行时组件缺失");
                    continue;
                }

                // 回测最近14天
                DateTime end = DateTime.UtcNow;
                DateTime start = end.AddDays(-14);
                var strat = strategyFactory.CreateFromInstance(candidate);
                var req = new BacktestRequest(symbol, start, end, strat);
                var res = await backtestEngine.RunAsync(req, ct).ConfigureAwait(false);

                if (res == null)
                {
                    LogService.Warning("[StrategyLifecycle] 回测返回空结果: {Candidate}", candidate.Id);
                    continue;
                }

                // 验证门槛
                bool passSharpe = res.Sharpe >= cfg.Optimization.MinSharpe;
                bool passDD = res.MaxDrawdown <= cfg.DrawdownStopThreshold;
                bool win = res.WinRate >= ServiceLocator.TradingConfig.MinConfidence;
                bool better = res.Sharpe > inst.SharpeRatio || res.Volatility < inst.Volatility;

                if (passSharpe && passDD && win && better)
                {
                    // 替换：停止旧策略，添加新策略并启动
                    try
                    {
                        _portfolio.StopStrategy(inst.Id);
                        _portfolio.AddStrategy(candidate);
                        await _portfolio.StartStrategyAsync(candidate.Id, inst.AccountType == AccountType.Live ? StrategyStage.LiveRunning : StrategyStage.PaperRunning).ConfigureAwait(false);

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
                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, "[StrategyLifecycle] 应用替换策略失败: {Old}", inst.Id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("[StrategyLifecycle] Tick 被取消");
                break;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[StrategyLifecycle] 替换流程失败: {Name}", inst?.Name ?? "<unknown>");
            }
        }
    }
}
