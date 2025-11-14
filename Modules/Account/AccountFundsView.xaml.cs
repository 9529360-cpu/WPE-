using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Account;

public partial class AccountFundsView : UserControl
{
    private readonly TradingAccountManager _accountManager;
    private readonly ObservableCollection<UpgradeRuleItem> _upgradeRules = new();

    public AccountFundsView()
    {
        InitializeComponent();

        // 初始化服务
        DataCacheService cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(cacheService);

        // 如果没有账户,创建默认账户 (初始资金100 USDT)
        if (_accountManager.SimulatedAccount == null)
            _accountManager.CreateSimulatedAccount("模拟账户", 100m);

        // 绑定数据
        UpgradeRulesList.ItemsSource = _upgradeRules;

        // 加载数据
        LoadData();
    }

    /// <summary>
    /// 加载数据
    /// </summary>
    private async void LoadData()
    {
        try
        {
            // 更新模拟账户
            TradingAccount? simAccount = _accountManager.SimulatedAccount;
            if (simAccount != null)
            {
                if (SimNetValueText != null)
                    SimNetValueText.Text = $"{simAccount.NetValue:N2} USDT";
                if (SimAvailableText != null)
                    SimAvailableText.Text = $"{simAccount.AvailableBalance:N2}";
                if (SimPositionValueText != null)
                    SimPositionValueText.Text = $"{simAccount.PositionValue:N2}";

                string sign = simAccount.TotalPnL >= 0 ? "+" : string.Empty;
                if (SimTotalPnLText != null)
                {
                    SimTotalPnLText.Text = $"{sign}{simAccount.TotalPnL:N2}";
                    SimTotalPnLText.Foreground = simAccount.TotalPnL >= 0 ? new SolidColorBrush(Color.FromRgb(34, 197, 94)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }
                if (SimTradesText != null)
                    SimTradesText.Text = $"{simAccount.TotalTrades} 笔";
                if (SimWinRateText != null)
                    SimWinRateText.Text = $"{simAccount.WinRate:P0}";
            }

            // 🔧 尝试加载真实账户数据（从 Binance API）
            await LoadLiveAccountDataAsync();

            // 更新升级规则
            UpdateUpgradeRules();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "加载账户数据失败");
            MessageBox.Show($"加载数据失败:\n\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 🔧 从 Binance API 加载真实账户数据
    /// </summary>
    private async System.Threading.Tasks.Task LoadLiveAccountDataAsync()
    {
        try
        {
            // 检查是否配置了 Binance API 凭证
            string apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY") ?? ConfigurationService.GetValue("Api:Binance:ApiKey");
            string secretKey = Environment.GetEnvironmentVariable("BINANCE_SECRET_KEY") ?? ConfigurationService.GetValue("Api:Binance:SecretKey");

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                SetLiveAccountInactiveState("请先在 [API 管理] 中配置 Binance API Key");
                return;
            }

            // 调用 Binance API 获取账户余额
            LogService.Info("[AccountFundsView] 正在从 Binance API 加载真实账户数据...");

            BinanceApiClient binanceClient = ServiceLocator.Api;
            IReadOnlyList<AccountBalance> balances = await binanceClient.GetAccountBalancesAsync();
            AccountBalance? usdtBalance = balances.FirstOrDefault(b => b.Asset == "USDT");

            if (usdtBalance != null)
            {
                decimal netValue = usdtBalance.WalletBalance;
                decimal available = usdtBalance.AvailableBalance;
                decimal positionValue = netValue - available;

                LogService.Info("[AccountFundsView] Binance 真实账户数据: 净值={NetValue}, 可用={Available}, 持仓={Position}", netValue, available, positionValue);
                if (LiveNetValueText != null)
                    LiveNetValueText.Text = $"{netValue:N2} USDT";
                if (LiveAvailableText != null)
                    LiveAvailableText.Text = $"{available:N2}";
                if (LivePositionValueText != null)
                    LivePositionValueText.Text = $"{positionValue:N2}";
                if (LiveAccountStatusBadge != null && LiveAccountStatusBadge.Child is TextBlock statusText)
                {
                    statusText.Text = "已激活";
                    LiveAccountStatusBadge.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                }
                if (UseLiveRadio != null)
                    UseLiveRadio.IsEnabled = true;
                if (LiveAccountHint != null)
                {
                    LiveAccountHint.Text = "真实账户交易,请谨慎操作";
                    LiveAccountHint.Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128));
                }
                if (_accountManager.LiveAccount == null && netValue > 0)
                {
                    _accountManager.CreateLiveAccount("真实账户", netValue);
                    LogService.Info("[AccountFundsView] 已自动创建真实账户，初始资金: {InitialBalance}", netValue);
                }
            }
            else
            {
                SetLiveAccountWarningState("请先向 Binance 合约账户充值 USDT", "余额为0", Color.FromRgb(251, 191, 36));
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AccountFundsView] 加载 Binance 真实账户数据失败");
            SetLiveAccountErrorState($"加载失败: {ex.Message}");
        }
    }

