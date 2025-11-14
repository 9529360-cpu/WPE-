using System;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Services;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 异步策略建议服务：
/// - 使用 AIStrategyGenerator 生成策略草案
/// - 可选将草案保存为模板
/// - 将草案以非运行状态写入策略组合供人工审阅/启用
/// </summary>
public sealed class AIStrategySuggestionService
{
    private readonly AIStrategyGenerator _generator;
    private readonly StrategyTemplateLibrary _templateLibrary;
    private readonly StrategyPortfolioManager _portfolio;

    public AIStrategySuggestionService(AIStrategyGenerator generator, StrategyTemplateLibrary templateLibrary, StrategyPortfolioManager portfolio)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _templateLibrary = templateLibrary ?? throw new ArgumentNullException(nameof(templateLibrary));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
    }

    /// <summary>
    /// 生成策略建议。
    /// - saveAsTemplate: 是否将结果保存为模板
    /// - addToPortfolio: 是否将结果添加到策略组合（默认 true）。
    /// 返回创建的 StrategyInstance 供 UI 显示。
    /// </summary>
    public async Task<StrategyInstance> GenerateAndStoreAsync(string[] symbols, bool saveAsTemplate = false, bool addToPortfolio = true, CancellationToken ct = default)
    {
        if (symbols == null || symbols.Length == 0)
        {
            throw new ArgumentException("symbols 不能为空", nameof(symbols));
        }

        var instance = await _generator.GenerateAsync(symbols, ct).ConfigureAwait(false);

        // Ensure non-running by default
        instance.IsRunning = false;
        instance.Stage = StrategyStage.Idle;

        // Attach audit metadata for AI generated instance
        instance.Audit = new StrategyAudit
        {
            Source = "AI",
            ModelVersion = "deepseek-chat",
            CreatedUtc = DateTime.UtcNow
        };

        // Optionally add to portfolio (persisted)
        if (addToPortfolio)
        {
            try
            {
                _portfolio.AddStrategy(instance);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[AIStrategySuggestionService] 将生成策略添加到组合失败");
            }
        }

        // Optionally persist as template (for later reuse)
        if (saveAsTemplate)
        {
            try
            {
                var template = new StrategyTemplate(
                    Name: instance.Name,
                    Type: instance.Type,
                    Symbols: instance.Symbols.AsReadOnly(),
                    AccountType: instance.AccountType,
                    Parameters: instance.Parameters as IReadOnlyDictionary<string, double> ?? new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(instance.Parameters),
                    Metrics: new TemplateMetrics(WinRate: 0, MaxDrawdown: 0, Sharpe: 0, ProfitFactor: 0),
                    Timestamp: DateTime.UtcNow,
                    Audit: new TemplateAudit { Source = "AI", ModelVersion = "deepseek-chat", CreatedUtc = DateTime.UtcNow }
                );

                _templateLibrary.SaveTemplate(template);
            }
            catch (Exception ex)
            {
                // 吃掉异常但记录日志
                LogService.Error(ex, "[AIStrategySuggestionService] 保存模板失败");
            }
        }

        LogService.Info("[AIStrategySuggestionService] 生成策略建议完成: {Name} (symbols: {Symbols}, addToPortfolio: {AddToPortfolio}, saveAsTemplate: {SaveAsTemplate}, model: {Model}, at: {Time})", instance.Name, string.Join(',', instance.Symbols), addToPortfolio, saveAsTemplate, "deepseek-chat", DateTime.UtcNow);

        return instance;
    }
}
