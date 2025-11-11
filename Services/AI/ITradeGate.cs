using 币安量化机器人.Models;

namespace 币安量化机器人.Services.AI;

public interface ITradeGate
{
    TradeGateDecision Permit(AITradingSignal signal, TradingAccount account);
}

public readonly struct TradeGateDecision
{
    public bool Allowed { get; }
    public string Reason { get; }

    public TradeGateDecision(bool allowed, string reason)
    {
        Allowed = allowed;
        Reason = reason;
    }

    public static TradeGateDecision Allow(string reason = "") => new(true, reason);
    public static TradeGateDecision Deny(string reason) => new(false, reason);
}
