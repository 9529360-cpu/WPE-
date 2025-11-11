using System;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

/// <summary>
/// AI订单执行引擎 - 统一处理模拟和真实订单
/// </summary>
/// <remarks>
/// 核心职责:
/// 1. AI信号转订单请求
/// 2. 风控检查
/// 3. 根据账户类型选择执行器 (模拟/真实)
/// 4. 更新账户状态
/// 5. 记录执行结果
/// </remarks>
public class AIOrderExecutionEngine
{
    private readonly TradingAccountManager _accountManager;
    private readonly SimulatedOrderExecutor _simulatedExecutor;
    private readonly LiveOrderExecutor _liveExecutor;
    private readonly AIRiskManager _riskManager;

    public AIOrderExecutionEngine(
        TradingAccountManager accountManager,
        BinanceApiClient apiClient,
        DataCacheService cacheService,
        AIRiskManager riskManager)
    {
        _accountManager = accountManager;
        _simulatedExecutor = new SimulatedOrderExecutor(apiClient, cacheService);
        _liveExecutor = new LiveOrderExecutor(apiClient, cacheService);
        _riskManager = riskManager;
    }

    /// <summary>
    /// 执行AI生成的交易信号
    /// </summary>
    public async Task<OrderExecutionResult> ExecuteSignalAsync(
        AITradingSignal signal,
        CancellationToken ct = default)
    {
        try
        {
            // 1. 获取当前账户
            TradingAccount? account = _accountManager.ActiveAccount;
            if (account == null)
            {
                return OrderExecutionResult.Failed("没有激活的账户");
            }

            // 2. AI信号转订单请求
            OrderRequest orderRequest = ConvertSignalToOrder(signal, account);

            // 3. 风控检查
            if (!_riskManager.ApproveSignal(signal))
            {
                LogService.Warning("订单被风控拒绝: {Symbol} {Action} 信心度={Confidence}",
                    signal.Symbol, signal.Action, signal.Confidence);

                return OrderExecutionResult.Rejected("风控拒绝");
            }

            // 4. 根据账户类型执行
            OrderExecutionResult result;
            if (account.Type == AccountType.Simulated)
            {
                LogService.Info("使用模拟账户执行");
                result = await _simulatedExecutor.ExecuteAsync(orderRequest, account, ct);
            }
            else
            {
                LogService.Warning("⚠️ 使用真实账户执行");
                result = await _liveExecutor.ExecuteAsync(orderRequest, account, ct);
            }

            // 5. 更新账户统计
            if (result.IsSuccess)
            {
                account.TotalTrades++;
                account.LastTradeAt = DateTime.UtcNow;

                LogService.Info("✅ 订单执行成功: {Account} {OrderId} @ {Price}",
                    account.Name, result.OrderId, result.ExecutedPrice);
            }

            return result;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "订单执行引擎异常");
            return OrderExecutionResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// AI信号转订单请求
    /// </summary>
    private OrderRequest ConvertSignalToOrder(AITradingSignal signal, TradingAccount account)
    {
        // 计算实际下单数量 (根据账户可用余额和仓位比例)
        double availableBalance = (double)account.AvailableBalance;
        double positionValue = availableBalance * signal.PositionSize;
        decimal quantity = (decimal)(positionValue / signal.EntryPrice);

        // 限制最小下单量 (避免太小)
        if (quantity < 0.001m)
        {
            quantity = 0.001m;
        }

        return new OrderRequest
        {
            Symbol = signal.Symbol,
            Side = signal.Action == SignalAction.Buy ? OrderSide.Buy : OrderSide.Sell,
            Type = OrderType.Limit, // 使用限价单减少滑点
            Quantity = quantity,
            Price = (decimal)signal.EntryPrice,
            StopPrice = (decimal)signal.StopLoss,
            TimeInForce = TimeInForce.Gtc
        };
    }
}
