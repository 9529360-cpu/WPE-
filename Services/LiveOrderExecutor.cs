using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.Resilience;
using 币安量化机器人.Services.Risk;

namespace 币安量化机器人.Services;

/// <summary>
/// 真实订单执行器 - 调用Binance API真实下单
/// </summary>
public class LiveOrderExecutor
{
    private readonly BinanceApiClient _apiClient;
    private readonly DataCacheService _cache;
    private readonly ResilienceService _resilience;
    private readonly RiskEngine _riskEngine;

    public LiveOrderExecutor(BinanceApiClient apiClient, DataCacheService cache, ResilienceService resilience, RiskEngine riskEngine)
    {
        _apiClient = apiClient;
        _cache = cache;
        _resilience = resilience;
        _riskEngine = riskEngine;
    }

    /// <summary>
    /// 执行真实订单
    /// </summary>
    public async Task<OrderExecutionResult> ExecuteAsync(
        OrderRequest request,
        TradingAccount account,
        CancellationToken ct = default)
    {
        if (request == null)
        {
            return OrderExecutionResult.Failed("OrderRequest 不能为空");
        }
        if (account == null)
        {
            return OrderExecutionResult.Failed("账户信息缺失");
        }
        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            return OrderExecutionResult.Failed("订单缺少交易对");
        }
        if (request.Quantity <= 0 || request.Price <= 0)
        {
            return OrderExecutionResult.Failed("下单数量或价格无效");
        }

