using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 绩效分析引擎 - 计算交易绩效指标
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. 计算收益率（日/周/月/年/总）
/// 2. 计算夏普比率、索提诺比率
/// 3. 计算最大回撤、胜率、盈亏比
/// 4. 生成净值曲线
/// 5. 交易统计分析
/// </remarks>
public sealed class PerformanceAnalyzer
{
    private readonly TradingAccountManager _accountManager;
    private readonly DataCacheService _cacheService;

    public PerformanceAnalyzer(TradingAccountManager accountManager, DataCacheService cacheService)
    {
        _accountManager = accountManager;
        _cacheService = cacheService;
    }

    /// <summary>
    /// 生成完整的绩效报告
    /// </summary>
    public async Task<PerformanceReport> GenerateReportAsync(AccountType accountType = AccountType.Simulated)
    {
        var account = accountType == AccountType.Simulated
            ? _accountManager.SimulatedAccount
            : _accountManager.LiveAccount;

        if (account == null)
        {
            throw new InvalidOperationException($"账户 {accountType} 不存在");
        }

        // 1. 基本统计
        int totalTrades = account.TotalTrades;
        int winningTrades = account.WinningTrades;
        int losingTrades = account.LosingTrades;
        double winRate = account.WinRate;

        // 2. 收益率计算
        double totalReturn = account.TotalReturnPercent;
        double dailyReturn = await CalculateDailyReturnAsync(account);
        double weeklyReturn = await CalculateWeeklyReturnAsync(account);
        double monthlyReturn = await CalculateMonthlyReturnAsync(account);
        double annualizedReturn = CalculateAnnualizedReturn(account);

        // 3. 风险指标
        double maxDrawdown = account.MaxDrawdown;
        double sharpeRatio = await CalculateSharpeRatioAsync(account);
        double sortinoRatio = await CalculateSortinoRatioAsync(account);
        double calmarRatio = annualizedReturn / (Math.Abs(maxDrawdown) == 0 ? 1 : Math.Abs(maxDrawdown));

        // 4. 交易质量
        decimal avgWin = account.OrderHistory.Where(o => o.RealizedPnL > 0).Any()
            ? (decimal)account.OrderHistory.Where(o => o.RealizedPnL > 0).Average(o => o.RealizedPnL)
            : 0;
        decimal avgLoss = account.OrderHistory.Where(o => o.RealizedPnL < 0).Any()
            ? (decimal)account.OrderHistory.Where(o => o.RealizedPnL < 0).Average(o => Math.Abs(o.RealizedPnL))
            : 0;
        double profitFactor = account.ProfitFactor;
        double expectancy = CalculateExpectancy(winRate, avgWin, avgLoss);

        // 5. 净值曲线
        var equityCurve = await GenerateEquityCurveAsync(account);

        return new PerformanceReport
        {
            AccountType = accountType,
            AccountName = account.Name,

            // 基本信息
            InitialBalance = account.InitialBalance,
            CurrentBalance = account.AvailableBalance,
            NetValue = account.NetValue,
            PositionValue = account.PositionValue,

            // 收益率
            TotalReturn = totalReturn,
            DailyReturn = dailyReturn,
            WeeklyReturn = weeklyReturn,
            MonthlyReturn = monthlyReturn,
            AnnualizedReturn = annualizedReturn,

            // 交易统计
            TotalTrades = totalTrades,
            WinningTrades = winningTrades,
            LosingTrades = losingTrades,
            WinRate = winRate,
            AverageWin = avgWin,
            AverageLoss = avgLoss,
            ProfitFactor = profitFactor,
            Expectancy = expectancy,

            // 风险指标
            MaxDrawdown = maxDrawdown,
            SharpeRatio = sharpeRatio,
            SortinoRatio = sortinoRatio,
            CalmarRatio = calmarRatio,

            // 净值曲线
            EquityCurve = equityCurve,

            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 计算日收益率
    /// </summary>
    private async Task<double> CalculateDailyReturnAsync(TradingAccount account)
    {
        var today = DateTime.Today;
        decimal yesterdayNetValue = await GetNetValueAtDateAsync(account, today.AddDays(-1));

        if (yesterdayNetValue == 0)
        {
            return 0;
        }

        return (double)(account.NetValue - yesterdayNetValue) / (double)yesterdayNetValue;
    }

    /// <summary>
    /// 计算周收益率
    /// </summary>
    private async Task<double> CalculateWeeklyReturnAsync(TradingAccount account)
    {
        var lastWeek = DateTime.Today.AddDays(-7);
        decimal lastWeekNetValue = await GetNetValueAtDateAsync(account, lastWeek);

        if (lastWeekNetValue == 0)
        {
            return 0;
        }

        return (double)(account.NetValue - lastWeekNetValue) / (double)lastWeekNetValue;
    }

    /// <summary>
    /// 计算月收益率
    /// </summary>
    private async Task<double> CalculateMonthlyReturnAsync(TradingAccount account)
    {
        var lastMonth = DateTime.Today.AddDays(-30);
        decimal lastMonthNetValue = await GetNetValueAtDateAsync(account, lastMonth);

        if (lastMonthNetValue == 0)
        {
            return 0;
        }

        return (double)(account.NetValue - lastMonthNetValue) / (double)lastMonthNetValue;
    }

    /// <summary>
    /// 计算年化收益率
    /// </summary>
    private double CalculateAnnualizedReturn(TradingAccount account)
    {
        double daysSinceStart = (DateTime.UtcNow - account.CreatedAt).TotalDays;
        if (daysSinceStart < 1)
        {
            return 0;
        }

        double totalReturn = account.TotalReturnPercent;
        return totalReturn * (365.0 / daysSinceStart);
    }

    /// <summary>
    /// 计算夏普比率
    /// </summary>
    private async Task<double> CalculateSharpeRatioAsync(TradingAccount account)
    {
        var returns = await GetDailyReturnsAsync(account);
        if (returns.Count < 2)
        {
            return 0;
        }

        double avgReturn = returns.Average();
        double stdDev = Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());

        if (stdDev == 0)
        {
            return 0;
        }

        const double riskFreeRate = 0.03 / 365; // 假设无风险利率 3% 年化
        return (avgReturn - riskFreeRate) / stdDev * Math.Sqrt(365);
    }

    /// <summary>
    /// 计算索提诺比率 (只考虑下行风险)
    /// </summary>
    private async Task<double> CalculateSortinoRatioAsync(TradingAccount account)
    {
        var returns = await GetDailyReturnsAsync(account);
        if (returns.Count < 2)
        {
            return 0;
        }

        double avgReturn = returns.Average();
        var negativeReturns = returns.Where(r => r < 0).ToList();

        if (negativeReturns.Count == 0)
        {
            return double.PositiveInfinity;
        }

        double downSideDeviation = Math.Sqrt(negativeReturns.Select(r => Math.Pow(r, 2)).Average());

        if (downSideDeviation == 0)
        {
            return 0;
        }

        const double riskFreeRate = 0.03 / 365;
        return (avgReturn - riskFreeRate) / downSideDeviation * Math.Sqrt(365);
    }

    /// <summary>
    /// 计算期望值
    /// </summary>
    private double CalculateExpectancy(double winRate, decimal avgWin, decimal avgLoss)
    {
        return winRate * (double)avgWin - (1 - winRate) * (double)Math.Abs(avgLoss);
    }

    /// <summary>
    /// 生成净值曲线
    /// </summary>
    private async Task<List<EquityPoint>> GenerateEquityCurveAsync(TradingAccount account)
    {
        // 从数据库加载历史净值数据
        // TODO: 实现从数据库加载

        // 暂时返回模拟数据
        var curve = new List<EquityPoint>();
        var startDate = account.CreatedAt;
        var currentDate = DateTime.UtcNow;
        int days = (currentDate - startDate).Days;

        if (days < 1)
        {
            days = 1;
        }

        double initialBalance = (double)account.InitialBalance;
        double currentNetValue = (double)account.NetValue;
        double dailyChange = (currentNetValue - initialBalance) / days;

        for (int i = 0; i <= days; i++)
        {
            var date = startDate.AddDays(i);
            double netValue = initialBalance + dailyChange * i;

            curve.Add(new EquityPoint
            {
                Date = date,
                NetValue = netValue,
                Balance = netValue * 0.8, // 假设 80% 是可用余额
                PositionValue = netValue * 0.2 // 假设 20% 是持仓
            });
        }

        return await Task.FromResult(curve);
    }

    /// <summary>
    /// 获取指定日期的净值
    /// </summary>
    private async Task<decimal> GetNetValueAtDateAsync(TradingAccount account, DateTime date)
    {
        // TODO: 从数据库查询历史净值
        // 暂时返回初始余额
        return await Task.FromResult(account.InitialBalance);
    }

    /// <summary>
    /// 获取每日收益率序列
    /// </summary>
    private async Task<List<double>> GetDailyReturnsAsync(TradingAccount account)
    {
        // TODO: 从数据库查询每日收益率
        // 暂时返回模拟数据
        var returns = new List<double>();
        var random = new Random(42);

        for (int i = 0; i < 30; i++)
        {
            returns.Add((random.NextDouble() - 0.5) * 0.02); // -1% 到 +1%
        }

        return await Task.FromResult(returns);
    }
}

/// <summary>
/// 绩效报告
/// </summary>
public class PerformanceReport
{
    public AccountType AccountType { get; set; }
    public string AccountName { get; set; } = string.Empty;

    // 基本信息
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal NetValue { get; set; }
    public decimal PositionValue { get; set; }

    // 收益率
    public double TotalReturn { get; set; }
    public double DailyReturn { get; set; }
    public double WeeklyReturn { get; set; }
    public double MonthlyReturn { get; set; }
    public double AnnualizedReturn { get; set; }

    // 交易统计
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public double WinRate { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public double ProfitFactor { get; set; }
    public double Expectancy { get; set; }

    // 风险指标
    public double MaxDrawdown { get; set; }
    public double SharpeRatio { get; set; }
    public double SortinoRatio { get; set; }
    public double CalmarRatio { get; set; }

    // 净值曲线
    public List<EquityPoint> EquityCurve { get; set; } = new();

    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// 净值曲线点
/// </summary>
public class EquityPoint
{
    public DateTime Date { get; set; }
    public double NetValue { get; set; }
    public double Balance { get; set; }
    public double PositionValue { get; set; }
}
