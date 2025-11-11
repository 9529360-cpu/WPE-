using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;
using 币安量化机器人.Models.Configuration; // 🆕 配置类引用
using 币安量化机器人.Services.AI; // 🆕 AI事件引用

namespace 币安量化机器人.Application.Backtesting;

/// <summary>
/// 增强回测引擎 - 真实订单撮合 + 滑点 + 手续费
/// </summary>
/// <remarks>
/// 使用真实的订单撮合逻辑、滑点计算和手续费模拟
/// 提供更接近实盘的回测结果
/// 支持事件通知到中央AI协调器
/// </remarks>
public class EnhancedBacktestEngine : IBacktestEngine
{
    private readonly OrderMatcher _orderMatcher;
    private readonly CostCalculator _costCalculator;
    private readonly PerformanceCalculator _perfCalculator;
    private readonly BacktestConfig _config;

    // 🆕 事件总线（可选，用于通知AI协调器）
    private EventBus? _eventBus;

    /// <summary>
    /// 创建增强回测引擎
    /// </summary>
    /// <param name="slippageCalculator">滑点计算器 (可选)</param>
    /// <param name="costCalculator">成本计算器 (可选)</param>
    /// <param name="perfCalculator">绩效计算器 (可选)</param>
    /// <param name="config">回测配置 (可选,使用默认配置)</param>
    /// <param name="eventBus">事件总线 (可选,用于通知AI协调器)</param>
    public EnhancedBacktestEngine(
        SlippageCalculator? slippageCalculator = null,
        CostCalculator? costCalculator = null,
        PerformanceCalculator? perfCalculator = null,
        BacktestConfig? config = null,
        EventBus? eventBus = null)
    {
        _config = config ?? new BacktestConfig();
        _eventBus = eventBus;

        // 使用配置创建计算器
        slippageCalculator ??= new SlippageCalculator(
            baseSlippage: _config.BaseSlippage,
            impactFactor: _config.ImpactFactor,
            spreadMultiplier: _config.SpreadMultiplier
        );

        _orderMatcher = new OrderMatcher(slippageCalculator);

        _costCalculator = costCalculator ?? new CostCalculator(
            makerFeeRate: _config.MakerFeeRate,
            takerFeeRate: _config.TakerFeeRate,
            fundingRateAvg: _config.AvgFundingRate
        );

        _perfCalculator = perfCalculator ?? new PerformanceCalculator();
    }

    /// <summary>
    /// 🆕 设置事件总线（用于动态绑定）
    /// </summary>
    public void SetEventBus(EventBus eventBus)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// 运行回测
    /// </summary>
    /// <param name="request">回测请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>回测结果</returns>
    public async ValueTask<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        double initialCapital = _config.InitialCapital;
        double currentEquity = initialCapital;
        var position = new BacktestPosition();
        var completedTrades = new List<BacktestTrade>();
        var allSignals = new List<TradeSignal>();

        // 生成市场数据
        List<MarketObservation> marketData = await GenerateMarketDataAsync(request.Symbol, request.Start, request.End, cancellationToken);

        // 运行策略
        await foreach (MarketObservation? observation in AsAsyncEnumerable(marketData).WithCancellation(cancellationToken))
        {
            StrategyDecision decision = await request.Strategy.EvaluateAsync(observation, cancellationToken);
            allSignals.Add(new TradeSignal(request.Symbol, decision.Action, decision.Confidence, decision.MlSignal));

            // 处理信号
            switch (decision.Action.ActionType)
            {
                case TradeActionType.EnterLong when !position.IsOpen:
                    position = OpenPosition(observation, decision.Action.Quantity, isLong: true);
                    break;

                case TradeActionType.EnterShort when !position.IsOpen:
                    position = OpenPosition(observation, decision.Action.Quantity, isLong: false);
                    break;

                case TradeActionType.Exit when position.IsOpen:
                    BacktestTrade trade = ClosePosition(position, observation, decision.Action.Reason);
                    completedTrades.Add(trade);
                    currentEquity += trade.NetPnL;
                    position = new BacktestPosition();
                    break;
            }
        }

        // 强制平仓未平仓的持仓
        if (position.IsOpen && marketData.Count > 0)
        {
            MarketObservation lastObservation = marketData[^1];
            BacktestTrade forcedTrade = ClosePosition(position, lastObservation, "Forced close at end");
            completedTrades.Add(forcedTrade);
            currentEquity += forcedTrade.NetPnL;
        }

        // 计算绩效
        PerformanceMetrics metrics = _perfCalculator.Calculate(completedTrades, initialCapital);

        var result = new BacktestResult(
            Strategy: request.Strategy.Name,
            NetProfit: metrics.NetProfit,
            Sharpe: metrics.SharpeRatio,
            Sortino: metrics.SortinoRatio,
            MaxDrawdown: metrics.MaxDrawdownPercent / 100,
            Calmar: metrics.CalmarRatio,
            WinRate: metrics.WinRate,
            ProfitFactor: metrics.ProfitFactor,
            Signals: allSignals
        );

