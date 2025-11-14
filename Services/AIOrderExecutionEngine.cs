using System;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

/// <summary>
/// AI订单执行引擎 - 统一处理模拟和真实订单
/// </summary>
public class AIOrderExecutionEngine
{
    private readonly TradingAccountManager _accountManager;
    private readonly SimulatedOrderExecutor _simulatedExecutor;
    private readonly LiveOrderExecutor _liveExecutor;
    private readonly AIRiskManager _riskManager;

    // 全局交易闸门（由中央协调器设置）
    private ITradeGate? _globalGate;
    public void SetGlobalGate(ITradeGate gate) => _globalGate = gate;

    public AIOrderExecutionEngine(
        TradingAccountManager accountManager,
        BinanceApiClient apiClient,
        DataCacheService cacheService,
        AIRiskManager riskManager)
    {
        _accountManager = accountManager;
        _simulatedExecutor = new SimulatedOrderExecutor(apiClient, cacheService);

        // 使用 ServiceLocator 提供的 ResilienceService 与 RiskEngine
        _liveExecutor = new LiveOrderExecutor(apiClient, cacheService, ServiceLocator.GetResilienceService(), ServiceLocator.Risk);
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
            if (signal == null)
            {
                return OrderExecutionResult.Failed("信号为空");
            }

            // 1. 获取当前账户
            TradingAccount? account = _accountManager.ActiveAccount;
            if (account == null)
            {
                return OrderExecutionResult.Failed("没有激活的账户");
            }

            // respect cancellation early
            if (ct.IsCancellationRequested)
            {
                return OrderExecutionResult.Failed("已取消");
            }

            // 全局闸门检查
            if (_globalGate != null)
            {
                var gateDecision = _globalGate.Permit(signal, account);
                if (!gateDecision.Allowed)
                {
                    return OrderExecutionResult.Rejected($"全局拒绝: {gateDecision.Reason}");
                }
            }

            // basic signal validation
            if (string.IsNullOrWhiteSpace(signal.Symbol))
            {
                return OrderExecutionResult.Failed("信号缺少交易对");
            }

            if (double.IsNaN(signal.Confidence) || signal.Confidence < 0 || signal.Confidence > 1)
            {
                return OrderExecutionResult.Failed("信心度无效");
            }

            if (signal.EntryPrice <= 0)
            {
                return OrderExecutionResult.Failed("入口价格无效");
            }

            // 2. AI信号转订单请求
            OrderRequest orderRequest;
            try
            {
                orderRequest = ConvertSignalToOrder(signal, account);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "ConvertSignalToOrder 失败");
                return OrderExecutionResult.Failed("转换订单请求失败");
            }

            // 3. 风控检查 (再次确认)
            if (!_riskManager.ApproveSignal(signal))
            {
                return OrderExecutionResult.Rejected("风控拒绝");
            }

            // respect cancellation before execution
            if (ct.IsCancellationRequested)
            {
                return OrderExecutionResult.Failed("已取消");
            }

            // 4. 根据账户类型执行
            OrderExecutionResult result;
            if (account.Type == AccountType.Simulated)
            {
                result = await _simulatedExecutor.ExecuteAsync(orderRequest, account, ct).ConfigureAwait(false);
            }
            else
            {
                // For live execution add a small retry/backoff strategy for transient failures
                const int maxAttempts = 3;
                int attempt = 0;
                Exception? lastEx = null;
                while (attempt < maxAttempts && !ct.IsCancellationRequested)
                {
                    attempt++;
                    try
                    {
                        result = await _liveExecutor.ExecuteAsync(orderRequest, account, ct).ConfigureAwait(false);
                        // if executor returns failure but not exception, return it
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        return OrderExecutionResult.Failed("执行已取消");
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        LogService.Warning("LiveExecutor attempt {Attempt} failed: {Message}", attempt, ex.Message);

                        // exponential backoff with jitter
                        int delayMs = (int)(Math.Pow(2, attempt) * 250) + new Random().Next(50, 200);
                        try
                        {
                            await Task.Delay(delayMs, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return OrderExecutionResult.Failed("执行已取消");
                        }
                    }
                }

                // all attempts failed
                LogService.Error(lastEx ?? new Exception("LiveExecutor 未知错误"), "Live executor 多次尝试失败");
                return OrderExecutionResult.Failed(lastEx?.GetBaseException().Message ?? "下单失败（多次重试）");
            }

            // 5. 更新账户统计
            if (result.IsSuccess)
            {
                account.TotalTrades++;
                account.LastTradeAt = DateTime.UtcNow;
            }

            return result;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "订单执行引擎异常");
            return OrderExecutionResult.Failed(ex.GetBaseException().Message);
        }
    }

    /// <summary>
    /// AI信号转订单请求
    /// </summary>
    private OrderRequest ConvertSignalToOrder(AITradingSignal signal, TradingAccount account)
    {
        // safe checks
        if (signal == null)
        {
            throw new ArgumentNullException(nameof(signal));
        }
        if (account == null)
        {
            throw new ArgumentNullException(nameof(account));
        }

        // 计算实际下单数量 (根据账户可用余额和仓位比例)
        decimal availableBalance = account.AvailableBalance;
        if (availableBalance <= 0)
        {
            throw new InvalidOperationException("账户可用余额不足");
        }

        double posSize = double.IsNaN(signal.PositionSize) || signal.PositionSize <= 0 ? 0.01 : signal.PositionSize;
        decimal positionValue = (decimal)posSize * availableBalance; // how much USDT to allocate

        // guard entry price
        if (signal.EntryPrice <= 0)
        {
            throw new InvalidOperationException("Entry price invalid");
        }

        decimal price = (decimal)signal.EntryPrice;

        // compute quantity = positionValue / price
        decimal quantity = price > 0 ? Decimal.Divide(positionValue, price) : 0m;

        // cap by available balance / price (ensure we don't exceed funds)
        decimal maxAffordableQty = price > 0 ? Decimal.Divide(availableBalance, price) : 0m;
        if (quantity > maxAffordableQty)
        {
            quantity = maxAffordableQty;
        }

        // minimum safeguards
        const decimal MinQty = 0.0001m;
        if (quantity < MinQty)
        {
            quantity = MinQty;
        }

        // normalize quantity to reasonable precision (6 decimals)
        quantity = Math.Round(quantity, 6);

        // ensure stop price sensible
        decimal stopPrice = signal.StopLoss > 0 ? (decimal)signal.StopLoss : price * 0.99m;

        return new OrderRequest
        {
            Symbol = signal.Symbol,
            Side = signal.Action == SignalAction.Buy ? OrderSide.Buy : OrderSide.Sell,
            Type = OrderType.Limit, // 使用限价单减少滑点
            Quantity = quantity,
            Price = price,
            StopPrice = stopPrice,
            TimeInForce = TimeInForce.Gtc
        };
    }
}
