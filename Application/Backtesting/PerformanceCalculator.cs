using System;
using System.Collections.Generic;
using System.Linq;

namespace 币安量化机器人.Application.Backtesting;

/// <summary>
/// 绩效计算器 - 计算详细的回测绩效指标
/// </summary>
/// <remarks>
/// <para>计算的关键指标:</para>
/// <list type="bullet">
/// <item><b>收益指标</b>: 净利润、收益率、每笔期望收益</item>
/// <item><b>风险指标</b>: 最大回撤、夏普比率、索提诺比率</item>
/// <item><b>交易质量</b>: 胜率、盈亏比、平均盈/亏</item>
/// <item><b>稳定性</b>: 连胜/连亏次数、恢复系数</item>
/// </list>
/// <para>关键比率说明:</para>
/// <list type="bullet">
/// <item><b>夏普比率</b>: 风险调整后收益,>2优秀,>1良好</item>
/// <item><b>索提诺比率</b>: 下行风险调整后收益,更关注亏损</item>
/// <item><b>卡尔马比率</b>: 年化收益/最大回撤,衡量回撤控制</item>
/// <item><b>盈亏比</b>: 总盈利/总亏损,>2优秀,>1可接受</item>
/// </list>
/// </remarks>
public class PerformanceCalculator
{
    /// <summary>
    /// 绩效计算常量
    /// </summary>
    private static class PerformanceConstants
    {
        /// <summary>
        /// 一年交易日数 (用于年化计算)
        /// </summary>
        public const double ANNUAL_TRADING_DAYS = 252.0;

        /// <summary>
        /// 年化系数 (√252)
        /// </summary>
        public static readonly double ANNUAL_FACTOR = Math.Sqrt(ANNUAL_TRADING_DAYS);

        /// <summary>
        /// 最小样本数 (少于此数计算不可靠)
        /// </summary>
        public const int MIN_SAMPLE_SIZE = 2;
    }

    /// <summary>
    /// 计算完整绩效指标
    /// </summary>
    /// <param name="trades">交易记录列表</param>
    /// <param name="initialCapital">初始资金</param>
    /// <returns>详细绩效指标</returns>
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
                RecoveryFactor = 0
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
            ExpectancyPerTrade = trades.Average(t => t.NetPnL)
        };
    }

    /// <summary>
    /// 计算夏普比率 - 风险调整后收益
    /// </summary>
    /// <param name="returns">收益率序列</param>
    /// <returns>年化夏普比率</returns>
    /// <remarks>
    /// <para>公式: (平均收益率 / 收益率标准差) × √252</para>
    /// <para>评价标准:</para>
    /// <list type="bullet">
    /// <item>&lt;1: 差</item>
    /// <item>1-2: 良好</item>
    /// <item>2-3: 优秀</item>
    /// <item>>3: 卓越</item>
    /// </list>
    /// </remarks>
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

        // 年化夏普比率 (假设每日交易)
        return (avgReturn / stdReturn) * PerformanceConstants.ANNUAL_FACTOR;
    }

    /// <summary>
    /// 计算索提诺比率 - 下行风险调整后收益
    /// </summary>
    /// <param name="returns">收益率序列</param>
    /// <returns>年化索提诺比率</returns>
    /// <remarks>
    /// <para>与夏普比率类似,但只考虑下行波动(亏损)</para>
    /// <para>更适合评估策略的下行风险控制能力</para>
    /// </remarks>
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

    /// <summary>
    /// 计算卡尔马比率 - 年化收益 / 最大回撤
    /// </summary>
    /// <param name="netProfit">净利润</param>
    /// <param name="initialCapital">初始资金</param>
    /// <param name="equity">权益曲线</param>
    /// <returns>卡尔马比率</returns>
    /// <remarks>
    /// <para>衡量策略在控制回撤情况下的收益能力</para>
    /// <para>>3为优秀, 1-3为良好, &lt;1需要改进</para>
    /// </remarks>
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

    /// <summary>
    /// 计算最大回撤 (绝对值)
    /// </summary>
    /// <param name="equity">权益曲线</param>
    /// <returns>最大回撤金额</returns>
    /// <remarks>
    /// 从历史最高点到最低点的最大跌幅
    /// </remarks>
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

    /// <summary>
    /// 计算盈亏比 (Profit Factor)
    /// </summary>
    /// <param name="wins">盈利交易列表</param>
    /// <param name="losses">亏损交易列表</param>
    /// <returns>盈亏比</returns>
    /// <remarks>
    /// <para>公式: 总盈利 / 总亏损</para>
    /// <para>>2优秀, >1可接受, &lt;1策略亏损</para>
    /// </remarks>
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

    /// <summary>
    /// 计算最大连胜次数
    /// </summary>
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

    /// <summary>
    /// 计算最大连亏次数
    /// </summary>
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

    /// <summary>
    /// 计算平均持仓时间
    /// </summary>
    private TimeSpan CalculateAvgTradeDuration(IReadOnlyList<BacktestTrade> trades)
    {
        if (trades.Count == 0)
        {
            return TimeSpan.Zero;
        }

        double avgSeconds = trades.Average(t => (t.ExitTime - t.EntryTime).TotalSeconds);
        return TimeSpan.FromSeconds(avgSeconds);
    }

    /// <summary>
    /// 计算恢复系数 - 净利润 / 最大回撤
    /// </summary>
    /// <param name="netProfit">净利润</param>
    /// <param name="equity">权益曲线</param>
    /// <returns>恢复系数</returns>
    /// <remarks>
    /// 衡量策略从回撤中恢复的能力,越高越好
    /// </remarks>
    private double CalculateRecoveryFactor(double netProfit, List<double> equity)
    {
        double maxDD = CalculateMaxDrawdown(equity);
        if (maxDD == 0)
        {
            return 0;
        }

        return netProfit / maxDD;
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
}
