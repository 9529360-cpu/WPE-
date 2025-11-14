using System;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Core.Extensions;

namespace 币安量化机器人.Core.Risk;

public class ValueAtRiskCalculator
{
    public double Calculate(PositionSnapshot snapshot, RiskConfiguration configuration)
    {
        double volatility = snapshot.Indicators?.GetValueOrDefault("volatility", 0.02) ?? 0.02;
        double zScore = 2.33; // 99% confidence
        double var = snapshot.Equity * volatility * zScore;
        return Math.Min(var, snapshot.Equity * configuration.MaxDrawdown);
    }
}
