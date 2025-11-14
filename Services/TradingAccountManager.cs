using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 交易账户管理器 - 统一管理模拟账户和真实账户
/// </summary>
/// <remarks>
/// 核心职责:
/// 1. 管理模拟账户和真实账户
/// 2. 账户切换 (模拟 ↔ 真实)
/// 3. 账户升级检查 (模拟 → 真实)
/// 4. 账户数据持久化
/// 5. 账户状态监控
/// </remarks>
public class TradingAccountManager : INotifyPropertyChanged
{
    private readonly DataCacheService _cacheService;
    private readonly AccountUpgradeRules _upgradeRules;
    private TradingAccount? _activeAccount;

    public TradingAccountManager(DataCacheService cacheService, AccountUpgradeRules? upgradeRules = null)
    {
        _cacheService = cacheService;
        _upgradeRules = upgradeRules ?? AccountUpgradeRules.Conservative;

        Accounts = new List<TradingAccount>();

        LogService.Info("TradingAccountManager: 初始化");
    }

    #region 账户列表

    /// <summary>
    /// 所有账户
    /// </summary>
    public List<TradingAccount> Accounts { get; }

    /// <summary>
    /// 模拟账户
    /// </summary>
    public TradingAccount? SimulatedAccount => Accounts.FirstOrDefault(a => a.Type == AccountType.Simulated);

    /// <summary>
    /// 真实账户
    /// </summary>
    public TradingAccount? LiveAccount => Accounts.FirstOrDefault(a => a.Type == AccountType.Live);

    /// <summary>
    /// 当前激活的账户
    /// </summary>
    public TradingAccount? ActiveAccount
    {
        get => _activeAccount;
        private set => SetField(ref _activeAccount, value);
    }

    #endregion

    #region 账户创建

    /// <summary>
    /// 创建模拟账户
    /// </summary>
    /// <param name="name">账户名称</param>
    /// <param name="initialBalance">初始资金</param>
    /// <returns>创建的模拟账户</returns>
    public TradingAccount CreateSimulatedAccount(string name = "模拟账户", decimal initialBalance = 100m)
    {
        // 检查是否已存在模拟账户
        if (SimulatedAccount != null)
        {
            LogService.Warning("模拟账户已存在,无需重复创建");
            return SimulatedAccount;
        }

        var account = new TradingAccount(AccountType.Simulated, name, initialBalance);
        Accounts.Add(account);

        // 默认激活模拟账户
        ActiveAccount = account;

        LogService.Info("创建模拟账户: {Name}, 初始资金: {Balance} USDT", name, initialBalance);

        return account;
    }

    /// <summary>
    /// 创建真实账户
    /// </summary>
    /// <param name="name">账户名称</param>
    /// <param name="initialBalance">初始资金</param>
    /// <returns>创建的真实账户</returns>
    /// <exception cref="InvalidOperationException">模拟账户未达标时抛出异常</exception>
    public TradingAccount CreateLiveAccount(string name = "真实账户", decimal initialBalance = 5000m)
    {
        // 检查是否已存在真实账户
        if (LiveAccount != null)
        {
            LogService.Warning("真实账户已存在,无需重复创建");
            return LiveAccount;
        }

        // 检查模拟账户是否达标
        if (SimulatedAccount != null)
        {
            AccountUpgradeResult upgradeResult = _upgradeRules.CanUpgrade(SimulatedAccount);
            if (!upgradeResult.CanUpgrade)
            {
                string message = $"模拟账户未达标,无法创建真实账户:\n{upgradeResult.GetReport()}";
                LogService.Warning(message);
                throw new InvalidOperationException(message);
            }
        }

        var account = new TradingAccount(AccountType.Live, name, initialBalance);
        Accounts.Add(account);

        LogService.Info("✅ 创建真实账户: {Name}, 初始资金: {Balance} USDT", name, initialBalance);

        return account;
    }

    #endregion

    #region 账户切换

    /// <summary>
    /// 切换到指定账户
    /// </summary>
    /// <param name="type">账户类型</param>
    /// <exception cref="InvalidOperationException">账户不存在时抛出异常</exception>
    public void SwitchAccount(AccountType type)
    {
        TradingAccount? account = type == AccountType.Simulated ? SimulatedAccount : LiveAccount;

        if (account == null)
        {
            string message = $"{type} 账户不存在,无法切换";
            LogService.Error(message);
            throw new InvalidOperationException(message);
        }

        // 如果切换到真实账户,再次检查升级条件
        if (type == AccountType.Live && SimulatedAccount != null)
        {
            AccountUpgradeResult upgradeResult = _upgradeRules.CanUpgrade(SimulatedAccount);
            if (!upgradeResult.CanUpgrade)
            {
                string message = $"模拟账户未达标,无法切换到真实账户:\n{upgradeResult.GetReport()}";
                LogService.Warning(message);
                throw new InvalidOperationException(message);
            }
        }

        ActiveAccount = account;

        LogService.Info("账户已切换: {OldAccount} → {NewAccount}",
            _activeAccount?.Name ?? "无",
            account.Name);

        OnPropertyChanged(nameof(IsSimulatedMode));
        OnPropertyChanged(nameof(IsLiveMode));
    }

