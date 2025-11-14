using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Strategy;

public partial class StrategyPortfolioView : UserControl
{
    private readonly StrategyPortfolioManager _portfolioManager;
    private readonly TradingAccountManager _account_manager;
    private readonly DataCacheService _cacheService;

    public StrategyPortfolioView()
    {
        InitializeComponent();

        _cacheService = ServiceLocator.Cache;
        _account_manager = new TradingAccountManager(_cacheService);
        _portfolioManager = ServiceLocator.StrategyPortfolio; // 使用全局实例，包含磁盘数据

        // use global DialogService
        IDialogService dialog = ServiceLocator.DialogService;
        // set viewmodel as datacontext with injected dialog service
        this.DataContext = new StrategyPortfolioViewModel(_portfolioManager, dialog);

        // 订阅全局账户类型变更（来自 RuntimeState）
        RuntimeState.OnAccountTypeChanged += (acct) => Dispatcher.Invoke(() => AccountModeComboBox.SelectedIndex = acct == AccountType.Live ? 1 : 0);

        // 初始化 AccountModeComboBox 默认值
        if (AccountModeComboBox != null)
        {
            AccountModeComboBox.SelectedIndex = RuntimeState.CurrentAccountType == AccountType.Live ? 1 : 0;
        }
    }

    private void AccountModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountModeComboBox == null)
        {
            return;
        }
        var idx = AccountModeComboBox.SelectedIndex;
        RuntimeState.CurrentAccountType = idx == 1 ? AccountType.Live : AccountType.Simulated;
    }
}
