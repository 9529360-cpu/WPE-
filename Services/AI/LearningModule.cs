using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 学习模块
/// </summary>
/// <remarks>
/// 核心职责：
/// 1. 记录历史决策和结果
/// 2. 分析决策效果
/// 3. 优化未来决策
/// 4. 识别模式和趋势
/// 5. 持续改进系统性能
/// </remarks>
public class LearningModule
{
    private readonly DataCacheService _cacheService;
    private readonly List<DecisionRecord> _decisionHistory;
    private readonly object _lock = new();
    private const int MaxHistorySize = 1000;

    public LearningModule(DataCacheService cacheService)
    {
        _cacheService = cacheService;
        _decisionHistory = new List<DecisionRecord>();
    }

    /// <summary>
    /// 基于历史学习优化决策
    /// </summary>
    public async Task<(DecisionAction Action, double Confidence, string Reason)> OptimizeDecisionAsync(
        (DecisionAction Action, double Confidence, string Reason) initialDecision,
        SystemState state,
        CancellationToken ct = default)
    {
        try
        {
            // 1. 查找相似历史场景
            List<DecisionRecord> similarScenarios = FindSimilarScenarios(state);

            if (similarScenarios.Count == 0)
            {
                return initialDecision; // 没有历史数据，使用初始决策
            }

            // 2. 分析历史决策效果
            HistoricalPerformance historicalPerformance = AnalyzeHistoricalPerformance(similarScenarios);

            // 3. 基于历史学习调整决策
            (DecisionAction Action, double Confidence, string Reason) optimizedDecision = AdjustDecisionBasedOnHistory(
                initialDecision,
                historicalPerformance,
                state);

            LogService.Debug("[LearningModule] 决策优化: {Initial} -> {Optimized} (历史样本={Count})",
                initialDecision.Action, optimizedDecision.Action, similarScenarios.Count);

            return optimizedDecision;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[LearningModule] 决策优化失败");
            return initialDecision; // 失败时返回初始决策
        }
    }

