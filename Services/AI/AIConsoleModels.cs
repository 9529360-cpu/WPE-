using System;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services.AI
{
    public record AITradingSignalRecord
    {
        public string Symbol { get; init; }
        public string Action { get; init; }
        public double Confidence { get; init; }
        public string Reason { get; init; }
        public DateTime Timestamp { get; init; }

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
        public string Summary { get; init; }
        public DateTime Timestamp { get; init; }

        public TradingEventRecord() { }
        public TradingEventRecord(AITradingSignal s, OrderExecutionResult r)
        {
            Summary = $"{s.Timestamp:HH:mm:ss} {s.Symbol} {s.Action} => {r.IsSuccess}";
            Timestamp = DateTime.UtcNow;
        }
    }

    public record GateRejectionRecord
    {
        public string Reason { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public record StrategyChangeRecord
    {
        public string Summary { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public record ParameterAdjustmentRecord
    {
        public string Summary { get; init; }
        public DateTime Timestamp { get; init; }
    }

    // Market stream events
    public record MarketFundingRateUpdateEvent(string Symbol, double FundingRate, DateTime Timestamp = default);
    public record MarketOpenInterestUpdateEvent(string Symbol, double OpenInterest, DateTime Timestamp = default);
    public record MarketLongShortRatioUpdateEvent(string Symbol, double LongShortRatio, DateTime Timestamp = default);

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
