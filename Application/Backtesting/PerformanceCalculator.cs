using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Application.Backtesting;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Application.Backtesting;

public class PerformanceCalculator
{
    private static class PerformanceConstants
    {
        public const double ANNUAL_TRADING_DAYS = 252.0;
        public static readonly double ANNUAL_FACTOR = Math.Sqrt(ANNUAL_TRADING_DAYS);
        public const int MIN_SAMPLE_SIZE = 2;
    }

    public PerformanceMetrics Calculate(IReadOnlyList<BacktestTrade> trades, double initialCapital)
    {
        if (trades.Count == 0)
        {
            return new PerformanceMetrics
            {
                TotalTrades = 0,
                NetProfit = 0,
                SharpeRatio = 0,
                SortinoRatio = 0,
                CalmarRatio = 0,
                MaxDrawdown = 0,
                WinRate = 0,
                ProfitFactor = 0,
                AvgWin = 0,
                AvgLoss = 0,
                LargestWin = 0,
                LargestLoss = 0,
                MaxConsecutiveWins = 0,
                MaxConsecutiveLosses = 0,
                AvgTradeDuration = TimeSpan.Zero,
                RecoveryFactor = 0,
                ReturnPercent = 0,
                MaxDrawdownPercent = 0,
                ExpectancyPerTrade = 0,
                Volatility = 0
            };
        }

        var equity = new List<double> { initialCapital };
        double currentEquity = initialCapital;

        foreach (BacktestTrade trade in trades)
        {
            currentEquity += trade.NetPnL;
            equity.Add(currentEquity);
        }

        double netProfit = currentEquity - initialCapital;
        double[] returns = equity.Zip(equity.Skip(1), (prev, next) => (next - prev) / prev).ToArray();

        var wins = trades.Where(t => t.NetPnL > 0).ToList();
        var losses = trades.Where(t => t.NetPnL <= 0).ToList();

        double volatility = CalculateVolatility(returns);

        return new PerformanceMetrics
        {
            TotalTrades = trades.Count,
            NetProfit = netProfit,
            ReturnPercent = (netProfit / initialCapital) * 100,
            SharpeRatio = CalculateSharpeRatio(returns),
            SortinoRatio = CalculateSortinoRatio(returns),
            CalmarRatio = CalculateCalmarRatio(netProfit, initialCapital, equity),
            MaxDrawdown = CalculateMaxDrawdown(equity),
            MaxDrawdownPercent = (CalculateMaxDrawdown(equity) / initialCapital) * 100,
            WinRate = wins.Count / (double)trades.Count,
            ProfitFactor = CalculateProfitFactor(wins, losses),
            AvgWin = wins.Any() ? wins.Average(t => t.NetPnL) : 0,
            AvgLoss = losses.Any() ? Math.Abs(losses.Average(t => t.NetPnL)) : 0,
            LargestWin = wins.Any() ? wins.Max(t => t.NetPnL) : 0,
            LargestLoss = losses.Any() ? Math.Abs(losses.Min(t => t.NetPnL)) : 0,
            MaxConsecutiveWins = CalculateMaxConsecutiveWins(trades),
            MaxConsecutiveLosses = CalculateMaxConsecutiveLosses(trades),
            AvgTradeDuration = CalculateAvgTradeDuration(trades),
            RecoveryFactor = CalculateRecoveryFactor(netProfit, equity),
            ExpectancyPerTrade = trades.Average(t => t.NetPnL),
            Volatility = volatility
        };
    }

    private double CalculateSharpeRatio(double[] returns)
    {
        if (returns.Length < PerformanceConstants.MIN_SAMPLE_SIZE)
        {
            return 0;
        }

        double avgReturn = returns.Average();
        double stdReturn = Math.Sqrt(returns.Sum(r => Math.Pow(r - avgReturn, 2)) / returns.Length);

        if (stdReturn == 0)
        {
            return 0;
        }

        return (avgReturn / stdReturn) * PerformanceConstants.ANNUAL_FACTOR;
    }

    private double CalculateSortinoRatio(double[] returns)
    {
        if (returns.Length < PerformanceConstants.MIN_SAMPLE_SIZE)
        {
            return 0;
        }

        double avgReturn = returns.Average();
        double[] downside = returns.Where(r => r < 0).ToArray();

        if (downside.Length == 0)
        {
            return double.PositiveInfinity;
        }

        double downsideStd = Math.Sqrt(downside.Sum(r => r * r) / downside.Length);

        if (downsideStd == 0)
        {
            return 0;
        }

        return (avgReturn / downsideStd) * PerformanceConstants.ANNUAL_FACTOR;
    }

    private double CalculateCalmarRatio(double netProfit, double initialCapital, List<double> equity)
    {
        double maxDD = CalculateMaxDrawdown(equity);
        if (maxDD == 0)
        {
            return 0;
        }

        double annualReturn = (netProfit / initialCapital) * (PerformanceConstants.ANNUAL_TRADING_DAYS / equity.Count);
        return annualReturn / (maxDD / initialCapital);
    }

    private double CalculateMaxDrawdown(List<double> equity)
    {
        double maxDD = 0;
        double peak = equity[0];

        foreach (double value in equity)
        {
            if (value > peak)
            {
                peak = value;
            }

            double drawdown = peak - value;
            if (drawdown > maxDD)
            {
                maxDD = drawdown;
            }
        }

        return maxDD;
    }

    private double CalculateProfitFactor(List<BacktestTrade> wins, List<BacktestTrade> losses)
    {
        double totalWin = wins.Sum(t => t.NetPnL);
        double totalLoss = Math.Abs(losses.Sum(t => t.NetPnL));

        if (totalLoss == 0)
        {
            return totalWin > 0 ? double.PositiveInfinity : 0;
        }

        return totalWin / totalLoss;
    }