        // 🆕 发布回测完成事件
        if (_eventBus != null)
        {
            var evt = new BacktestCompletedEvent
            {
                StrategyName = request.Strategy.Name,
                Symbol = request.Symbol,
                StartTime = request.Start,
                EndTime = request.End,
                TotalReturn = (currentEquity - initialCapital) / initialCapital,
                SharpeRatio = metrics.SharpeRatio,
                MaxDrawdown = metrics.MaxDrawdownPercent / 100,
                WinRate = metrics.WinRate,
                TotalTrades = completedTrades.Count,
                ProfitFactor = metrics.ProfitFactor,
                Timestamp = DateTime.UtcNow
            };

            await _eventBus.PublishAsync(evt);
        }

        return result;
    }

    /// <summary>
    /// 开仓
    /// </summary>
    private BacktestPosition OpenPosition(MarketObservation market, double quantity, bool isLong)
    {
        // 模拟市价单开仓
        var order = new SimulatedOrder
        {
            Symbol = market.Symbol,
            Side = isLong ? OrderSide.Buy : OrderSide.Sell,
            Type = SimulatedOrderType.Market,
            Quantity = quantity,
            PlacedAt = market.Timestamp
        };

        OrderFillResult fillResult = _orderMatcher.MatchOrder(order, market);

        return new BacktestPosition
        {
            IsOpen = true,
            IsLong = isLong,
            Symbol = market.Symbol,
            EntryTime = fillResult.FillTime,
            EntryPrice = fillResult.AvgFillPrice,
            Quantity = fillResult.FilledQuantity,
            EntrySlippage = fillResult.Slippage
        };
    }

    /// <summary>
    /// 平仓
    /// </summary>
    private BacktestTrade ClosePosition(BacktestPosition position, MarketObservation market, string reason)
    {
        // 模拟市价单平仓
        var order = new SimulatedOrder
        {
            Symbol = market.Symbol,
            Side = position.IsLong ? OrderSide.Sell : OrderSide.Buy,
            Type = SimulatedOrderType.Market,
            Quantity = position.Quantity,
            PlacedAt = market.Timestamp
        };

        OrderFillResult fillResult = _orderMatcher.MatchOrder(order, market);

        // 计算持仓时长
        double holdingHours = (fillResult.FillTime - position.EntryTime).TotalHours;

        // 计算毛盈亏
        double grossPnL = position.IsLong
            ? position.Quantity * (fillResult.AvgFillPrice - position.EntryPrice)
            : position.Quantity * (position.EntryPrice - fillResult.AvgFillPrice);

        // 计算成本
        TradingCost cost = _costCalculator.CalculateTotalCost(
            entryQuantity: position.Quantity,
            entryPrice: position.EntryPrice,
            entryIsMaker: false, // 市价单都是taker
            exitQuantity: position.Quantity,
            exitPrice: fillResult.AvgFillPrice,
            exitIsMaker: false,
            holdingHours: holdingHours
        );

        // 净盈亏 = 毛盈亏 - 总成本
        double netPnL = grossPnL - cost.TotalCost;

        return new BacktestTrade
        {
            Symbol = position.Symbol,
            EntryTime = position.EntryTime,
            ExitTime = fillResult.FillTime,
            EntryPrice = position.EntryPrice,
            ExitPrice = fillResult.AvgFillPrice,
            Quantity = position.Quantity,
            IsLong = position.IsLong,
            GrossPnL = grossPnL,
            TradingCost = cost.EntryFee + cost.ExitFee,
            FundingCost = cost.FundingCost,
            NetPnL = netPnL,
            ExitReason = reason
        };
    }

    /// <summary>
    /// 生成或加载市场数据
    /// </summary>
    private static async Task<List<MarketObservation>> GenerateMarketDataAsync(
        string symbol,
        DateTime start,
        DateTime end,
        CancellationToken ct)
    {
        // TODO: 从数据库或API加载真实历史数据
        // 这里暂时使用模拟数据
        var data = new List<MarketObservation>();
        var random = new Random(42);
        DateTime timestamp = start;
        double price = 50000.0; // BTC起始价格

        while (timestamp < end)
        {
            ct.ThrowIfCancellationRequested();

            double change = (random.NextDouble() - 0.5) * 100; // ±50
            double open = price;
            double close = price + change;
            double high = Math.Max(open, close) + random.NextDouble() * 50;
            double low = Math.Min(open, close) - random.NextDouble() * 50;
            double volume = random.NextDouble() * 1000000; // 模拟成交量

            var indicators = new Dictionary<string, double>
            {
                ["sma"] = (open + close) / 2,
                ["std"] = Math.Abs(change),
                ["atr"] = Math.Abs(high - low),
                ["momentum_1"] = change
            };

            data.Add(new MarketObservation(
                symbol,
                TimeSpan.FromMinutes(1),
                timestamp,
                open, high, low, close,
                volume,
                indicators
            ));

            price = close;
            timestamp = timestamp.AddMinutes(1);
            await Task.Yield();
        }

        return data;
    }

    private static async IAsyncEnumerable<MarketObservation> AsAsyncEnumerable(List<MarketObservation> data)
    {
        foreach (MarketObservation item in data)
        {
            yield return item;
            await Task.Yield();
        }
    }
}

/// <summary>
/// 回测持仓状态
/// </summary>
internal class BacktestPosition
{
    public bool IsOpen { get; init; }
    public bool IsLong { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public DateTime EntryTime { get; init; }
    public double EntryPrice { get; init; }
    public double Quantity { get; init; }
    public double EntrySlippage { get; init; }
}
