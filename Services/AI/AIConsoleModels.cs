using System;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services.AI
{
    public record AITradingSignalRecord
    {
        public string Symbol { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public double Confidence { get; init; }
        public string Reason { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        public AITradingSignalRecord(AITradingSignal s)
        {
            Symbol = s.Symbol;
            Action = s.Action.ToString();
            Confidence = s.Confidence;
            Reason = s.Reason ?? string.Empty;
            Timestamp = s.Timestamp;
        }
    }

    public record TradingEventRecord
    {
        public string Summary { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        public TradingEventRecord() { }
        public TradingEventRecord(AITradingSignal s, OrderExecutionResult r)
        {
            Summary = $"{s.Timestamp:HH:mm:ss} {s.Symbol} {s.Action} => {r.IsSuccess}";
            Timestamp = DateTime.UtcNow;
        }
    }

    public record GateRejectionRecord
    {
        public string Reason { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record StrategyChangeRecord
    {
        public string Summary { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record ParameterAdjustmentRecord
    {
        public string Summary { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    // Market stream events - use property-style records to ensure Timestamp is initialized
    public record MarketFundingRateUpdateEvent
    {
        public string Symbol { get; init; } = string.Empty;
        public double FundingRate { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record MarketOpenInterestUpdateEvent
    {
        public string Symbol { get; init; } = string.Empty;
        public double OpenInterest { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record MarketLongShortRatioUpdateEvent
    {
        public string Symbol { get; init; } = string.Empty;
        public double LongShortRatio { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    // Market stream subscription request
    public record MarketStreamSubscriptionRequestEvent
    {
        public string Symbol { get; init; } = string.Empty;
        public bool SubscribeFundingRate { get; init; }
        public bool SubscribeOpenInterest { get; init; }
        public bool SubscribeLongShortRatio { get; init; }
    }

    // Gate and strategy events
    public record TradeGateRejectionEvent(string Reason, DateTime Timestamp = default);
    public record StrategyChangedEvent(string Summary, DateTime Timestamp = default);
    public record ParameterAdjustedEvent(string Summary, DateTime Timestamp = default);
}