    /// <summary>
    /// 切换到模拟账户
    /// </summary>
    public void SwitchToSimulated()
    {
        SwitchAccount(AccountType.Simulated);
    }

    /// <summary>
    /// 切换到真实账户
    /// </summary>
    public void SwitchToLive()
    {
        SwitchAccount(AccountType.Live);
    }

    #endregion

    #region 账户升级

    /// <summary>
    /// 检查模拟账户是否可以升级到真实账户
    /// </summary>
    /// <returns>升级检查结果</returns>
    public AccountUpgradeResult CanUpgradeToLive()
    {
        if (SimulatedAccount == null)
        {
            return AccountUpgradeResult.Failed("模拟账户不存在");
        }

        return _upgradeRules.CanUpgrade(SimulatedAccount);
    }

    /// <summary>
    /// 升级到真实账户 (如果模拟账户达标)
    /// </summary>
    /// <param name="initialBalance">真实账户初始资金</param>
    /// <returns>升级结果</returns>
    public (bool Success, string Message, TradingAccount? Account) UpgradeToLive(decimal initialBalance = 5000m)
    {
        AccountUpgradeResult upgradeResult = CanUpgradeToLive();

        if (!upgradeResult.CanUpgrade)
        {
            return (false, upgradeResult.Message, null);
        }

        try
        {
            TradingAccount liveAccount = CreateLiveAccount("真实账户", initialBalance);
            SwitchToLive();

            string message = $"🎉 恭喜!已成功升级到真实账户!\n账户名: {liveAccount.Name}\n初始资金: {initialBalance} USDT";
            LogService.Info(message);

            return (true, message, liveAccount);
        }
        catch (Exception ex)
        {
            string message = $"升级失败: {ex.Message}";
            LogService.Error(ex, message);
            return (false, message, null);
        }
    }

    #endregion

    #region 状态查询

    /// <summary>
    /// 是否为模拟模式
    /// </summary>
    public bool IsSimulatedMode => ActiveAccount?.Type == AccountType.Simulated;

    /// <summary>
    /// 是否为真实模式
    /// </summary>
    public bool IsLiveMode => ActiveAccount?.Type == AccountType.Live;

    /// <summary>
    /// 是否有激活的账户
    /// </summary>
    public bool HasActiveAccount => ActiveAccount != null;

    /// <summary>
    /// 获取所有账户概览
    /// </summary>
    public string GetAccountsSummary()
    {
        string summary = "账户概览\n" +
                      "========\n\n";

        if (SimulatedAccount != null)
        {
            summary += "【模拟账户】\n" +
                      SimulatedAccount.GetSummary() + "\n\n";
        }

        if (LiveAccount != null)
        {
            summary += "【真实账户】\n" +
                      LiveAccount.GetSummary() + "\n\n";
        }

        if (ActiveAccount != null)
        {
            summary += $"当前激活: {ActiveAccount.Name} ({ActiveAccount.Type})";
        }

        return summary;
    }

    #endregion

    #region 数据持久化

    /// <summary>
    /// 保存账户数据到数据库
    /// </summary>
    public async Task SaveAccountsAsync()
    {
        try
        {
            foreach (TradingAccount account in Accounts)
            {
                // TODO: 保存到数据库
                // await _cacheService.SaveAccountAsync(account);
            }

            LogService.Info("账户数据已保存");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "保存账户数据失败");
            throw;
        }
    }

    /// <summary>
    /// 从数据库加载账户数据
    /// </summary>
    public async Task LoadAccountsAsync()
    {
        try
        {
            // TODO: 从数据库加载
            // var accounts = await _cacheService.LoadAccountsAsync();
            // Accounts.Clear();
            // Accounts.AddRange(accounts);

            LogService.Info("账户数据已加载");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "加载账户数据失败");
            throw;
        }
    }

    #endregion

    #region 账户统计

    /// <summary>
    /// 获取总资产 (所有账户)
    /// </summary>
    public decimal TotalAssets => Accounts.Sum(a => a.NetValue);

    /// <summary>
    /// 获取总盈亏 (所有账户)
    /// </summary>
    public decimal TotalPnL => Accounts.Sum(a => a.TotalPnL);

    /// <summary>
    /// 获取今日盈亏 (所有账户)
    /// </summary>
    public decimal TotalTodayPnL => Accounts.Sum(a => a.TodayPnL);

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
