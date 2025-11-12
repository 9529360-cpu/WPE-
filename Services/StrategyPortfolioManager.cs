using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

public sealed class StrategyPortfolioManager
{
    private readonly TradingAccountManager _accountManager;
    private readonly Dictionary<string, StrategyInstance> _strategies = new();
    private readonly object _lock = new();
    private readonly string _storagePath;

    // 事件：当策略集合发生变化时触发（UI 可订阅以刷新）
    public event Action? StrategiesChanged;

    public StrategyPortfolioManager(TradingAccountManager accountManager, string? storageDirectory = null)
    {
        _accountManager = accountManager;
        _storagePath = Path.Combine(storageDirectory ?? AppContext.BaseDirectory, "Data", "strategies.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
    }

    private void OnStrategiesChanged()
    {
        try
        {
            StrategiesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LogService.Warning("[StrategyPortfolio] StrategiesChanged handler threw: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// 添加策略到组合
    /// </summary>
    public void AddStrategy(StrategyInstance strategy)
    {
        lock (_lock)
        {
            if (_strategies.ContainsKey(strategy.Id))
            {
                throw new InvalidOperationException($"策略 {strategy.Name} 已存在");
            }

            // 默认合约模式与审计补全
            strategy.Market = strategy.Market == 0 ? MarketType.Futures : strategy.Market;
            strategy.Audit ??= new StrategyAudit
            {
                Source = "AI",
                ModelVersion = ServiceLocator.AIStrategyGenerator is not null ? "deepseek-chat" : "unknown",
                CreatedUtc = DateTime.UtcNow
            };

            _strategies[strategy.Id] = strategy;
            SaveToDisk();
        }
        LogService.Info("[StrategyPortfolio] 策略已添加: {Name} (权重: {Weight:P0})", strategy.Name, strategy.Weight);
        OnStrategiesChanged();
    }

    /// <summary>
    /// 快速创建策略
    /// </summary>
    public StrategyInstance CreateStrategy(string name, string type, IEnumerable<string> symbols, double weight)
    {
        var s = new StrategyInstance
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"策略-{DateTime.Now:HHmmss}" : name,
            Type = type,
            Symbols = symbols?.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList() ?? new List<string>(),
            Weight = weight <= 0 || weight > 1 ? 0.25 : weight,
            Stage = StrategyStage.Idle,
            Market = MarketType.Futures,
            Audit = new StrategyAudit { Source = "Manual", ModelVersion = string.Empty, CreatedUtc = DateTime.UtcNow }
        };
        AddStrategy(s);
        return s;
    }

    /// <summary>
    /// 移除策略
    /// </summary>
    public void RemoveStrategy(string strategyId)
    {
        lock (_lock)
        {
            if (_strategies.Remove(strategyId, out StrategyInstance? strategy))
            {
                if (strategy.IsRunning)
                {
                    StopStrategy(strategyId);
                }
                SaveToDisk();
                LogService.Info("[StrategyPortfolio] 策略已移除: {Name}", strategy.Name);
            }
        }
        OnStrategiesChanged();
    }

    /// <summary>
    /// 启动策略
    /// </summary>
    public async Task StartStrategyAsync(string strategyId, StrategyStage stage = StrategyStage.LiveRunning, Models.AccountType? runAs = null)
    {
        lock (_lock)
        {
            if (!_strategies.TryGetValue(strategyId, out StrategyInstance? strategy))
            {
                throw new InvalidOperationException($"策略 {strategyId} 不存在");
            }

            if (strategy.IsRunning)
            {
                throw new InvalidOperationException($"策略 {strategy.Name} 已在运行中");
            }

            strategy.IsRunning = true;
            strategy.StartTime = DateTime.UtcNow;
            strategy.Stage = stage;
        }

        SaveToDisk();

        // Determine which account to run under: explicit override (runAs) or strategy.AccountType
        var effectiveAccount = runAs ?? _strategies[strategyId].AccountType;

        // Try to start AutoTrader for this strategy (non-blocking)
        try
        {
            var symbols = _strategies[strategyId].Symbols?.ToArray() ?? Array.Empty<string>();
            var acctType = effectiveAccount;
            // Start the global AutoTradingController with these symbols
            _ = ServiceLocator.AutoTrader.StartAsync(symbols, acctType, enableScalping: false);
            LogService.Info("[StrategyPortfolio] 请求启动 AutoTrader for strategy {Name} (Acct={Acct})", _strategies[strategyId].Name, acctType);
        }
        catch (Exception ex)
        {
            LogService.Warning("[StrategyPortfolio] 启动 AutoTrader 失败: {Message}", ex.Message);
        }

        await Task.CompletedTask;
        LogService.Info("[StrategyPortfolio] 策略已启动: {Name}", _strategies[strategyId].Name);
        OnStrategiesChanged();
    }

    /// <summary>
    /// 停止策略
    /// </summary>
    public void StopStrategy(string strategyId)
    {
        lock (_lock)
        {
            if (_strategies.TryGetValue(strategyId, out StrategyInstance? strategy))
            {
                strategy.IsRunning = false;
                strategy.StopTime = DateTime.UtcNow;
                strategy.Stage = StrategyStage.Idle;
                SaveToDisk();
                LogService.Info("[StrategyPortfolio] 策略已停止: {Name}", strategy.Name);
            }
        }
        OnStrategiesChanged();
    }

    /// <summary>
    /// 更新策略权重
    /// </summary>
    public void UpdateStrategyWeight(string strategyId, double weight)
    {
        lock (_lock)
        {
            if (!_strategies.TryGetValue(strategyId, out StrategyInstance? strategy))
            {
                throw new InvalidOperationException($"策略 {strategyId} 不存在");
            }

            if (weight < 0 || weight > 1)
            {
                throw new ArgumentException("权重必须在 0-1 之间");
            }

            strategy.Weight = weight;
            SaveToDisk();
            LogService.Info("[StrategyPortfolio] 策略权重已更新: {Name} -> {Weight:P0}",
                strategy.Name, weight);
        }
    }

    /// <summary>
    /// 进度与阶段上报
    /// </summary>
    public void UpdateProgress(string strategyId, long processedBars, long? targetBars = null, StrategyStage? stage = null)
    {
        lock (_lock)
        {
            if (!_strategies.TryGetValue(strategyId, out StrategyInstance? s))
            {
                return;
            }
            s.ProcessedBars = processedBars;
            if (targetBars.HasValue)
            {
                s.TargetBars = targetBars.Value;
            }
            if (stage.HasValue)
            {
                s.Stage = stage.Value;
            }
        }
    }

    /// <summary>
    /// 获取所有策略
    /// </summary>
    public IReadOnlyList<StrategyInstance> GetAllStrategies()
    {
        lock (_lock)
        {
            return _strategies.Values.ToList();
        }
    }

    /// <summary>
    /// 获取运行中的策略
    /// </summary>
    public IReadOnlyList<StrategyInstance> GetRunningStrategies()
    {
        lock (_lock)
        {
            return _strategies.Values.Where(s => s.IsRunning).ToList();
        }
    }

    /// <summary>
    /// 获取特定账户类型的策略
    /// </summary>
    public IReadOnlyList<StrategyInstance> GetStrategies(AccountType accountType)
    {
        lock (_lock)
        {
            return _strategies.Values.Where(s => s.AccountType == accountType).ToList();
        }
    }

    /// <summary>
    /// 计算组合总收益
    /// </summary>
    public decimal CalculatePortfolioReturn()
    {
        lock (_lock)
        {
            return _strategies.Values.Sum(s => s.TotalReturn * (decimal)s.Weight);
        }
    }

    /// <summary>
    /// 计算组合夏普比率
    /// </summary>
    public double CalculatePortfolioSharpe()
    {
        lock (_lock)
        {
            var strategies = _strategies.Values.ToList();
            if (!strategies.Any())
            {
                return 0;
            }

            double weightedSharpe = strategies.Sum(s => s.SharpeRatio * s.Weight);
            return weightedSharpe;
        }
    }

    private StrategyInstance? GetBestPerformer()
    {
        return _strategies.Values
            .Where(s => s.TotalTrades > 0)
            .OrderByDescending(s => s.TotalReturn)
            .FirstOrDefault();
    }

    private StrategyInstance? GetWorstPerformer()
    {
        return _strategies.Values
            .Where(s => s.TotalTrades > 0)
            .OrderBy(s => s.TotalReturn)
            .FirstOrDefault();
    }

    public bool ValidateWeights()
    {
        lock (_lock)
        {
            double totalWeight = _strategies.Values.Sum(s => s.Weight);
            return Math.Abs(totalWeight - 1.0) < 0.01;
        }
    }

    public void AutoBalanceWeights()
    {
        lock (_lock)
        {
            int count = _strategies.Count;
            if (count == 0)
            {
                return;
            }

            double equalWeight = 1.0 / count;
            foreach (StrategyInstance strategy in _strategies.Values)
            {
                strategy.Weight = equalWeight;
            }

            LogService.Info("[StrategyPortfolio] 权重已自动平衡: 每个策略 {Weight:P0}", equalWeight);
        }
    }

    public void AdjustWeightsByRisk(double maxWeightReductionPerStep = 0.1)
    {
        lock (_lock)
        {
            if (_strategies.Count == 0)
            {
                return;
            }
            double total = 0;
            foreach (var s in _strategies.Values)
            {
                double ddPenalty = Math.Clamp(s.MaxDrawdown, 0, 0.5);
                double volPenalty = Math.Clamp(s.Volatility, 0, 1.0) * 0.5;
                double sharpeBoost = Math.Max(0, s.SharpeRatio) / 4.0;
                double target = s.Weight * (1 + sharpeBoost - ddPenalty - volPenalty);
                double delta = Math.Clamp(target - s.Weight, -maxWeightReductionPerStep, maxWeightReductionPerStep);
                s.Weight = Math.Clamp(s.Weight + delta, 0.01, 0.8);
                total += s.Weight;
            }
            foreach (var s in _strategies.Values)
            {
                s.Weight /= total;
            }
            SaveToDisk();
        }
    }

    /// <summary>
    /// 保存策略组合到磁盘
    /// </summary>
    public void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_strategies.Values, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
            // 在每次成功持久化后通知订阅者
            OnStrategiesChanged();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "保存策略到磁盘失败");
        }
    }

