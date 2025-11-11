using System;
using System.Collections.Generic;
using System.Linq;

namespace 币安量化机器人.Services;

/// <summary>
/// 风险热力图生成器
/// </summary>
public sealed class RiskHeatmapGenerator
{
    /// <summary>
    /// 生成风险热力图数据
    /// </summary>
    public RiskHeatmapData GenerateHeatmap(List<string> symbols, Dictionary<string, PositionRisk> positionRisks)
    {
        var heatmap = new RiskHeatmapData
        {
            Symbols = symbols,
            GeneratedAt = DateTime.UtcNow
        };

        // 为每个交易对生成风险评分
        foreach (string symbol in symbols)
        {
            PositionRisk risk = positionRisks.ContainsKey(symbol)
                ? positionRisks[symbol]
                : new PositionRisk { Symbol = symbol };

            heatmap.RiskScores[symbol] = CalculateRiskScore(risk);
            heatmap.Details[symbol] = risk;
        }

        return heatmap;
    }

    /// <summary>
    /// 计算风险评分 (0-100)
    /// </summary>
    private double CalculateRiskScore(PositionRisk risk)
    {
        double score = 0.0;

        // 仓位占比风险 (40%)
        score += risk.PositionRatio * 40;

        // 未实现盈亏风险 (30%)
        if (risk.UnrealizedPnL < 0)
        {
            score += Math.Min(Math.Abs(risk.UnrealizedPnL) * 10, 30);
        }

        // 杠杆风险 (20%)
        score += Math.Min(risk.Leverage * 2, 20);

        // 波动率风险 (10%)
        score += Math.Min(risk.Volatility * 100, 10);

        return Math.Min(score, 100);
    }
}

/// <summary>
/// 风险热力图数据
/// </summary>
public class RiskHeatmapData
{
    public List<string> Symbols { get; set; } = new();
    public Dictionary<string, double> RiskScores { get; set; } = new();
    public Dictionary<string, PositionRisk> Details { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// 仓位风险
/// </summary>
public class PositionRisk
{
    public string Symbol { get; set; } = string.Empty;
    public double PositionRatio { get; set; }
    public double UnrealizedPnL { get; set; }
    public double Leverage { get; set; }
    public double Volatility { get; set; }
}
