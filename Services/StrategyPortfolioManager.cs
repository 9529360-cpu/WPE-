using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 策略组合管理器
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. 管理多个策略的并行运行
/// 2. 资金分配和权重管理
/// 3. 策略绩效对比分析
/// 4. 策略启停控制
/// 5. 组合收益统计
/// </remarks>
public sealed class StrategyPortfolioManager
{
    private readonly TradingAccountManager _accountManager;
    private readonly Dictionary<string, StrategyInstance> _strategies = new();
    private readonly object _lock = new();

    public StrategyPortfolioManager(TradingAccountManager accountManager)
    {
        _accountManager = accountManager;
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

            _strategies[strategy.Id] = strategy;
            LogService.Info("[StrategyPortfolio] 策略已添加: {Name} (权重: {Weight:P0})",
                strategy.Name, strategy.Weight);
        }
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
                LogService.Info("[StrategyPortfolio] 策略已移除: {Name}", strategy.Name);
            }
        }
    }

    /// <summary>
    /// 启动策略
    /// </summary>
    public async Task StartStrategyAsync(string strategyId)
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
        }

        await Task.CompletedTask;
        LogService.Info("[StrategyPortfolio] 策略已启动: {Name}", _strategies[strategyId].Name);
    }

    /// <summary>
    /// 停止策略
    /// </summary>
    public void StopStrategy(string strategyId)
    {
        lock (_lock)
        {
            if (!_strategies.TryGetValue(strategyId, out StrategyInstance? strategy))
            {
                return;
            }

            strategy.IsRunning = false;
            strategy.StopTime = DateTime.UtcNow;
            LogService.Info("[StrategyPortfolio] 策略已停止: {Name}", strategy.Name);
        }
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
            LogService.Info("[StrategyPortfolio] 策略权重已更新: {Name} -> {Weight:P0}",
                strategy.Name, weight);
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

            return new PortfolioStats
            {
                TotalStrategies = _strategies.Count,
                RunningStrategies = _strategies.Values.Count(s => s.IsRunning),
                TotalReturn = totalReturn,
                SharpeRatio = sharpeRatio,
                TotalTrades = totalTrades,
                WinRate = winRate,
                BestPerformer = GetBestPerformer(),
                WorstPerformer = GetWorstPerformer()
            };
        }
    }

    /// <summary>
    /// 获取表现最好的策略
    /// </summary>
    private StrategyInstance? GetBestPerformer()
    {
        return _strategies.Values
            .Where(s => s.TotalTrades > 0)
            .OrderByDescending(s => s.TotalReturn)
            .FirstOrDefault();
    }

    /// <summary>
    /// 获取表现最差的策略
    /// </summary>
    private StrategyInstance? GetWorstPerformer()
    {
        return _strategies.Values
            .Where(s => s.TotalTrades > 0)
            .OrderBy(s => s.TotalReturn)
            .FirstOrDefault();
    }

    /// <summary>
    /// 验证权重总和
    /// </summary>
    public bool ValidateWeights()
    {
        lock (_lock)
        {
            double totalWeight = _strategies.Values.Sum(s => s.Weight);
            return Math.Abs(totalWeight - 1.0) < 0.01; // 允许 1% 误差
        }
    }

    /// <summary>
    /// 自动平衡权重
    /// </summary>
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
}

/// <summary>
/// 组合统计
/// </summary>
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
}
