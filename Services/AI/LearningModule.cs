using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 学习模块 (Phase 3: 增强版)
/// </summary>
/// <remarks>
/// 核心职责：
/// 1. 记录历史决策和结果
/// 2. 分析决策效果
/// 3. 优化未来决策
/// 4. 识别模式和趋势
/// 5. 持续改进系统性能
/// 
/// 🆕 Phase 3 增强功能：
/// 6. 决策因子权重优化
/// 7. 因子贡献度分析
/// 8. 自适应权重调整
/// 9. 性能指标追踪
/// 10. 学习曲线可视化
/// </remarks>
public class LearningModule
{
    private readonly DataCacheService _cacheService;
    private readonly List<DecisionRecord> _decisionHistory;
    private readonly Dictionary<string, FactorPerformance> _factorPerformance;
    private readonly object _lock = new();
    private const int MaxHistorySize = 1000;

    // 🆕 Phase 3: 因子权重学习参数
    private double _learningRate = 0.01; // 学习率
    private int _optimizationCycle = 0;  // 优化周期计数

    private const string LearningStateKey = "learning_module_state_v1";

    public LearningModule(DataCacheService cacheService)
    {
        _cacheService = cacheService;
        _decisionHistory = new List<DecisionRecord>();
        _factorPerformance = new Dictionary<string, FactorPerformance>();

        // Try to restore persisted learning state
        try
        {
            var _ = LoadStateAsync().ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    LogService.Info("[LearningModule] 学习状态已恢复");
                }
            });
        }
        catch (Exception ex)
        {
            LogService.Warning("[LearningModule] 恢复学习状态失败: {0}", ex.Message);
        }
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

            // 持久化到数据库
            try
            {
                long tsMs = new DateTimeOffset(record.Timestamp).ToUnixTimeMilliseconds();
                string stateJson = JsonSerializer.Serialize(state);
                string decisionJson = JsonSerializer.Serialize(decision);
                await _cacheService.SaveDecisionRecordAsync(tsMs, stateJson, decisionJson).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Warning("[LearningModule] 保存决策记录到数据库失败: {Message}", ex.Message);
            }

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

                    LogService.Debug("[LearningModule] 更新决策结果: 成功={Success}, 收益={ProfitPercent:F2}",
                        outcome.Success, outcome.ProfitPercent);
                }
            }

            // 持久化 outcome 到数据库
            try
            {
                long tsMs = new DateTimeOffset(decisionTimestamp).ToUnixTimeMilliseconds();
                string outcomeJson = JsonSerializer.Serialize(outcome);
                await _cacheService.UpdateDecisionRecordOutcomeAsync(tsMs, outcomeJson).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Warning("[LearningModule] 更新决策结果到数据库失败: {Message}", ex.Message);
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

    /// <summary>
    /// 🆕 Phase 3: 优化决策因子权重
    /// </summary>
    /// <remarks>
    /// 算法：基于历史表现的梯度下降优化
    /// 1. 收集最近N条决策记录
    /// 2. 分析每个因子的贡献度
    /// 3. 计算因子与结果的相关性
    /// 4. 调整权重以最大化预期收益
    /// </remarks>
    public async Task<Dictionary<string, decimal>> OptimizeFactorWeightsAsync(
        Dictionary<string, decimal> currentWeights,
        SystemState currentState,
        AIDecision lastDecision,
        DecisionOutcome? lastOutcome,
        CancellationToken ct = default)
    {
        try
        {
            _optimizationCycle++;

            // 1. 如果结果不存在，不优化
            if (lastOutcome == null)
            {
                return currentWeights;
            }

            // 2. 记录因子表现
            if (lastDecision.FactorBreakdown != null)
            {
                await RecordFactorPerformanceAsync(lastDecision.FactorBreakdown, lastOutcome, ct).ConfigureAwait(false);
            }

            // 3. 每10次决策执行一次权重优化
            if (_optimizationCycle % 10 != 0)
            {
                return currentWeights;
            }

            // 4. 分析因子贡献度
            Dictionary<string, double> factorContributions = AnalyzeFactorContributions();

            if (factorContributions.Count == 0)
            {
                LogService.Debug("[LearningModule] 因子贡献数据不足，跳过权重优化");
                return currentWeights;
            }

            // 5. 计算新权重
            var optimizedWeights = new Dictionary<string, decimal>();
            foreach (var (factorCode, currentWeight) in currentWeights)
            {
                if (factorContributions.TryGetValue(factorCode, out double contribution))
                {
                    // 梯度下降更新：weight = weight + learning_rate * contribution
                    double adjustment = _learningRate * contribution;
                    decimal newWeight = currentWeight + (decimal)adjustment;

                    // 约束权重范围 [0.01, 0.30]
                    newWeight = Math.Clamp(newWeight, 0.01m, 0.30m);

                    optimizedWeights[factorCode] = newWeight;
                }
                else
                {
                    optimizedWeights[factorCode] = currentWeight;
                }
            }

            // 6. 归一化权重（总和=1）
            decimal totalWeight = optimizedWeights.Values.Sum();
            if (totalWeight > 0)
            {
                var normalizedWeights = optimizedWeights.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value / totalWeight
                );

                LogService.Info("[LearningModule] 🎓 因子权重已优化 (周期={Cycle})", _optimizationCycle);
                LogTopFactorWeightChanges(currentWeights, normalizedWeights);

                // 持久化学习状态
                try
                {
                    await SaveStateAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.Warning("[LearningModule] 优化后保存学习状态失败: {0}", ex.Message);
                }

                return normalizedWeights;
            }

            return currentWeights;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[LearningModule] 因子权重优化失败");
            return currentWeights;
        }
    }

    /// <summary>
    /// 🆕 记录因子表现
    /// </summary>
    private async Task RecordFactorPerformanceAsync(
        Dictionary<string, decimal> factorScores,
        DecisionOutcome outcome,
        CancellationToken ct)
    {
        await Task.CompletedTask;

        lock (_lock)
        {
            foreach (var (factorCode, score) in factorScores)
            {
                if (!_factorPerformance.ContainsKey(factorCode))
                {
                    _factorPerformance[factorCode] = new FactorPerformance
                    {
                        FactorCode = factorCode,
                        TotalSamples = 0,
                        SuccessfulPredictions = 0,
                        TotalProfit = 0,
                        ScoreHistory = new List<double>(),
                        OutcomeHistory = new List<double>()
                    };
                }

                var perf = _factorPerformance[factorCode];
                perf.TotalSamples++;

                if (outcome.Success)
                {
                    perf.SuccessfulPredictions++;
                }

                perf.TotalProfit += outcome.ProfitPercent;
                perf.ScoreHistory.Add((double)score);
                perf.OutcomeHistory.Add(outcome.ProfitPercent);

                // 限制历史大小
                if (perf.ScoreHistory.Count > 100)
                {
                    perf.ScoreHistory.RemoveAt(0);
                    perf.OutcomeHistory.RemoveAt(0);
                }
            }
        }

        // 持久化学习状态：当某个因子样本累积到阈值时保存一次（限频）
        try
        {
            bool shouldSave = false;
            lock (_lock)
            {
                foreach (var perf in _factorPerformance.Values)
                {
                    if (perf.TotalSamples > 0 && perf.TotalSamples % 50 == 0)
                    {
                        shouldSave = true;
                        break;
                    }
                }
            }

            if (shouldSave)
            {
                try
                {
                    await SaveStateAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.Warning("[LearningModule] 自动保存学习状态失败: {0}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warning("[LearningModule] 检查自动保存条件失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 🆕 分析因子贡献度
    /// </summary>
    /// <returns>因子代码 → 贡献度（正数表示正贡献，负数表示负贡献）</returns>
    private Dictionary<string, double> AnalyzeFactorContributions()
    {
        lock (_lock)
        {
            var contributions = new Dictionary<string, double>();

            foreach (var (factorCode, perf) in _factorPerformance)
            {
                // 样本数不足，跳过
                if (perf.TotalSamples < 10)
                {
                    continue;
                }

                // 计算相关系数（因子得分 vs 结果收益）
                double correlation = CalculateCorrelation(
                    perf.ScoreHistory,
                    perf.OutcomeHistory
                );

                // 计算胜率
                double winRate = (double)perf.SuccessfulPredictions / perf.TotalSamples;

                // 计算平均收益
                double avgProfit = perf.TotalProfit / perf.TotalSamples;

                // 综合贡献度 = 相关系数 * 胜率 * 平均收益
                double contribution = correlation * winRate * avgProfit;

                contributions[factorCode] = contribution;
            }

            return contributions;
        }
    }

    /// <summary>
    /// 🆕 计算相关系数（皮尔逊相关）
    /// </summary>
    private double CalculateCorrelation(List<double> x, List<double> y)
    {
        if (x.Count != y.Count || x.Count == 0)
        {
            return 0;
        }

        double meanX = x.Average();
        double meanY = y.Average();

        double numerator = 0;
        double denomX = 0;
        double denomY = 0;

        for (int i = 0; i < x.Count; i++)
        {
            double diffX = x[i] - meanX;
            double diffY = y[i] - meanY;

            numerator += diffX * diffY;
            denomX += diffX * diffX;
            denomY += diffY * diffY;
        }

        if (denomX == 0 || denomY == 0)
        {
            return 0;
        }

        return numerator / Math.Sqrt(denomX * denomY);
    }

    /// <summary>
    /// 🆕 记录权重变化日志
    /// </summary>
    private void LogTopFactorWeightChanges(
        Dictionary<string, decimal> oldWeights,
        Dictionary<string, decimal> newWeights)
    {
        var changes = new List<(string Factor, decimal OldWeight, decimal NewWeight, decimal Change)>();

        foreach (var (factor, newWeight) in newWeights)
        {
            if (oldWeights.TryGetValue(factor, out decimal oldWeight))
            {
                decimal change = newWeight - oldWeight;
                if (Math.Abs(change) > 0.01m) // 变化超过1%才记录
                {
                    changes.Add((factor, oldWeight, newWeight, change));
                }
            }
        }

        // 按变化幅度排序，取前5个
        var topChanges = changes
            .OrderByDescending(c => Math.Abs(c.Change))
            .Take(5)
            .ToList();

        if (topChanges.Any())
        {
            LogService.Info("[LearningModule] 🔄 权重变化前5名:");
            foreach (var (factor, oldW, newW, change) in topChanges)
            {
                string direction = change > 0 ? "↑" : "↓";
                LogService.Info("   {Direction} {Factor}: {Old:P2} → {New:P2} ({Change:+0.00%;-0.00%})",
                    direction, factor, oldW, newW, change);
            }
        }
    }

    /// <summary>
    /// 🆕 获取因子性能统计
    /// </summary>
    public Dictionary<string, FactorPerformanceStats> GetFactorPerformanceStats()
    {
        lock (_lock)
        {
            return _factorPerformance.ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    var perf = kvp.Value;
                    return new FactorPerformanceStats
                    {
                        FactorCode = perf.FactorCode,
                        TotalSamples = perf.TotalSamples,
                        WinRate = perf.TotalSamples > 0
                            ? (double)perf.SuccessfulPredictions / perf.TotalSamples
                            : 0,
                        AvgProfit = perf.TotalSamples > 0
                            ? perf.TotalProfit / perf.TotalSamples
                            : 0,
                        Correlation = perf.ScoreHistory.Count >= 10
                            ? CalculateCorrelation(perf.ScoreHistory, perf.OutcomeHistory)
                            : 0
                    };
                }
            );
        }
    }

    /// <summary>
    /// 🆕 重置因子学习状态
    /// </summary>
    public void ResetFactorLearning()
    {
        lock (_lock)
        {
            _factorPerformance.Clear();
            _optimizationCycle = 0;
            LogService.Info("[LearningModule] 因子学习状态已重置");
        }
    }

    /// <summary>
    /// 导出训练数据到 Data/exports，返回导出文件夹路径
    /// </summary>
    public async Task<string> ExportTrainingDataAsync(string? symbol = null)
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data", "exports");
        Directory.CreateDirectory(dataDir);

        // signals
        var signals = await _cacheService.LoadSignalsAsync(symbol, limit: 1000);
        string signalsPath = Path.Combine(dataDir, $"signals_{(symbol ?? "all")}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("id,symbol,action,confidence,reason,source,timestamp");
        foreach (var s in signals)
        {
            var reasonEscaped = (s.Reason ?? string.Empty).Replace("\"", "'");
            var sourceEscaped = (s.Source ?? string.Empty).Replace("\"", "'");
            sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},\"{1}\",{2},{3},\"{4}\",\"{5}\",{6}",
                s.Id,
                s.Symbol,
                s.Action,
                s.Confidence,
                reasonEscaped,
                sourceEscaped,
                new DateTimeOffset(s.Timestamp).ToUnixTimeMilliseconds()));
        }
        await File.WriteAllTextAsync(signalsPath, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);

        // orders
        var orders = await _cacheService.LoadOrdersAsync(symbol, limit: 1000);
        string ordersPath = Path.Combine(dataDir, $"orders_{(symbol ?? "all")}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        var sb2 = new StringBuilder();
        sb2.AppendLine("order_id,symbol,side,type,quantity,price,stop_price,status,filled_qty,avg_fill_price,commission,strategy_name,created_at,updated_at,filled_at");
        foreach (var o in orders)
        {
            var price = o.Price.HasValue ? o.Price.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            var stop = o.StopPrice.HasValue ? o.StopPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            var avg = o.AvgFillPrice.HasValue ? o.AvgFillPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            var strategy = (o.StrategyName ?? string.Empty).Replace("\"", "'");
            long createdTs = new DateTimeOffset(o.CreatedAt).ToUnixTimeMilliseconds();
            long updatedTs = new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds();
            long filledTs = o.FilledAt.HasValue ? new DateTimeOffset(o.FilledAt.Value).ToUnixTimeMilliseconds() : 0;
            sb2.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},\"{1}\",{2},{3},{4},{5},{6},{7},{8},{9},{10},\"{11}\",{12},{13},{14}",
                o.OrderId,
                o.Symbol,
                o.Side,
                o.Type,
                o.Quantity,
                price,
                stop,
                o.Status,
                o.FilledQuantity,
                avg,
                o.Commission,
                strategy,
                createdTs,
                updatedTs,
                filledTs));
        }
        await File.WriteAllTextAsync(ordersPath, sb2.ToString(), Encoding.UTF8).ConfigureAwait(false);

        return dataDir;
    }

    /// <summary>
    /// 启动训练（stub 实现），返回模型文件路径
    /// </summary>
    public async Task<string> StartTrainingAsync(string? exportFolder = null, CancellationToken ct = default)
    {
        try
        {
            string folder = exportFolder ?? await ExportTrainingDataAsync(null);
            if (!Directory.Exists(folder))
            {
                throw new ArgumentException("导出文件夹不存在", nameof(exportFolder));
            }

            string modelsDir = Path.Combine(AppContext.BaseDirectory, "Data", "models");
            Directory.CreateDirectory(modelsDir);
            string modelPath = Path.Combine(modelsDir, $"ai_model_{DateTime.UtcNow:yyyyMMddHHmmss}.bin");

            LogService.Info("[LearningModule] 开始训练，使用数据目录: {0}", folder);

            // 模拟长时间训练，支持取消
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LogService.Info("[LearningModule] 训练被取消");
                throw;
            }

            // 生成 placeholder 模型内容，包含简单因子统计摘要
            var stats = GetStatistics();
            var meta = new StringBuilder();
            meta.AppendLine("Model: Placeholder");
            meta.AppendLine($"TrainedAt: {DateTime.UtcNow:O}");
            meta.AppendLine($"TotalRecords: {stats.TotalRecords}");
            meta.AppendLine($"RecordsWithOutcome: {stats.RecordsWithOutcome}");
            meta.AppendLine($"OverallSuccessRate: {stats.OverallSuccessRate:F4}");
            meta.AppendLine($"OverallAvgProfit: {stats.OverallAvgProfit:F4}");

            await File.WriteAllTextAsync(modelPath, meta.ToString(), Encoding.UTF8).ConfigureAwait(false);

            LogService.Info("[LearningModule] 训练完成，模型已保存: {0}", modelPath);
            return modelPath;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[LearningModule] 训练失败");
            throw;
        }
    }

    /// <summary>
    /// 保存当前学习状态到持久层
    /// </summary>
    public async Task SaveStateAsync()
    {
        try
        {
            var state = new
            {
                LearningRate = _learningRate,
                OptimizationCycle = _optimizationCycle,
                FactorPerformance = _factorPerformance
            };

            string json = JsonSerializer.Serialize(state);
            await _cacheService.SaveLearningStateAsync(LearningStateKey, json).ConfigureAwait(false);
            LogService.Info("[LearningModule] 学习状态已保存");
        }
        catch (Exception ex)
        {
            LogService.Warning("[LearningModule] 保存学习状态失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 从持久层加载学习状态
    /// </summary>
    public async Task LoadStateAsync()
    {
        try
        {
            string? json = await _cacheService.LoadLearningStateAsync(LearningStateKey).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (!doc.TryGetProperty("OptimizationCycle", out var cycleProp))
            {
                return;
            }

            if (doc.TryGetProperty("LearningRate", out var lrProp) && lrProp.ValueKind == JsonValueKind.Number)
            {
                _learningRate = lrProp.GetDouble();
            }

            _optimizationCycle = cycleProp.GetInt32();

            if (doc.TryGetProperty("FactorPerformance", out var fpProp) && fpProp.ValueKind != JsonValueKind.Null)
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, FactorPerformance>>(fpProp.GetRawText());
                if (dict != null)
                {
                    lock (_lock)
                    {
                        _factorPerformance.Clear();
                        foreach (var kv in dict)
                        {
                            _factorPerformance[kv.Key] = kv.Value;
                        }
                    }
                }
            }

            LogService.Info("[LearningModule] 学习状态加载完成 (周期={0})", _optimizationCycle);
        }
        catch (Exception ex)
        {
            LogService.Warning("[LearningModule] 加载学习状态失败: {0}", ex.Message);
        }
    }
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

/// <summary>
/// 🆕 Phase 3: 因子表现记录
/// </summary>
public class FactorPerformance
{
    public string FactorCode { get; set; } = string.Empty;
    public int TotalSamples { get; set; }
    public int SuccessfulPredictions { get; set; }
    public double TotalProfit { get; set; }
    public List<double> ScoreHistory { get; set; } = new();
    public List<double> OutcomeHistory { get; set; } = new();
}

/// <summary>
/// 🆕 Phase 3: 因子性能统计
/// </summary>
public class FactorPerformanceStats
{
    public string FactorCode { get; init; } = string.Empty;
    public int TotalSamples { get; init; }
    public double WinRate { get; init; }
    public double AvgProfit { get; init; }
    public double Correlation { get; init; } // 与结果的相关性
}

#endregion