    /// <summary>
    /// 更新知识库（记录决策和结果）
    /// </summary>
    public async Task UpdateKnowledgeAsync(
        SystemState state,
        AIDecision decision,
        CancellationToken ct = default)
    {
        try
        {
            var record = new DecisionRecord
            {
                Timestamp = DateTime.UtcNow,
                State = state,
                Decision = decision,
                Outcome = null // 结果需要后续更新
            };

            lock (_lock)
            {
                _decisionHistory.Add(record);

                // 限制历史大小
                if (_decisionHistory.Count > MaxHistorySize)
                {
                    _decisionHistory.RemoveAt(0);
                }
            }

            // TODO: 持久化到数据库
            // await _cacheService.SaveDecisionRecordAsync(record);

            LogService.Debug("[LearningModule] 记录决策: {Action}", decision.PrimaryAction);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[LearningModule] 更新知识库失败");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 更新决策结果
    /// </summary>
    public async Task UpdateDecisionOutcomeAsync(
        DateTime decisionTimestamp,
        DecisionOutcome outcome,
        CancellationToken ct = default)
    {
        try
        {
            lock (_lock)
            {
                DecisionRecord? record = _decisionHistory
                    .FirstOrDefault(r => r.Timestamp == decisionTimestamp);

                if (record != null)
                {
                    record.Outcome = outcome;

                    LogService.Debug("[LearningModule] 更新决策结果: 成功={Success}, 收益={Profit:F2}",
                        outcome.Success, outcome.ProfitPercent);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[LearningModule] 更新决策结果失败");
        }

        await Task.CompletedTask;
    }

    #region 历史分析

    /// <summary>
    /// 查找相似历史场景
    /// </summary>
    private List<DecisionRecord> FindSimilarScenarios(SystemState currentState)
    {
        lock (_lock)
        {
            return _decisionHistory
                .Where(r => r.Outcome != null) // 只考虑有结果的记录
                .Where(r => IsSimilarState(r.State, currentState))
                .OrderByDescending(r => r.Timestamp)
                .Take(20) // 最近20条相似记录
                .ToList();
        }
    }

    /// <summary>
    /// 判断状态是否相似
    /// </summary>
    private bool IsSimilarState(SystemState historical, SystemState current)
    {
        // 简化版：基于关键指标的相似度

        // 1. 工作流阶段必须相同
        if (historical.CurrentStage != current.CurrentStage)
        {
            return false;
        }

        // 2. 市场状况相似
        double marketSimilarity = CalculateMarketSimilarity(
            historical.MarketCondition,
            current.MarketCondition);

        // 3. 风险等级相似
        bool riskSimilarity = Math.Abs((double)(historical.RiskMetrics.MaxDrawdown - current.RiskMetrics.MaxDrawdown)) < 0.05;

        return marketSimilarity > 0.7 && riskSimilarity;
    }

    /// <summary>
    /// 计算市场相似度
    /// </summary>
    private double CalculateMarketSimilarity(MarketCondition hist, MarketCondition curr)
    {
        double volaSimilarity = 1.0 - Math.Abs(hist.Volatility - curr.Volatility);
        double trendSimilarity = 1.0 - Math.Abs(hist.Trend - curr.Trend) / 2.0;
        double liquiditySimilarity = 1.0 - Math.Abs(hist.Liquidity - curr.Liquidity);

        return (volaSimilarity + trendSimilarity + liquiditySimilarity) / 3.0;
    }

    /// <summary>
    /// 分析历史决策表现
    /// </summary>
    private HistoricalPerformance AnalyzeHistoricalPerformance(List<DecisionRecord> records)
    {
        var outcomes = records.Select(r => r.Outcome!).ToList();

        double successRate = outcomes.Count(o => o.Success) / (double)outcomes.Count;
        double avgProfit = outcomes.Average(o => o.ProfitPercent);
        double maxProfit = outcomes.Max(o => o.ProfitPercent);
        double maxLoss = outcomes.Min(o => o.ProfitPercent);

        // 按决策动作分组统计
        var actionPerformance = records
            .GroupBy(r => r.Decision.PrimaryAction)
            .ToDictionary(
                g => g.Key,
                g => new ActionPerformance
                {
                    SuccessRate = g.Count(r => r.Outcome!.Success) / (double)g.Count(),
                    AvgProfit = g.Average(r => r.Outcome!.ProfitPercent),
                    Count = g.Count()
                });

        return new HistoricalPerformance
        {
            SuccessRate = successRate,
            AvgProfit = avgProfit,
            MaxProfit = maxProfit,
            MaxLoss = maxLoss,
            ActionPerformance = actionPerformance
        };
    }

    /// <summary>
    /// 基于历史调整决策
    /// </summary>
    private (DecisionAction Action, double Confidence, string Reason) AdjustDecisionBasedOnHistory(
        (DecisionAction Action, double Confidence, string Reason) initial,
        HistoricalPerformance history,
        SystemState state)
    {
        // 1. 如果该动作历史表现不佳，降低信心度
        if (history.ActionPerformance.TryGetValue(initial.Action, out ActionPerformance? actionPerf))
        {
            if (actionPerf.SuccessRate < 0.5)
            {
                double adjustedConfidence = initial.Confidence * 0.7;
                string reason = $"{initial.Reason} [历史胜率低: {actionPerf.SuccessRate:P0}]";

                // 如果信心度太低，建议观望
                if (adjustedConfidence < 0.4)
                {
                    return (DecisionAction.Hold, adjustedConfidence, $"历史表现不佳，建议观望");
                }

                return (initial.Action, adjustedConfidence, reason);
            }

            // 历史表现良好，提升信心度
            if (actionPerf.SuccessRate > 0.7)
            {
                double adjustedConfidence = Math.Min(initial.Confidence * 1.2, 1.0);
                string reason = $"{initial.Reason} [历史胜率高: {actionPerf.SuccessRate:P0}]";

                return (initial.Action, adjustedConfidence, reason);
            }
        }

        // 2. 如果整体历史表现不佳，建议保守
        if (history.SuccessRate < 0.5)
        {
            return (DecisionAction.ReduceRisk, initial.Confidence * 0.8, "历史整体表现不佳，建议保守");
        }

        // 默认：保持初始决策
        return initial;
    }

    #endregion

    #region 性能分析

    /// <summary>
    /// 获取学习模块统计信息
    /// </summary>
    public LearningStatistics GetStatistics()
    {
        lock (_lock)
        {
            var recordsWithOutcome = _decisionHistory.Where(r => r.Outcome != null).ToList();

            if (recordsWithOutcome.Count == 0)
            {
                return new LearningStatistics
                {
                    TotalRecords = _decisionHistory.Count,
                    RecordsWithOutcome = 0,
                    OverallSuccessRate = 0,
                    OverallAvgProfit = 0,
                    Timestamp = DateTime.UtcNow
                };
            }

            return new LearningStatistics
            {
                TotalRecords = _decisionHistory.Count,
                RecordsWithOutcome = recordsWithOutcome.Count,
                OverallSuccessRate = recordsWithOutcome.Count(r => r.Outcome!.Success) / (double)recordsWithOutcome.Count,
                OverallAvgProfit = recordsWithOutcome.Average(r => r.Outcome!.ProfitPercent),
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 清空历史记录
    /// </summary>
    public void ClearHistory()
    {
        _decisionHistory.Clear();
        LogService.Info("[LearningModule] 历史决策记录已清空");
    }

    #endregion
}

#region 学习模块数据模型

/// <summary>
/// 决策记录
/// </summary>
public class DecisionRecord
{
    public DateTime Timestamp { get; set; }
    public required SystemState State { get; init; }
    public required AIDecision Decision { get; init; }
    public DecisionOutcome? Outcome { get; set; }
}

/// <summary>
/// 决策结果
/// </summary>
public class DecisionOutcome
{
    public bool Success { get; init; }
    public double ProfitPercent { get; init; }
    public double Duration { get; init; } // 持续时间（小时）
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 历史表现
/// </summary>
public class HistoricalPerformance
{
    public double SuccessRate { get; init; }
    public double AvgProfit { get; init; }
    public double MaxProfit { get; init; }
    public double MaxLoss { get; init; }
    public Dictionary<DecisionAction, ActionPerformance> ActionPerformance { get; init; } = new();
}

/// <summary>
/// 动作表现
/// </summary>
public class ActionPerformance
{
    public double SuccessRate { get; init; }
    public double AvgProfit { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// 学习统计信息
/// </summary>
public class LearningStatistics
{
    public int TotalRecords { get; init; }
    public int RecordsWithOutcome { get; init; }
    public double OverallSuccessRate { get; init; }
    public double OverallAvgProfit { get; init; }
    public DateTime Timestamp { get; init; }
}

#endregion
