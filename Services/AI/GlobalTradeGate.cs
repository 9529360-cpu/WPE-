using System;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services.AI;

public class GlobalTradeGate : ITradeGate
{
    private readonly StateManager _state;

    public GlobalTradeGate(StateManager state)
    {
        _state = state;
    }

    public TradeGateDecision Permit(AITradingSignal signal, TradingAccount account)
    {
        var st = _state.CurrentState;

        // 1) 市场极端波动禁入
        if (st.MarketCondition.Volatility > 0.8)
        {
            return TradeGateDecision.Deny($"高波动 {st.MarketCondition.Volatility:P0}");
        }

        // 2) 系统资源不健康禁入
        if (!st.SystemResources.IsHealthy)
        {
            return TradeGateDecision.Deny("系统资源不健康");
        }

        // 3) 账户日损超阈值禁入 (把 double 转为 decimal 比较)
        decimal todayRet = (decimal)account.TodayReturnPercent;
        if (todayRet < -0.05m)
        {
            return TradeGateDecision.Deny("当日亏损超阈值");
        }

        // 4) 决策信心过低禁入
        if (signal.Confidence < ServiceLocator.TradingConfig.MinConfidence)
        {
            return TradeGateDecision.Deny($"信心度低 {signal.Confidence:P0}");
        }

        // 5) 允许
        return TradeGateDecision.Allow("通过全局闸门");
    }
}