    private void SetLiveAccountInactiveState(string hint)
    {
        if (LiveNetValueText != null)
            LiveNetValueText.Text = "0.00 USDT";
        if (LiveAvailableText != null)
            LiveAvailableText.Text = "0.00";
        if (LivePositionValueText != null)
            LivePositionValueText.Text = "0.00";
        if (LiveAccountStatusBadge != null && LiveAccountStatusBadge.Child is TextBlock statusText)
        {
            statusText.Text = "未激活";
            LiveAccountStatusBadge.Background = new SolidColorBrush(Color.FromRgb(156, 163, 175));
        }
        if (UseLiveRadio != null)
            UseLiveRadio.IsEnabled = false;
        if (LiveAccountHint != null)
        {
            LiveAccountHint.Text = hint;
            LiveAccountHint.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
    }

    private void SetLiveAccountWarningState(string hint, string badgeText, Color badgeColor)
    {
        if (LiveNetValueText != null)
            LiveNetValueText.Text = "0.00 USDT";
        if (LiveAvailableText != null)
            LiveAvailableText.Text = "0.00";
        if (LivePositionValueText != null)
            LivePositionValueText.Text = "0.00";
        if (LiveAccountStatusBadge != null && LiveAccountStatusBadge.Child is TextBlock statusText)
        {
            statusText.Text = badgeText;
            LiveAccountStatusBadge.Background = new SolidColorBrush(badgeColor);
        }
        if (LiveAccountHint != null)
        {
            LiveAccountHint.Text = hint;
            LiveAccountHint.Foreground = new SolidColorBrush(badgeColor);
        }
    }

    private void SetLiveAccountErrorState(string hint)
    {
        if (LiveAccountStatusBadge != null && LiveAccountStatusBadge.Child is TextBlock statusText)
        {
            statusText.Text = "加载失败";
            LiveAccountStatusBadge.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
        if (LiveAccountHint != null)
        {
            LiveAccountHint.Text = hint;
            LiveAccountHint.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
    }

    /// <summary>
    /// 更新升级规则
    /// </summary>
    private void UpdateUpgradeRules()
    {
        _upgradeRules.Clear();

        AccountUpgradeResult upgradeResult = _accountManager.CanUpgradeToLive();

        foreach (UpgradeCheckItem check in upgradeResult.CheckItems)
        {
            _upgradeRules.Add(new UpgradeRuleItem
            {
                Icon = check.Passed ? "✅" : "❌",
                RuleName = check.Name,
                Description = check.Message,
                CurrentValue = $"当前: {check.Value}",
                StatusText = check.Passed ? "已达标" : "未达标",
                StatusColor = new SolidColorBrush(check.Passed ? Color.FromRgb(16, 185, 129) : Color.FromRgb(239, 68, 68))
            });
        }

        // 更新升级按钮状态
        UpgradeButton.IsEnabled = upgradeResult.CanUpgrade;
    }

    /// <summary>
    /// 刷新
    /// </summary>
    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadData();

    /// <summary>
    /// 检查升级条件
    /// </summary>
    private void CheckUpgrade_Click(object sender, RoutedEventArgs e)
    {
        UpdateUpgradeRules();

        AccountUpgradeResult upgradeResult = _accountManager.CanUpgradeToLive();

        string message = upgradeResult.CanUpgrade ? "🎉 恭喜!您已满足所有升级条件,可以升级到真实账户!" : $"❌ 暂不满足升级条件\n\n{upgradeResult.GetReport()}";
        MessageBox.Show(message, "升级检查", MessageBoxButton.OK, upgradeResult.CanUpgrade ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>
    /// 升级到真实账户
    /// </summary>
    private void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show("确定要升级到真实账户吗?\n\n升级后将可以使用真实资金进行交易。", "确认升级", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;
        var dialog = new InputDialog("请输入真实账户初始资金 (USDT):", "5000");
        if (dialog.ShowDialog() == true)
        {
            if (decimal.TryParse(dialog.InputValue, out decimal initialBalance))
            {
                (bool Success, string Message, TradingAccount? Account) upgrade = _accountManager.UpgradeToLive(initialBalance);

                if (upgrade.Success)
                {
                    MessageBox.Show("✅ 升级成功!", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadData();
                }
                else
                {
                    MessageBox.Show($"❌ 升级失败: {upgrade.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("请输入有效的金额!", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>
    /// 账户类型切换
    /// </summary>
    private void AccountType_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            // 🔧 添加空检查
            if (UseSimulatedRadio == null || UseLiveRadio == null)
            {
                LogService.Warning("[AccountFundsView] 单选按钮未初始化");
                return;
            }

            if (UseSimulatedRadio.IsChecked == true)
            {
                _accountManager.SwitchAccount(AccountType.Simulated);

                // 更新边框样式 (添加空检查)
                if (SimulatedAccountCard != null)
                {
                    SimulatedAccountCard.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    SimulatedAccountCard.BorderThickness = new Thickness(2);
                }
                if (LiveAccountCard != null)
                {
                    LiveAccountCard.BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235));
                    LiveAccountCard.BorderThickness = new Thickness(1);
                }
            }
            else if (UseLiveRadio.IsChecked == true)
            {
                // 检查是否有真实账户
                if (_accountManager.LiveAccount == null)
                {
                    MessageBox.Show("您还没有真实账户!\n\n请先满足升级条件,然后升级到真实账户。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    UseSimulatedRadio.IsChecked = true;
                    return;
                }
                _accountManager.SwitchAccount(AccountType.Live);

                // 更新边框样式 (添加空检查)
                if (SimulatedAccountCard != null)
                {
                    SimulatedAccountCard.BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235));
                    SimulatedAccountCard.BorderThickness = new Thickness(1);
                }
                if (LiveAccountCard != null)
                {
                    LiveAccountCard.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    LiveAccountCard.BorderThickness = new Thickness(2);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "账户切换失败");
            MessageBox.Show($"账户切换失败:\n\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

/// <summary>
/// 升级规则项 (用于UI绑定)
/// </summary>
public class UpgradeRuleItem
{
    public string Icon { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Brush StatusColor { get; set; } = Brushes.Gray;
}

/// <summary>
/// 简单输入对话框
/// </summary>
public class InputDialog : Window
{
    public string InputValue { get; private set; } = string.Empty;
    public InputDialog(string prompt, string defaultValue)
    {
        Title = "输入";
        Width = 400;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var promptText = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(promptText, 0);
        var textBox = new TextBox { Text = defaultValue, Height = 32, FontSize = 14 };
        Grid.SetRow(textBox, 1);
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var okButton = new Button { Content = "确定", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        okButton.Click += (_, __) => { InputValue = textBox.Text; DialogResult = true; };
        var cancelButton = new Button { Content = "取消", Width = 80, Height = 32 };
        cancelButton.Click += (_, __) => { DialogResult = false; };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(promptText);
        grid.Children.Add(textBox);
        grid.Children.Add(buttonPanel);
        Content = grid;
    }
}
