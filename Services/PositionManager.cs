using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

/// <summary>
/// 仓位管理器 - 监控所有持仓,自动止盈止损
/// </summary>
/// <remarks>
/// 核心职责:
/// 1. 实时监控所有持仓
/// 2. 更新持仓价格和盈亏
/// 3. 检查止盈止损条件
/// 4. 自动平仓
/// 5. 更新账户统计
/// </remarks>
public class PositionManager
{
    private readonly TradingAccountManager _accountManager;
    private readonly BinanceApiClient _apiClient;
    private readonly AIOrderExecutionEngine _executionEngine;
    private bool _isMonitoring;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 监控间隔 (毫秒)
    /// </summary>
    private const int MonitorIntervalMs = 1000; // 1秒

    /// <summary>
    /// 最大持仓时长 (小时)
    /// </summary>
    private const int MaxHoldingHours = 24;

    public PositionManager(
        TradingAccountManager accountManager,
        BinanceApiClient apiClient,
        AIOrderExecutionEngine executionEngine)
    {
        _accountManager = accountManager;
        _apiClient = apiClient;
        _executionEngine = executionEngine;
    }

    /// <summary>
    /// 是否正在监控
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// 启动仓位监控 (实时监控所有持仓)
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken ct = default)
    {
        if (_isMonitoring)
        {
            LogService.Warning("仓位监控已在运行中");
            return;
        }

        _isMonitoring = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        LogService.Info("🔍 仓位监控已启动");

        try
        {
            await MonitoringLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            LogService.Info("仓位监控已停止");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "仓位监控异常");
        }
        finally
        {
            _isMonitoring = false;
        }
    }

    /// <summary>
    /// 停止监控
    /// </summary>
    public void StopMonitoring()
    {
        _cts?.Cancel();
        _isMonitoring = false;
        LogService.Info("仓位监控已停止");
    }

    /// <summary>
    /// 监控循环
    /// </summary>
    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var account = _accountManager.ActiveAccount;
                if (account == null)
                {
                    await Task.Delay(MonitorIntervalMs, ct);
                    continue;
                }

                // 获取所有持仓中的仓位
                var openPositions = account.Positions
                    .Where(p => p.Status == PositionStatus.Open)
                    .ToList();

                if (openPositions.Count == 0)
                {
                    await Task.Delay(MonitorIntervalMs, ct);
                    continue;
                }

                // 逐个检查持仓
                foreach (var position in openPositions)
                {
                    // 1. 更新当前价格
                    await UpdatePositionPriceAsync(position, ct);

                    // 2. 计算盈亏
                    UpdatePositionPnL(position);

                    // 3. 检查是否需要平仓
                    if (ShouldClosePosition(position, out string? reason))
                    {
                        await ClosePositionAsync(position, reason, ct);
                    }
                }

                // 等待下一个周期
                await Task.Delay(MonitorIntervalMs, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Error(ex, "仓位监控周期异常");
                await Task.Delay(5000, ct); // 异常后等5秒
            }
        }
    }

    /// <summary>
    /// 更新持仓价格
    /// </summary>
    private async Task UpdatePositionPriceAsync(Position position, CancellationToken ct)
    {
        try
        {
            var tickers = await _apiClient.GetMiniTickersAsync(new[] { position.Symbol }, ct);
            var ticker = tickers.FirstOrDefault();

            if (ticker != null)
            {
                position.CurrentPrice = ticker.LastPrice;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "更新持仓价格失败: {Symbol}", position.Symbol);
        }
    }

    /// <summary>
    /// 更新持仓盈亏
    /// </summary>
    private void UpdatePositionPnL(Position position)
    {
        // 计算价格差
        double priceDiff = position.CurrentPrice - position.EntryPrice;

        // 做空时价格差反向
        if (position.Side == OrderSide.Sell)
        {
            priceDiff = -priceDiff;
        }

        // 未实现盈亏 = 数量 * 价格差
        position.UnrealizedPnL = position.Quantity * priceDiff;

        // 盈亏百分比
        position.PnLPercent = position.EntryPrice == 0 ? 0 : priceDiff / position.EntryPrice;
    }

    /// <summary>
    /// 判断是否应该平仓
    /// </summary>
    private bool ShouldClosePosition(Position position, out string reason)
    {
        // 1. 止损检查
        if (position.StopLoss > 0)
        {
            bool hitStopLoss = position.Side == OrderSide.Buy
                ? position.CurrentPrice <= position.StopLoss
                : position.CurrentPrice >= position.StopLoss;

            if (hitStopLoss)
            {
                reason = $"触发止损 ({position.StopLoss:F4})";
                return true;
            }
        }

        // 2. 止盈检查
        if (position.TakeProfit > 0)
        {
            bool hitTakeProfit = position.Side == OrderSide.Buy
                ? position.CurrentPrice >= position.TakeProfit
                : position.CurrentPrice <= position.TakeProfit;

            if (hitTakeProfit)
            {
                reason = $"触发止盈 ({position.TakeProfit:F4})";
                return true;
            }
        }

        // 3. 时间止损 (持仓超过最大时长)
        if (position.HoldingHours > MaxHoldingHours)
        {
            reason = $"持仓时间过长 ({position.HoldingHours:F1}小时)";
            return true;
        }

        // 4. 极端亏损止损 (亏损超过10%)
        if (position.PnLPercent < -0.10)
        {
            reason = $"极端亏损止损 ({position.PnLPercent:P2})";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>
    /// 平仓
    /// </summary>
    private async Task ClosePositionAsync(Position position, string reason, CancellationToken ct)
    {
        try
        {
            LogService.Info("📤 平仓: {Symbol} {Side} {Quantity} @ {CurrentPrice} (原因: {Reason}, 盈亏: {PnL:F2})",
                position.Symbol, position.Side, position.Quantity,
                position.CurrentPrice, reason, position.UnrealizedPnL);

            // 生成平仓信号 (反向操作)
            var closeSignal = new AITradingSignal
            {
                Symbol = position.Symbol,
                Action = position.Side == OrderSide.Buy ? SignalAction.Sell : SignalAction.Buy,
                Confidence = 1.0, // 平仓是强制的
                Reason = reason,
                EntryPrice = position.CurrentPrice,
                TargetPrice = 0,
                StopLoss = 0,
                PositionSize = 0.01, // 最小仓位
                Timeframe = "IMMEDIATE",
                RiskLevel = "LOW",
                Timestamp = DateTime.UtcNow,
                RawAnalysis = $"自动平仓: {reason}"
            };

            // 执行平仓
            var result = await _executionEngine.ExecuteSignalAsync(closeSignal, ct);

            if (result.IsSuccess)
            {
                // 更新持仓状态
                position.Status = PositionStatus.Closed;
                position.ClosePrice = position.CurrentPrice;
                position.CloseTime = DateTime.UtcNow;
                position.RealizedPnL = position.UnrealizedPnL;

                // 更新账户统计
                var account = _accountManager.ActiveAccount;
                if (account != null)
                {
                    account.TotalPnL += (decimal)position.RealizedPnL;
                    account.TodayPnL += (decimal)position.RealizedPnL;

                    if (position.RealizedPnL > 0)
                    {
                        account.WinningTrades++;
                    }
                    else
                    {
                        account.LosingTrades++;
                    }

                    account.LastTradeAt = DateTime.UtcNow;
                }

                LogService.Info("✅ 平仓成功: {Symbol} 盈亏={PnL:F2} ({PnLPercent:P2})",
                    position.Symbol, position.RealizedPnL, position.PnLPercent);
            }
            else
            {
                LogService.Error("❌ 平仓失败: {Symbol} {Error}",
                    position.Symbol, result.Error);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "平仓异常: {Symbol}", position.Symbol);
        }
    }

    /// <summary>
    /// 手动平仓指定持仓
    /// </summary>
    public async Task<OrderExecutionResult> ClosePositionManuallyAsync(
        Position position,
        CancellationToken ct = default)
    {
        return await ClosePositionWithResultAsync(position, "手动平仓", ct);
    }

    /// <summary>
    /// 平仓并返回结果
    /// </summary>
    private async Task<OrderExecutionResult> ClosePositionWithResultAsync(
        Position position,
        string reason,
        CancellationToken ct)
    {
        await ClosePositionAsync(position, reason, ct);

        return OrderExecutionResult.Success(
            position.Id,
            position.CurrentPrice,
            position.Quantity,
            0
        );
    }

    /// <summary>
    /// 平仓所有持仓
    /// </summary>
    public async Task CloseAllPositionsAsync(string reason = "批量平仓")
    {
        var account = _accountManager.ActiveAccount;
        if (account == null)
        {
            return;
        }

        var openPositions = account.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToList();

        LogService.Info("开始平仓所有持仓: 共{Count}个", openPositions.Count);

        foreach (var position in openPositions)
        {
            await ClosePositionAsync(position, reason, CancellationToken.None);
        }

        LogService.Info("✅ 所有持仓已平仓");
    }
}