        // 预下单风控检查（使用 RiskEngine.PreTradeCheckAsync）
        TradePermitResult permit;
        try
        {
            permit = await _riskEngine.PreTradeCheckAsync(request, account, ct).ConfigureAwait(false);
            if (permit == null)
            {
                permit = TradePermitResult.Deny("风控检查返回空结果");
            }
        }
        catch (OperationCanceledException)
        {
            return OrderExecutionResult.Failed("风控检查已取消");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "执行风控检查时出错，取消下单");
            return OrderExecutionResult.Failed("风控检查异常");
        }

        if (!permit.Allowed)
        {
            LogService.Warning("风控拒绝下单: {Reason}", permit.Reason);

            // 异步发送通知（如果配置了Telegram） - 不阻塞下单路径
            try
            {
                string? bot = Environment.GetEnvironmentVariable("NOTIFY_TELEGRAM_BOT_TOKEN");
                string? chat = Environment.GetEnvironmentVariable("NOTIFY_TELEGRAM_CHAT_ID");
                if (!string.IsNullOrWhiteSpace(bot) && !string.IsNullOrWhiteSpace(chat))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ServiceLocator.Notification.SendTelegramAsync(bot, chat, $"风控拒绝下单: {request.Symbol} - {permit.Reason}").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            LogService.Error(ex, "发送风控拒单通知失败");
                        }
                    });
                }
            }
            catch { }

            // 记录拒单到风控记录
            await _riskEngine.PostTradeRecordAsync(null, request, account).ConfigureAwait(false);

            return OrderExecutionResult.Rejected(permit.Reason);
        }

        // 幂等 clientOrderId 支持
        request.ClientOrderId ??= $"local-{Guid.NewGuid():N}";

        LogService.Warning("真实订单执行请求: {Symbol} {Side} {Quantity} @ {Price} (ClientId={ClientId})",
            request.Symbol, request.Side, request.Quantity, request.Price, request.ClientOrderId);

        OrderResponse? response = null;
        try
        {
            // 使用 ResilienceService 包装下单请求
            response = await _resilience.ExecuteAsync(async ct =>
            {
                return await _apiClient.PlaceOrderAsync(request, ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);

            // 处理响应逻辑与数据库记录
            if (string.Equals(response.Status, "FILLED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(response.Status, "PARTIALLY_FILLED", StringComparison.OrdinalIgnoreCase))
            {
                // 更新余额/持仓并记录
                _ = UpdateAccountBalanceAsync(account, ct);
                try
                {
                    var position = new Position
                    {
                        Symbol = request.Symbol,
                        Side = request.Side,
                        Quantity = (double)response.ExecutedQuantity,
                        EntryPrice = (double)(response.AvgPrice == 0 ? response.Price : response.AvgPrice),
                        CurrentPrice = (double)(response.AvgPrice == 0 ? response.Price : response.AvgPrice),
                        StopLoss = (double)request.StopPrice,
                        TakeProfit = 0,
                        OpenTime = response.Time,
                        Status = PositionStatus.Open
                    };

                    account.Positions.Add(position);
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "创建持仓记录失败");
                }

                try
                {
                    await SaveOrderToDatabase(response, request, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "保存订单到数据库失败");
                }

                // 计算并记录成交的 realized PnL（若适用）
                try
                {
                    var trade = new TradeRecord
                    {
                        TradeId = Guid.NewGuid().ToString("N"),
                        OrderId = response.OrderId.ToString(),
                        Symbol = request.Symbol,
                        Side = request.Side.ToString(),
                        Quantity = (double)response.ExecutedQuantity,
                        Price = (double)(response.AvgPrice == 0 ? response.Price : response.AvgPrice),
                        Commission = 0,
                        Timestamp = response.Time
                    };

                    // 简单 FIFO 实现: 如果是平仓（SELL）且有持仓则计算 realized pnl
                    if (request.Side == OrderSide.Sell)
                    {
                        var open = account.Positions.FirstOrDefault(p => p.Symbol == request.Symbol && p.Status == PositionStatus.Open && p.Side == OrderSide.Buy);
                        if (open != null)
                        {
                            double closedQty = Math.Min(open.Quantity, trade.Quantity);
                            // compute commission and PnL using trading config
                            var cfg = ServiceLocator.TradingConfig;
                            double execCommission = cfg.FeeRate * closedQty * trade.Price * cfg.ContractMultiplier;
                            double pnl = (trade.Price - open.EntryPrice) * closedQty * cfg.ContractMultiplier;
                            if (cfg.UseLeverage && cfg.DefaultLeverage > 1.0)
                            {
                                pnl *= cfg.DefaultLeverage;
                            }
                            pnl -= execCommission;
                            // produce a new TradeRecord with RealizedPnl set because properties are init-only
                            var tradeWithPnl = new TradeRecord
                            {
                                TradeId = trade.TradeId,
                                OrderId = trade.OrderId,
                                Symbol = trade.Symbol,
                                Side = trade.Side,
                                Quantity = trade.Quantity,
                                Price = trade.Price,
                                Commission = execCommission,
                                RealizedPnl = pnl,
                                Timestamp = trade.Timestamp
                            };

                            if (closedQty >= open.Quantity)
                            {
                                open.Status = PositionStatus.Closed;
                                open.ClosePrice = trade.Price;
                                open.CloseTime = trade.Timestamp;
                                open.RealizedPnL = pnl;
                                open.Quantity = 0;
                            }
                            else
                            {
                                // partial close: reduce quantity and update realized pnl
                                open.Quantity -= closedQty;
                                open.RealizedPnL += pnl;
                            }

                            await ServiceLocator.OrderHistory.RecordTradeAsync(tradeWithPnl ?? trade).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "记录成交 PnL 失败");
                }

                double executedQty = (double)response.ExecutedQuantity;
                double executedPrice = (double)(response.AvgPrice == 0 ? response.Price : response.AvgPrice);
                double commission = executedQty * executedPrice * 0.0004;

                LogService.Info("真实订单成交: {Symbol} {Side} {Quantity} @ {Price} (Status={Status})",
                    request.Symbol, request.Side, response.ExecutedQuantity, response.AvgPrice, response.Status);

                return OrderExecutionResult.Success(response.OrderId.ToString(), executedPrice, executedQty, commission);
            }

            LogService.Warning("订单未完全成交: {OrderId} 状态={Status}", response.OrderId, response.Status);
            return OrderExecutionResult.Failed($"订单状态: {response.Status}");
        }
        catch (OperationCanceledException)
        {
            return OrderExecutionResult.Failed("执行已取消");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "LiveOrderExecutor 执行下单失败");
            return OrderExecutionResult.Failed(ex.GetBaseException().Message ?? "下单失败");
        }
        finally
        {
            try
            {
                await _riskEngine.PostTradeRecordAsync(response, request, account).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "PostTradeRecordAsync 调用失败");
            }
        }
    }

    /// <summary>
    /// 更新账户余额 (从Binance获取)
    /// </summary>
    private async Task UpdateAccountBalanceAsync(TradingAccount account, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<AccountBalance> balances = await _apiClient.GetAccountBalancesAsync(ct).ConfigureAwait(false);
            AccountBalance? usdtBalance = balances.FirstOrDefault(b => string.Equals(b.Asset, "USDT", StringComparison.OrdinalIgnoreCase));

            if (usdtBalance != null)
            {
                account.CurrentBalance = usdtBalance.WalletBalance;
                account.AvailableBalance = usdtBalance.AvailableBalance;

                LogService.Debug("账户余额已更新: 可用={Available} USDT", account.AvailableBalance);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "更新账户余额失败");
        }
    }

    /// <summary>
    /// 保存订单到数据库
    /// </summary>
    private async Task SaveOrderToDatabase(
        OrderResponse response,
        OrderRequest request,
        CancellationToken ct)
    {
        var record = new OrderHistoryRecord
        {
            OrderId = response.OrderId.ToString(),
            Symbol = request.Symbol,
            Side = request.Side.ToString(),
            Type = request.Type.ToString(),
            Quantity = (double)request.Quantity,
            Price = (double)request.Price,
            Status = response.Status,
            FilledQuantity = (double)response.ExecutedQuantity,
            AvgFillPrice = (double)response.AvgPrice,
            Commission = 0,
            CreatedAt = response.Time,
            UpdatedAt = response.Time,
            FilledAt = response.Time
        };

        await _cache.SaveOrderAsync(record).ConfigureAwait(false);
    }
}