    /// <summary>
    /// 从磁盘加载策略组合
    /// </summary>
    public void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return;
            }

            var json = File.ReadAllText(_storagePath);
            var list = JsonSerializer.Deserialize<List<StrategyInstance>>(json) ?? new List<StrategyInstance>();
            lock (_lock)
            {
                _strategies.Clear();
                foreach (var s in list)
                {
                    // Backfill audit & force Futures as default market
                    s.Audit ??= new StrategyAudit { Source = string.IsNullOrEmpty(s.Description) ? "AI" : "Manual", ModelVersion = string.Empty, CreatedUtc = s.StartTime ?? DateTime.UtcNow };
                    if (s.Market == 0)
                    {
                        s.Market = MarketType.Futures;
                    }
                    _strategies[s.Id] = s;
                }
            }
            OnStrategiesChanged();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "加载策略失败");
        }
    }

    /// <summary>
    /// 获取组合统计
    /// </summary>
    public PortfolioStats GetPortfolioStats()
    {
        lock (_lock)
        {
            decimal totalReturn = CalculatePortfolioReturn();
            double sharpeRatio = CalculatePortfolioSharpe();
            int totalTrades = _strategies.Values.Sum(s => s.TotalTrades);
            double winRate = totalTrades > 0
                ? _strategies.Values.Sum(s => s.WinningTrades) / (double)totalTrades
                : 0;

            // 新增聚合风险指标
            double portVol = CalculatePortfolioVolatility();
            double riskUtil = CalculateRiskUtilization();

            return new PortfolioStats
            {
                TotalStrategies = _strategies.Count,
                RunningStrategies = _strategies.Values.Count(s => s.IsRunning),
                TotalReturn = totalReturn,
                SharpeRatio = sharpeRatio,
                TotalTrades = totalTrades,
                WinRate = winRate,
                BestPerformer = GetBestPerformer(),
                WorstPerformer = GetWorstPerformer(),
                Volatility = portVol,
                RiskUtilization = riskUtil
            };
        }
    }

    /// <summary>
    /// 组合年化波动率（加权）
    /// </summary>
    public double CalculatePortfolioVolatility()
    {
        lock (_lock)
        {
            if (_strategies.Count == 0)
            {
                return 0;
            }
            return _strategies.Values.Sum(s => s.Volatility * s.Weight);
        }
    }

    /// <summary>
    /// 风险利用率 (此处用组合夏普近似)
    /// </summary>
    public double CalculateRiskUtilization()
    {
        lock (_lock)
        {
            double portVol = CalculatePortfolioVolatility();
            if (portVol <= 1e-9)
            {
                return 0;
            }
            return CalculatePortfolioSharpe();
        }
    }

    // 更新策略绩效快照（供回测/实盘收集后写入）
    public void UpdatePerformanceSnapshot(string strategyId, decimal totalReturn, double sharpe, double maxDrawdown, int totalTrades, int winningTrades, double? volatility = null)
    {
        lock (_lock)
        {
            if (!_strategies.TryGetValue(strategyId, out var s))
            {
                return;
            }
            s.TotalReturn = totalReturn;
            s.SharpeRatio = sharpe;
            s.MaxDrawdown = maxDrawdown;
            s.TotalTrades = totalTrades;
            s.WinningTrades = winningTrades;
            if (volatility.HasValue)
            {
                s.Volatility = volatility.Value;
            }
        }
        SaveToDisk();
    }
}

