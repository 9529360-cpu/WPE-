using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;
using 币安量化机器人.Models.Configuration;
using 币安量化机器人.Services.AI;
using 币安量化机器人.Services.Observability;

namespace 币安量化机器人.Services.AI;

public sealed class AIStrategyGenerator
{
    private const int DefaultAiTimeoutSeconds = 25;

    public async Task<Services.StrategyInstance> GenerateAsync(string[] symbols, CancellationToken ct = default)
    {
        using var act = TraceManager.StartActivity("AIStrategy.Generate", System.Diagnostics.ActivityKind.Producer);
        TraceManager.AddTag("symbols", string.Join(',', symbols));

        if (symbols == null || symbols.Length == 0)
        {
            throw new ArgumentException("symbols 不能为空", nameof(symbols));
        }

        AIConfig aiConfig = Services.ConfigurationService.GetAIConfig();

        // If AI is disabled or API key missing, return a safe fallback strategy
        if (!aiConfig.EnableAITrading || string.IsNullOrWhiteSpace(aiConfig.DeepSeekApiKey))
        {
            LogService.Warning("[AIStrategyGenerator] AI 生成被禁用或未配置 API Key，使用本地回退策略");
            var fb = Fallback(symbols);
            TraceManager.AddTag("strategy.id", fb.Id);
            TraceManager.AddEvent("strategy.fallback");
            return fb;
        }

        var agent = new DeepSeekTradingAgent(aiConfig.DeepSeekApiKey, aiConfig.Temperature);
        if (!agent.IsEnabled)
        {
            LogService.Warning("[AIStrategyGenerator] DeepSeek agent 未启用或 API Key 无效，使用回退策略");
            var fb = Fallback(symbols);
            TraceManager.AddTag("strategy.id", fb.Id);
            TraceManager.AddEvent("strategy.fallback");
            return fb;
        }

        string prompt = "请基于以下交易对生成一个实用交易策略(JSON，仅JSON，无解释)。字段: name,type,symbols(parameters为对象),weight(0.05-0.5),accountType(可选:Simulated/Live)。Symbols: " + string.Join(",", symbols);

        string response = string.Empty;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(DefaultAiTimeoutSeconds));

        try
        {
            LogService.Info("[AIStrategyGenerator] 开始请求 AI 生成策略，symbols={Symbols}", string.Join(',', symbols));
            response = await agent.ChatAsync(prompt, context: null, timeoutCts.Token).ConfigureAwait(false);
            LogService.Info("[AIStrategyGenerator] AI 响应长度: {Len}", response?.Length ?? 0);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            LogService.Warning("[AIStrategyGenerator] AI 请求超时，使用回退策略");
            var fb = Fallback(symbols);
            TraceManager.AddTag("strategy.id", fb.Id);
            TraceManager.AddEvent("strategy.fallback");
            return fb;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AIStrategyGenerator] 调用 DeepSeek 失败，使用回退策略");
            var fb = Fallback(symbols);
            TraceManager.AddTag("strategy.id", fb.Id);
            TraceManager.AddEvent("strategy.fallback");
            return fb;
        }

        try
        {
            var parameters = new Dictionary<string, double>();

            string json = TryExtractJson(response) ?? response;
            var dto = System.Text.Json.JsonSerializer.Deserialize<AIStrategyProposalDto>(json);
            if (dto != null)
            {
                string name = string.IsNullOrWhiteSpace(dto.Name) ? $"AI-Strategy-{DateTime.Now:HHmmss}" : dto.Name.Trim();
                string type = string.IsNullOrWhiteSpace(dto.Type) ? "Momentum" : dto.Type.Trim();
                double weight = dto.Weight;
                if (weight <= 0.0 || weight > 0.9)
                {
                    weight = 0.25;
                }

                var syms = (dto.Symbols?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList() ?? symbols.ToList());
                parameters = dto.Parameters ?? new Dictionary<string, double>();
                var acct = ParseAccountType(dto.AccountType);

                // Enrich parameters using template library if available
                try
                {
                    var best = Services.ServiceLocator.StrategyTemplates.GetBest(type, syms);
                    if (best != null)
                    {
                        foreach (var kv in best.Parameters)
                        {
                            if (!parameters.ContainsKey(kv.Key))
                            {
                                parameters[kv.Key] = kv.Value;
                            }
                        }
                    }

                    if (parameters.Count == 0)
                    {
                        parameters = best?.Parameters?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<string, double> { { "k", 14 } };
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warning("[AIStrategyGenerator] 从模板库增强参数失败: {Error}", ex.Message);
                    if (parameters.Count == 0)
                    {
                        parameters = new Dictionary<string, double> { { "k", 14 } };
                    }
                }

                var strat = new Services.StrategyInstance
                {
                    Name = name,
                    Type = type,
                    Symbols = syms,
                    Weight = Math.Clamp(weight, 0.05, 0.5),
                    Parameters = parameters,
                    AccountType = acct,
                    Market = Services.MarketType.Futures
                };

                TraceManager.AddTag("strategy.id", strat.Id);
                TraceManager.AddEvent("strategy.generated");

                try
                {
                    var cache = ServiceLocator.Cache;
                    await cache.SaveSignalAsync(strat.Symbols?.FirstOrDefault() ?? "", "strategy_generated", 1.0, strat.Name, "AIStrategyGenerator", DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    LogService.Warning("保存AI生成信号失败: {Message}", ex.Message);
                }

                return strat;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AIStrategyGenerator] 解析 AI 返回结果失败，使用回退策略");
            var fb = Fallback(symbols);
            TraceManager.AddTag("strategy.id", fb.Id);
            TraceManager.AddEvent("strategy.fallback");
            return fb;
        }

        var fallback = Fallback(symbols);
        TraceManager.AddTag("strategy.id", fallback.Id);
        TraceManager.AddEvent("strategy.fallback");
        return fallback;
    }

    private static Services.StrategyInstance Fallback(string[] symbols)
    {
        return new Services.StrategyInstance
        {
            Name = $"AI-Strategy-{DateTime.Now:HHmmss}",
            Type = "Momentum",
            Symbols = symbols.ToList(),
            Weight = 0.25,
            Parameters = new Dictionary<string, double> { { "k", 14 } },
            AccountType = 币安量化机器人.Models.AccountType.Simulated,
            Market = Services.MarketType.Futures
        };
    }

    private static string? TryExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1);
        }
        return null;
    }

    private static 币安量化机器人.Models.AccountType ParseAccountType(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return 币安量化机器人.Models.AccountType.Simulated;
        }
        return s.Trim().Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? 币安量化机器人.Models.AccountType.Live
            : 币安量化机器人.Models.AccountType.Simulated;
    }
}

public sealed class AIStrategyProposalDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public System.Collections.Generic.List<string>? Symbols { get; set; }
    public System.Collections.Generic.Dictionary<string, double>? Parameters { get; set; }
    public double Weight { get; set; } = 0.25;
    public string? AccountType { get; set; }
}