    private int CalculateMaxConsecutiveWins(IReadOnlyList<BacktestTrade> trades)
    {
        int maxWins = 0, currentWins = 0;

        foreach (BacktestTrade trade in trades)
        {
            if (trade.NetPnL > 0)
            {
                currentWins++;
                maxWins = Math.Max(maxWins, currentWins);
            }
            else
            {
                currentWins = 0;
            }
        }

        return maxWins;
    }

    private int CalculateMaxConsecutiveLosses(IReadOnlyList<BacktestTrade> trades)
    {
        int maxLosses = 0, currentLosses = 0;

        foreach (BacktestTrade trade in trades)
        {
            if (trade.NetPnL <= 0)
            {
                currentLosses++;
                maxLosses = Math.Max(maxLosses, currentLosses);
            }
            else
            {
                currentLosses = 0;
            }
        }

        return maxLosses;
    }

    private TimeSpan CalculateAvgTradeDuration(IReadOnlyList<BacktestTrade> trades)
    {
        if (trades.Count == 0)
        {
            return TimeSpan.Zero;
        }

        double avgSeconds = trades.Average(t => (t.ExitTime - t.EntryTime).TotalSeconds);
        return TimeSpan.FromSeconds(avgSeconds);
    }

    private double CalculateRecoveryFactor(double netProfit, List<double> equity)
    {
        double maxDD = CalculateMaxDrawdown(equity);
        if (maxDD == 0)
        {
            return 0;
        }

        return netProfit / maxDD;
    }

    private double CalculateVolatility(double[] returns)
    {
        if (returns.Length < 2)
        {
            return 0;
        }

        double avg = returns.Average();
        double std = Math.Sqrt(returns.Sum(r => Math.Pow(r - avg, 2)) / returns.Length);
        return std * Math.Sqrt(252.0);
    }
}

/// <summary>
/// 回测交易记录
/// </summary>
public class BacktestTrade
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// 开仓时间
    /// </summary>
    public DateTime EntryTime { get; init; }

    /// <summary>
    /// 平仓时间
    /// </summary>
    public DateTime ExitTime { get; init; }

    /// <summary>
    /// 开仓价格
    /// </summary>
    public double EntryPrice { get; init; }

    /// <summary>
    /// 平仓价格
    /// </summary>
    public double ExitPrice { get; init; }

    /// <summary>
    /// 交易数量
    /// </summary>
    public double Quantity { get; init; }

    /// <summary>
    /// 是否做多
    /// </summary>
    public bool IsLong { get; init; }

    /// <summary>
    /// 毛盈亏 (未扣除成本)
    /// </summary>
    public double GrossPnL { get; init; }

    /// <summary>
    /// 交易手续费
    /// </summary>
    public double TradingCost { get; init; }

    /// <summary>
    /// 资金费用
    /// </summary>
    public double FundingCost { get; init; }

    /// <summary>
    /// 净盈亏 (已扣除所有成本)
    /// </summary>
    public double NetPnL { get; init; }

    /// <summary>
    /// 平仓原因 (止盈/止损/信号等)
    /// </summary>
    public string? ExitReason { get; init; }
}

/// <summary>
/// 详细绩效指标
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// 总交易次数
    /// </summary>
    public int TotalTrades { get; init; }

    /// <summary>
    /// 净利润 (USDT)
    /// </summary>
    public double NetProfit { get; init; }

    /// <summary>
    /// 收益率 (%)
    /// </summary>
    public double ReturnPercent { get; init; }

    /// <summary>
    /// 夏普比率 (年化)
    /// </summary>
    public double SharpeRatio { get; init; }

    /// <summary>
    /// 索提诺比率 (年化)
    /// </summary>
    public double SortinoRatio { get; init; }

    /// <summary>
    /// 卡尔马比率
    /// </summary>
    public double CalmarRatio { get; init; }

    /// <summary>
    /// 最大回撤 (USDT)
    /// </summary>
    public double MaxDrawdown { get; init; }

    /// <summary>
    /// 最大回撤百分比 (%)
    /// </summary>
    public double MaxDrawdownPercent { get; init; }

    /// <summary>
    /// 胜率 (0.0 - 1.0)
    /// </summary>
    public double WinRate { get; init; }

    /// <summary>
    /// 盈亏比 (Profit Factor)
    /// </summary>
    public double ProfitFactor { get; init; }

    /// <summary>
    /// 平均盈利 (USDT)
    /// </summary>
    public double AvgWin { get; init; }

    /// <summary>
    /// 平均亏损 (USDT)
    /// </summary>
    public double AvgLoss { get; init; }

    /// <summary>
    /// 最大单笔盈利 (USDT)
    /// </summary>
    public double LargestWin { get; init; }

    /// <summary>
    /// 最大单笔亏损 (USDT)
    /// </summary>
    public double LargestLoss { get; init; }

    /// <summary>
    /// 最大连胜次数
    /// </summary>
    public int MaxConsecutiveWins { get; init; }

    /// <summary>
    /// 最大连亏次数
    /// </summary>
    public int MaxConsecutiveLosses { get; init; }

    /// <summary>
    /// 平均持仓时间
    /// </summary>
    public TimeSpan AvgTradeDuration { get; init; }

    /// <summary>
    /// 恢复系数 (净利润/最大回撤)
    /// </summary>
    public double RecoveryFactor { get; init; }

    /// <summary>
    /// 每笔期望收益 (USDT)
    /// </summary>
    public double ExpectancyPerTrade { get; init; }

    /// <summary>
    /// 年化波动率
    /// </summary>
    public double Volatility { get; init; }
}
