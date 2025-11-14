using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using 币安量化机器人.Services;

namespace 币安量化机器人.Models;

/// <summary>
/// 交易账户 - 统一的账户模型 (支持模拟账户和真实账户)
/// </summary>
/// <remarks>
/// 核心设计:
/// 1. 模拟账户: 本地模拟成交,不调用真实API
/// 2. 真实账户: 调用Binance API真实下单
/// 3. 双账户隔离: 数据完全分离,不会混淆
/// 4. 升级机制: 模拟账户达标后可升级到真实账户
/// </remarks>
public class TradingAccount : INotifyPropertyChanged
{
    private decimal _currentBalance;
    private decimal _availableBalance;
    private decimal _lockedBalance;
    private decimal _totalPnL;
    private decimal _todayPnL;
    private DateTime? _lastTradeAt;

    public TradingAccount(AccountType type, string name, decimal initialBalance)
    {
        Type = type;
        Name = name;
        InitialBalance = initialBalance;
        CurrentBalance = initialBalance;
        AvailableBalance = initialBalance;
        LockedBalance = 0;
        CreatedAt = DateTime.UtcNow;

        Positions = new List<Position>();
        OrderHistory = new List<Order>();
    }

    #region 账户基本信息

    /// <summary>
    /// 账户ID
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 账户类型 (模拟/真实)
    /// </summary>
    public AccountType Type { get; init; }

    /// <summary>
    /// 账户名称
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// 初始资金
    /// </summary>
    public decimal InitialBalance { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; }

    #endregion

    #region 账户资金

    /// <summary>
    /// 当前余额 (包含持仓市值)
    /// </summary>
    public decimal CurrentBalance
    {
        get => _currentBalance;
        set => SetField(ref _currentBalance, value);
    }

    /// <summary>
    /// 可用余额 (可用于开仓)
    /// </summary>
    public decimal AvailableBalance
    {
        get => _availableBalance;
        set => SetField(ref _availableBalance, value);
    }

    /// <summary>
    /// 冻结资金 (订单占用)
    /// </summary>
    public decimal LockedBalance
    {
        get => _lockedBalance;
        set => SetField(ref _lockedBalance, value);
    }

    /// <summary>
    /// 持仓市值
    /// </summary>
    public decimal PositionValue => Positions
        .Where(p => p.Status == PositionStatus.Open)
        .Sum(p => (decimal)(p.Quantity * p.CurrentPrice));

    /// <summary>
    /// 账户净值 (可用余额 + 持仓市值)
    /// </summary>
    public decimal NetValue => AvailableBalance + PositionValue;

    #endregion

    #region 盈亏统计

    /// <summary>
    /// 总盈亏
    /// </summary>
    public decimal TotalPnL
    {
        get => _totalPnL;
        set => SetField(ref _totalPnL, value);
    }

    /// <summary>
    /// 当日盈亏
    /// </summary>
    public decimal TodayPnL
    {
        get => _todayPnL;
        set => SetField(ref _todayPnL, value);
    }

    /// <summary>
    /// 总收益率
    /// </summary>
    public double TotalReturnPercent => InitialBalance == 0 ? 0 : (double)(TotalPnL / InitialBalance);

    /// <summary>
    /// 当日收益率
    /// </summary>
    public double TodayReturnPercent => InitialBalance == 0 ? 0 : (double)(TodayPnL / InitialBalance);

    #endregion

    #region 交易统计

    /// <summary>
    /// 总交易次数
    /// </summary>
    public int TotalTrades { get; set; }

    /// <summary>
    /// 盈利次数
    /// </summary>
    public int WinningTrades { get; set; }

    /// <summary>
    /// 亏损次数
    /// </summary>
    public int LosingTrades { get; set; }

    /// <summary>
    /// 胜率
    /// </summary>
    public double WinRate => TotalTrades > 0 ? (double)WinningTrades / TotalTrades : 0;

    /// <summary>
    /// 盈亏比
    /// </summary>
    public double ProfitFactor
    {
        get
        {
            double wins = OrderHistory.Where(o => o.RealizedPnL > 0).Sum(o => o.RealizedPnL);
            double losses = Math.Abs(OrderHistory.Where(o => o.RealizedPnL < 0).Sum(o => o.RealizedPnL));
            return losses == 0 ? 0 : wins / losses;
        }
    }

    /// <summary>
    /// 最后交易时间
    /// </summary>
    public DateTime? LastTradeAt
    {
        get => _lastTradeAt;
        set => SetField(ref _lastTradeAt, value);
    }

    #endregion

    #region 持仓和订单

    /// <summary>
    /// 持仓列表
    /// </summary>
    public List<Position> Positions { get; }

    /// <summary>
    /// 订单历史
    /// </summary>
    public List<Order> OrderHistory { get; }

    /// <summary>
    /// 当前持仓数量
    /// </summary>
    public int OpenPositionCount => Positions.Count(p => p.Status == PositionStatus.Open);

    #endregion

    #region 风险指标

    /// <summary>
    /// 最大回撤
    /// </summary>
    public double MaxDrawdown { get; set; }

    /// <summary>
    /// 夏普比率
    /// </summary>
    public double SharpeRatio { get; set; }

    #endregion

    #region 方法

    /// <summary>
    /// 重置当日盈亏 (每日UTC 00:00调用)
    /// </summary>
    public void ResetDailyPnL()
    {
        TodayPnL = 0;
        LogService.Info("账户日盈亏已重置: {AccountName}", Name);
    }

    /// <summary>
    /// 获取账户概览
    /// </summary>
    public string GetSummary()
    {
        return $"""
            { Name} ({ Type})
            ├─ 净值:
        { NetValue: F2}
        USDT
            ├─ 可用:
        { AvailableBalance: F2}
        USDT
            ├─ 持仓:
        { PositionValue: F2}
        USDT({ OpenPositionCount}
        个)
            ├─ 总盈亏:
        { TotalPnL: F2} ({ TotalReturnPercent: P2})
            ├─ 今日盈亏:
        { TodayPnL: F2} ({ TodayReturnPercent: P2})
            ├─ 交易次数:
        { TotalTrades} (胜率 { WinRate: P0})
            └─ 盈亏比:
        { ProfitFactor: F2}
        """;
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

/// <summary>
/// 账户类型
/// </summary>
public enum AccountType
{
    /// <summary>
    /// 模拟账户 (Paper Trading) - 本地模拟成交
    /// </summary>
    Simulated,

    /// <summary>
    /// 真实账户 (Live Trading) - 真实API下单
    /// </summary>
    Live
}

/// <summary>
/// 持仓
/// </summary>
public class Position
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Symbol { get; init; } = string.Empty;
    public OrderSide Side { get; init; }
    // Quantity can change on partial closes
    public double Quantity { get; set; }
    public double EntryPrice { get; set; }
    public double CurrentPrice { get; set; }
    public double ClosePrice { get; set; }
    public double StopLoss { get; init; }
    public double TakeProfit { get; init; }
    public double UnrealizedPnL { get; set; }
    public double RealizedPnL { get; set; }
    public double PnLPercent { get; set; }
    public DateTime OpenTime { get; init; }
    public DateTime? CloseTime { get; set; }
    public PositionStatus Status { get; set; }
    public double HoldingHours => (DateTime.UtcNow - OpenTime).TotalHours;
}

/// <summary>
/// 持仓状态
/// </summary>
public enum PositionStatus
{
    Open,
    Closed,
    Liquidated
}