public enum StrategyStage
{
    Idle,
    Backtesting,
    Optimizing,
    PaperRunning,
    LiveRunning
}

/// <summary>
/// 市场类型（现货/合约）
/// </summary>
public enum MarketType
{
    Spot = 0,
    Futures = 1
}

/// <summary>
/// 审计元数据：用于记录策略来源、模型版本与时间戳
/// </summary>
public record StrategyAudit
{
    public string Source { get; init; } = string.Empty; // AI / Manual / Import
    public string ModelVersion { get; init; } = string.Empty; // e.g., deepseek-chat@2024-xx
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 策略实例
/// </summary>
public class StrategyInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // MeanReversion, Momentum, Grid, etc.
    public double Weight { get; set; } = 0.25; // 资金权重
    public bool IsRunning { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? StopTime { get; set; }

    // 市场类型：默认合约
    public MarketType Market { get; set; } = MarketType.Futures;

    // 账户分区
    public AccountType AccountType { get; set; } = AccountType.Simulated;

    // 进度与阶段
    public StrategyStage Stage { get; set; } = StrategyStage.Idle;
    public long ProcessedBars { get; set; }
    public long TargetBars { get; set; }
    public double Progress => TargetBars > 0 ? Math.Clamp(ProcessedBars / (double)TargetBars * 100.0, 0, 100) : (IsRunning ? Math.Clamp((DateTime.UtcNow - (StartTime ?? DateTime.UtcNow)).TotalMinutes % 100, 0, 100) : 0);

    // 绩效指标
    public decimal TotalReturn { get; set; }
    public double SharpeRatio { get; set; }
    public double MaxDrawdown { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades : 0;

    // 配置参数
    public Dictionary<string, double> Parameters { get; set; } = new();

    // 交易对
    public List<string> Symbols { get; set; } = new();

    public TimeSpan RunningTime =>
        StartTime.HasValue ? (StopTime ?? DateTime.UtcNow) - StartTime.Value : TimeSpan.Zero;

    public double Volatility { get; set; } // 年化波动率 (0-1)

    // 审计元数据
    public StrategyAudit? Audit { get; set; }
}

public class PortfolioStats
{
    public int TotalStrategies { get; set; }
    public int RunningStrategies { get; set; }
    public decimal TotalReturn { get; set; }
    public double SharpeRatio { get; set; }
    public int TotalTrades { get; set; }
    public double WinRate { get; set; }
    public StrategyInstance? BestPerformer { get; set; }
    public StrategyInstance? WorstPerformer { get; set; }
    public double Volatility { get; set; }
    public double RiskUtilization { get; set; }
}
