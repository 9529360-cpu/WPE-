using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 模拟订单执行器 - 本地模拟成交,不调用真实API
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. 本地模拟订单成交
/// 2. 模拟滑点和手续费
/// 3. 更新模拟账户余额
/// 4. 创建持仓记录
/// 5. 保存订单历史到数据库
/// </remarks>
public class SimulatedOrderExecutor
{
    private readonly BinanceApiClient _apiClient;
    private readonly DataCacheService _cache;

    /// <summary>
    /// 手续费率配置
    /// </summary>
    private static class FeeRates
    {
        /// <summary>
        /// Taker手续费率 (0.04%)
        /// </summary>
        public const double Taker = 0.0004;

        /// <summary>
        /// Maker手续费率 (0.02%)
        /// </summary>
        public const double Maker = 0.0002;
    }

    public SimulatedOrderExecutor(BinanceApiClient apiClient, DataCacheService cache)
    {
        _apiClient = apiClient;
        _cache = cache;
    }

    /// <summary>
    /// 执行模拟订单
    /// </summary>
    public async Task<OrderExecutionResult> ExecuteAsync(
        OrderRequest request,
        TradingAccount account,
        CancellationToken ct = default)
    {
        try
        {
            LogService.Info("模拟订单执行: {Symbol} {Side} {Quantity} @ {Price}",
                request.Symbol, request.Side, request.Quantity, request.Price);

            // 1. 获取当前市场价格
            double currentPrice = await GetMarketPriceAsync(request.Symbol, ct);

            // 2. 模拟订单成交
            double executedPrice = SimulateExecution(request, currentPrice);
            double commission = CalculateCommission((double)request.Quantity, executedPrice, request.Type);

            // 3. 验证账户余额
            double orderCost = (double)request.Quantity * executedPrice + commission;
            if (request.Side == OrderSide.Buy)
            {
                if (account.AvailableBalance < (decimal)orderCost)
                {
                    return OrderExecutionResult.Failed("余额不足");
                }

                // 扣除可用余额
                account.AvailableBalance -= (decimal)orderCost;
                account.LockedBalance += (decimal)orderCost;
            }
            else
            {
                // 卖出增加可用余额
                account.AvailableBalance += (decimal)orderCost;
            }

            // 4. 创建持仓
            var position = new Position
            {
                Symbol = request.Symbol,
                Side = request.Side,
                Quantity = (double)request.Quantity,
                EntryPrice = executedPrice,
                CurrentPrice = executedPrice,
                StopLoss = (double)request.StopPrice,
                TakeProfit = 0, // 止盈价格 (后续可配置)
                OpenTime = DateTime.UtcNow,
                Status = PositionStatus.Open
            };
            account.Positions.Add(position);

            // 5. 创建订单记录
            var order = new Order
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Symbol = request.Symbol,
                Side = request.Side,
                Type = ConvertOrderType(request.Type),
                Price = (double)request.Price,
                Quantity = (double)request.Quantity,
                ExecutedPrice = executedPrice,
                ExecutedQuantity = (double)request.Quantity,
                Commission = commission,
                Status = OrderStatus.Filled,
                CreateTime = DateTime.UtcNow,
                UpdateTime = DateTime.UtcNow
            };
            account.OrderHistory.Add(order);

            // 6. 保存到数据库
            await SaveOrderToDatabase(order, ct);

            LogService.Info("✅ 模拟订单成交: {Symbol} {Side} {Quantity} @ {Price} (手续费: {Commission})",
                request.Symbol, request.Side, request.Quantity, executedPrice, commission);

            return OrderExecutionResult.Success(order.OrderId, executedPrice, (double)request.Quantity, commission);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "❌ 模拟订单执行失败: {Symbol}", request.Symbol);
            return OrderExecutionResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// 获取市场价格
    /// </summary>
    private async Task<double> GetMarketPriceAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var tickers = await _apiClient.GetMiniTickersAsync(new[] { symbol }, ct);
            var ticker = tickers.FirstOrDefault();
            return ticker?.LastPrice ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 模拟订单成交价格 (考虑滑点)
    /// </summary>
    private double SimulateExecution(OrderRequest request, double marketPrice)
    {
        // 市价单: 使用市场价格 + 滑点
        if (request.Type == OrderType.Market)
        {
            double slippage = marketPrice * 0.0005; // 0.05% 滑点
            return request.Side == OrderSide.Buy
                ? marketPrice + slippage  // 买入价格略高
                : marketPrice - slippage; // 卖出价格略低
        }

        // 限价单: 使用委托价格 (假设立即成交)
        if (request.Type == OrderType.Limit)
        {
            return (double)request.Price;
        }

        // 其他类型: 使用市场价格
        return marketPrice;
    }

    /// <summary>
    /// 计算手续费
    /// </summary>
    private double CalculateCommission(double quantity, double price, OrderType orderType)
    {
        // 市价单使用Taker费率,限价单使用Maker费率
        double feeRate = orderType == OrderType.Market ? FeeRates.Taker : FeeRates.Maker;
        return quantity * price * feeRate;
    }

    /// <summary>
    /// 保存订单到数据库
    /// </summary>
    private async Task SaveOrderToDatabase(Order order, CancellationToken ct)
    {
        var record = new OrderHistoryRecord
        {
            OrderId = order.OrderId,
            Symbol = order.Symbol,
            Side = order.Side.ToString(),
            Type = order.Type.ToString(),
            Quantity = order.Quantity,
            Price = order.Price,
            Status = "FILLED",
            FilledQuantity = order.ExecutedQuantity,
            AvgFillPrice = order.ExecutedPrice,
            Commission = order.Commission,
            CreatedAt = order.CreateTime,
            UpdatedAt = order.UpdateTime,
            FilledAt = order.UpdateTime
        };

        await _cache.SaveOrderAsync(record);
    }

    /// <summary>
    /// 转换订单类型
    /// </summary>
    private Models.OrderType ConvertOrderType(OrderType type)
    {
        return type switch
        {
            OrderType.Market => Models.OrderType.Market,
            OrderType.Limit => Models.OrderType.Limit,
            _ => Models.OrderType.Market
        };
    }
}
