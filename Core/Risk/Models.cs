using System;
using System.Collections.Generic;

namespace 币安量化机器人.Core.Risk
{
    public enum AlertSeverity { Info, Warning, Error, Critical }

    public class RiskEvent
    {
        public string Symbol { get; }
        public string RuleName { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }

        public RiskEvent(string symbol, string ruleName, string message, DateTime timestamp)
        {
            Symbol = symbol;
            RuleName = ruleName;
            Message = message;
            Timestamp = timestamp;
        }
    }

    public class RiskConfiguration
    {
        public double MaxDailyLossPercent { get; set; } = 0.1;
        public TimeSpan MaxPositionHoldingTime { get; set; } = TimeSpan.FromDays(1);
        public double MaxDrawdown { get; set; } = 0.2;
        public double MaxOrderQuantity { get; set; } = 100;
        public double MaxPositionSize { get; set; } = 1000;
        public TimeSpan RiskEvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);
        public double DailyLossLimit { get; set; } = 10000;
        public double BlacklistThreshold { get; set; } = 5;
        public double StopLossMultiplier { get; set; } = 1.5;
        public double TrailingStopActivationPercent { get; set; } = 0.02;
        public double TrailingStopPercent { get; set; } = 0.01;
    }

    public class RiskProfile
    {
        public double Exposure { get; }
        public double MaxDrawdown { get; }
        public double CurrentDailyLoss { get; }
        public IEnumerable<string> BlacklistSymbols { get; }
        public double Kelly { get; }
        public double ValueAtRisk { get; }

        public RiskProfile(double exposure = 0, double maxDrawdown = 0, double currentDailyLoss = 0, IEnumerable<string> blacklist = null, double kelly = 0, double var = 0)
        {
            Exposure = exposure;
            MaxDrawdown = maxDrawdown;
            CurrentDailyLoss = currentDailyLoss;
            BlacklistSymbols = blacklist ?? Array.Empty<string>();
            Kelly = kelly;
            ValueAtRisk = var;
        }
    }

    public class PositionSnapshot
    {
        public string Symbol { get; set; }
        public double Quantity { get; set; }
        public double CurrentPrice { get; set; }
        public double Equity { get; set; }
        public double DailyPnl { get; set; }
        public double RealizedPnl { get; set; }
        public double MaxDrawdown { get; set; }
        public DateTime OpenTime { get; set; }
        public int ConsecutiveLosingTrades { get; set; }
        public IDictionary<string, double> Indicators { get; set; }
        public double EntryPrice { get; set; }
    }

    public enum TradeActionType { Hold, Buy, Sell }

    public class TradeAction
    {
        public TradeActionType ActionType { get; set; }
        public double Quantity { get; set; }
    }

    public class TradeFill
    {
        public string Symbol { get; set; }
        public TradeActionType ActionType { get; set; }
    }
}
