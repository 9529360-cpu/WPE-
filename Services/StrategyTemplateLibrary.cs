using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

public sealed class StrategyTemplateLibrary
{
    private readonly string _path;
    private readonly object _lock = new();

    public StrategyTemplateLibrary(string? baseDir = null)
    {
        _path = Path.Combine(baseDir ?? AppContext.BaseDirectory, "Data", "strategy_templates.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public void SaveTemplate(StrategyTemplate template)
    {
        lock (_lock)
        {
            var list = LoadAllInternal();
            list.Add(template with { Timestamp = DateTime.UtcNow });
            Persist(list);
        }
    }

    public IReadOnlyList<StrategyTemplate> LoadAll()
    {
        lock (_lock)
        {
            return LoadAllInternal();
        }
    }

    public StrategyTemplate? GetBest(string type, IReadOnlyCollection<string> symbols)
    {
        lock (_lock)
        {
            var list = LoadAllInternal();
            return list
                .Where(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase) && t.Symbols.OrderBy(s => s).SequenceEqual(symbols.OrderBy(s => s)))
                .OrderByDescending(t => t.Metrics.Sharpe)
                .ThenBy(t => t.Metrics.MaxDrawdown)
                .FirstOrDefault();
        }
    }

    private List<StrategyTemplate> LoadAllInternal()
    {
        if (!File.Exists(_path))
        {
            return new List<StrategyTemplate>();
        }
        try
        {
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<StrategyTemplate>>(json) ?? new List<StrategyTemplate>();
        }
        catch
        {
            return new List<StrategyTemplate>();
        }
    }

    private void Persist(List<StrategyTemplate> list)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_path, JsonSerializer.Serialize(list, opts));
    }
}

public record StrategyTemplate(
    string Name,
    string Type,
    IReadOnlyList<string> Symbols,
    AccountType AccountType,
    IReadOnlyDictionary<string, double> Parameters,
    TemplateMetrics Metrics,
    DateTime Timestamp);

public record TemplateMetrics(double WinRate, double MaxDrawdown, double Sharpe, double ProfitFactor);
