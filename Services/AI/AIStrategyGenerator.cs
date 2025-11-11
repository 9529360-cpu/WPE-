using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;
using 币安量化机器人.Models.Configuration;

namespace 币安量化机器人.Services.AI;

public sealed class AIStrategyGenerator
{
    public async Task<Services.StrategyInstance> GenerateAsync(string[] symbols, CancellationToken ct = default)
    {
        string key = Services.ConfigurationService.GetDeepSeekApiKey();
        var agent = new DeepSeekTradingAgent(key);
        string prompt = "请基于以下交易对生成一个实用交易策略(JSON，仅JSON，无解释)。字段: name,type,symbols(parameters为对象),weight(0.05-0.5),accountType(可选:Simulated/Live)。Symbols: " + string.Join(",", symbols);
        string response = await agent.ChatAsync(prompt, context: null, ct);

        var parameters = new Dictionary<string, double>();
        try
        {
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

                return new Services.StrategyInstance
                {
                    Name = name,
                    Type = type,
                    Symbols = syms,
                    Weight = Math.Clamp(weight, 0.05, 0.5),
                    Parameters = parameters,
                    AccountType = acct
                };
            }
        }
        catch
        {
            // ignore and fallback
        }

        return Fallback(symbols);
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
            AccountType = 币安量化机器人.Models.AccountType.Simulated
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
