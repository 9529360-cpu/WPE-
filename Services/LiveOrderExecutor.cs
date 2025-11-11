using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 真实订单执行器 - 调用Binance API真实下单
/// </summary>
/// <remarks>
/// ⚠️ 警告: 此执行器会调用真实API,真金白银!
/// 
/// 核心功能:
/// 1. 调用Binance API真实下单
/// 2. 处理订单响应
/// 3. 更新真实账户余额
/// 4. 创建持仓记录
/// 5. 保存订单历史到数据库
/// </remarks>
public class LiveOrderExecutor
{
    private readonly BinanceApiClient _apiClient;
    private readonly DataCacheService _cache;

    public LiveOrderExecutor(BinanceApiClient apiClient, DataCacheService cache)
    {
        _apiClient = apiClient;
        _cache = cache;
    }

    /// <summary>
    /// 执行真实订单
    /// </summary>
    public async Task<OrderExecutionResult> ExecuteAsync(
        OrderRequest request,
        TradingAccount account,
        CancellationToken ct = default)
    {
        try
        {
            // ⚠️ 真实下单警告
            LogService.Warning("⚠️ 真实订单执行: {Symbol} {Side} {Quantity} @ {Price}",
                request.Symbol, request.Side, request.Quantity, request.Price);

            // 1. 调用Binance API下单
            OrderResponse response = await _apiClient.PlaceOrderAsync(request, ct);

            // 2. 检查订单状态
            if (response.Status == "FILLED")
            {
                // 3. 更新账户余额 (从Binance获取最新余额)
                await UpdateAccountBalanceAsync(account, ct);

                // 4. 创建持仓
                var position = new Position
                {
                    Symbol = request.Symbol,
                    Side = request.Side,
                    Quantity = (double)response.ExecutedQuantity,
                    EntryPrice = (double)response.AvgPrice,
                    CurrentPrice = (double)response.AvgPrice,
                    StopLoss = (double)request.StopPrice,
                    TakeProfit = 0,
                    OpenTime = response.Time,
                    Status = PositionStatus.Open
                };
                account.Positions.Add(position);

                // 5. 保存到数据库
                await SaveOrderToDatabase(response, request, ct);

                LogService.Info("✅ 真实订单成交: {Symbol} {Side} {Quantity} @ {Price}",
                    request.Symbol, request.Side, response.ExecutedQuantity, response.AvgPrice);

                return OrderExecutionResult.Success(
                    response.OrderId.ToString(),
                    (double)response.AvgPrice,
                    (double)response.ExecutedQuantity,
                    0 // TODO: 从response获取手续费
                );
            }
            else
            {
                LogService.Warning("订单未完全成交: {OrderId} 状态={Status}",
                    response.OrderId, response.Status);

                return OrderExecutionResult.Failed($"订单状态: {response.Status}");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "❌ 真实订单执行失败: {Symbol}", request.Symbol);
            return OrderExecutionResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// 更新账户余额 (从Binance获取)
    /// </summary>
    private async Task UpdateAccountBalanceAsync(TradingAccount account, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<AccountBalance> balances = await _apiClient.GetAccountBalancesAsync(ct);
            AccountBalance? usdtBalance = balances.FirstOrDefault(b => b.Asset == "USDT");

            if (usdtBalance != null)
            {
                account.CurrentBalance = usdtBalance.WalletBalance;
                account.AvailableBalance = usdtBalance.AvailableBalance;

                LogService.Debug("账户余额已更新: 可用={Available} USDT",
                    account.AvailableBalance);
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
            Commission = 0, // TODO: 从response获取
            CreatedAt = response.Time,
            UpdatedAt = response.Time,
            FilledAt = response.Time
        };

        await _cache.SaveOrderAsync(record);
    }
}
