using System;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Core.Risk;

/// <summary>
/// 时间止损规则：防止持仓过久被套
/// </summary>
public sealed class TimeBasedExitRule : IRiskRule
{
    private TimeSpan _maxHoldingTime = TimeSpan.FromHours(24);

    public string Name => "TimeBasedExit";

    public void Configure(RiskConfiguration configuration)
    {
        // 从配置中读取最大持仓时间,默认24小时
        _maxHoldingTime = configuration.MaxPositionHoldingTime;
    }

    public RiskRuleResult Evaluate(in PositionSnapshot snapshot)
    {
        if (snapshot.OpenTime == default)
        {
            // 如果没有开仓时间,跳过检查
            return new RiskRuleResult(true);
        }

        TimeSpan holdingDuration = DateTime.UtcNow - snapshot.OpenTime;
        if (holdingDuration > _maxHoldingTime)
        {
            return new RiskRuleResult(
                false,
                $"持仓时间 {holdingDuration.TotalHours:F1}h 超过限制 {_maxHoldingTime.TotalHours:F1}h,强制平仓"
            );
        }

        return new RiskRuleResult(true);
    }
}
