using System;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services.AI;

public class GlobalTradeGate : ITradeGate
{
    private readonly StateManager _state;

    public GlobalTradeGate(StateManager state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public TradeGateDecision Permit(AITradingSignal signal, TradingAccount account)
    {
        if (signal == null)
        {
            return TradeGateDecision.Deny("信号为空");
        }
        if (account == null)
        {
            return TradeGateDecision.Deny("账户为空");
        }

        var st = _state.CurrentState;
        if (st == null)
        {
            // conservative deny if state unknown
            return TradeGateDecision.Deny("系统状态未知");
        }

        // 1) 市场极端波动禁入
        if (st.MarketCondition?.Volatility is double v && v > 0.8)
        {
            return TradeGateDecision.Deny($"高波动 {v:P0}");
        }

        // 2) 系统资源不健康禁入
        if (st.SystemResources != null && !st.SystemResources.IsHealthy)
        {
            return TradeGateDecision.Deny("系统资源不健康");
        }

        // 3) 账户日损超阈值禁入 (把 double 转为 decimal 比较)
        try
        {
            decimal todayRet = (decimal)account.TodayReturnPercent;
            if (todayRet < -0.05m)
            {
                return TradeGateDecision.Deny("当日亏损超阈值");
            }
        }
        catch { }

        // 4) 决策信心过低禁入
        double minConfidence = 0.7;
        try
        {
            minConfidence = ServiceLocator.TradingConfig?.MinConfidence ?? 0.7;
        }
        catch { }

        if (signal.Confidence < minConfidence)
        {
            return TradeGateDecision.Deny($"信心度低 {signal.Confidence:P0}");
        }

        // 5) 允许
        return TradeGateDecision.Allow("通过全局闸门");
    }
}
