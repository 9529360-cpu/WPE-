using System;
using System.Collections.Concurrent;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Core.Risk;

/// <summary>
/// 移动止损规则：盈利后自动跟踪价格,锁定利润
/// </summary>
public sealed class TrailingStopLossRule : IRiskRule
{
    private readonly ConcurrentDictionary<string, TrailingStopState> _states = new();
    private double _activationProfitPercent = 0.01; // 激活阈值:盈利1%
    private double _trailingPercent = 0.005;        // 跟踪距离:0.5%

    public string Name => "TrailingStopLoss";

    public void Configure(RiskConfiguration configuration)
    {
        _activationProfitPercent = configuration.TrailingStopActivationPercent;
        _trailingPercent = configuration.TrailingStopPercent;
    }

    public RiskRuleResult Evaluate(in PositionSnapshot snapshot)
    {
        string key = snapshot.Symbol;
        var state = _states.GetOrAdd(key, _ => new TrailingStopState());

        double currentPrice = snapshot.CurrentPrice;
        double entryPrice = snapshot.EntryPrice;
        bool isLong = snapshot.Quantity > 0;

        // 计算当前盈亏百分比
        double profitPercent = isLong
            ? (currentPrice - entryPrice) / entryPrice
            : (entryPrice - currentPrice) / entryPrice;

        // 未激活:检查是否达到激活条件
        if (!state.IsActivated)
        {
            if (profitPercent >= _activationProfitPercent)
            {
                state.Activate(currentPrice);
            }
            return new RiskRuleResult(true);
        }

        // 已激活:更新最高/最低价
        if (isLong)
        {
            // 多头:追踪最高价
            if (currentPrice > state.HighWaterMark)
            {
                state.HighWaterMark = currentPrice;
                state.StopPrice = currentPrice * (1 - _trailingPercent);
            }

            // 检查是否触发止损
            if (currentPrice <= state.StopPrice)
            {
                _states.TryRemove(key, out _); // 清除状态
                return new RiskRuleResult(
                    false,
                    $"移动止损触发: 价格 {currentPrice:F4} <= 止损 {state.StopPrice:F4}"
                );
            }
        }
        else
        {
            // 空头:追踪最低价
            if (currentPrice < state.LowWaterMark || state.LowWaterMark == 0)
            {
                state.LowWaterMark = currentPrice;
                state.StopPrice = currentPrice * (1 + _trailingPercent);
            }

            // 检查是否触发止损
            if (currentPrice >= state.StopPrice)
            {
                _states.TryRemove(key, out _);
                return new RiskRuleResult(
                    false,
                    $"移动止损触发: 价格 {currentPrice:F4} >= 止损 {state.StopPrice:F4}"
                );
            }
        }

        return new RiskRuleResult(true);
    }

    /// <summary>
    /// 获取指定交易对的移动止损状态(用于UI显示)
    /// </summary>
    public TrailingStopState? GetState(string symbol)
    {
        return _states.TryGetValue(symbol, out var state) ? state : null;
    }
}

/// <summary>
/// 移动止损状态
/// </summary>
public class TrailingStopState
{
    public bool IsActivated { get; private set; }
    public double HighWaterMark { get; set; }
    public double LowWaterMark { get; set; }
    public double StopPrice { get; set; }
    public DateTime ActivatedAt { get; private set; }

    public void Activate(double currentPrice)
    {
        IsActivated = true;
        HighWaterMark = currentPrice;
        LowWaterMark = currentPrice;
        StopPrice = 0;
        ActivatedAt = DateTime.UtcNow;
    }
}
