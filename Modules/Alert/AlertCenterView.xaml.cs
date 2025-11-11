using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 币安量化机器人.Modules.Alert;

public partial class AlertCenterView : UserControl
{
    private readonly ObservableCollection<PriceAlertItem> _priceAlerts = new();
    private readonly ObservableCollection<PositionAlertItem> _positionAlerts = new();
    private readonly ObservableCollection<NotificationHistoryItem> _allNotifications = new();
    private readonly ObservableCollection<NotificationHistoryItem> _filteredNotifications = new();

    private bool _globalEnabled = true;

    public AlertCenterView()
    {
        InitializeComponent();

        // 绑定数据
        PriceAlertsGrid.ItemsSource = _priceAlerts;
        PositionAlertsList.ItemsSource = _positionAlerts;
        NotificationHistoryGrid.ItemsSource = _filteredNotifications;

        // 初始化
        InitializePositionAlerts();
        GenerateMockData();
        UpdateStatistics();
    }

    private void InitializePositionAlerts()
    {
        _positionAlerts.Clear();

        _positionAlerts.Add(new PositionAlertItem
        {
            Icon = "📉",
            IconBackground = new SolidColorBrush(Color.FromRgb(254, 242, 242)),
            AlertName = "止损预警",
            Description = "持仓亏损达到止损线时发出预警",
            CurrentStatus = "阈值: 5%",
            IsEnabled = true
        });

        _positionAlerts.Add(new PositionAlertItem
        {
            Icon = "📈",
            IconBackground = new SolidColorBrush(Color.FromRgb(236, 253, 245)),
            AlertName = "止盈预警",
            Description = "持仓盈利达到止盈线时发出预警",
            CurrentStatus = "阈值: 10%",
            IsEnabled = true
        });
    }

    private void GenerateMockData()
    {
        _priceAlerts.Add(new PriceAlertItem
        {
            Symbol = "BTCUSDT",
            Condition = "价格突破",
            TriggerPrice = 50000,
            CurrentPrice = 48500,
            Distance = "+3.09%",
            Status = "活跃",
            CreateTime = DateTime.Now.AddHours(-2)
        });

        _allNotifications.Add(new NotificationHistoryItem
        {
            Timestamp = DateTime.Now.AddMinutes(-10),
            NotificationType = "价格预警",
            Level = "信息",
            Title = "BTCUSDT价格变动",
            Message = "当前价格48500,距离触发价50000还有3.09%"
        });

        FilterNotifications();
    }

    private void UpdateStatistics()
    {
        ActiveAlertsText.Text = $"{_priceAlerts.Count(a => a.Status == "活跃")} 个";

        int todayTriggered = _allNotifications.Count(n =>
            n.Timestamp.Date == DateTime.Today &&
            (n.NotificationType == "价格预警" || n.NotificationType == "持仓预警")
        );
        TodayTriggeredText.Text = $"{todayTriggered} 次";

        NotificationMethodsText.Text = "系统通知";
    }

    private void FilterNotifications()
    {
        _filteredNotifications.Clear();

        string filter = "全部";
        if (NotificationTypeFilter.SelectedItem is ComboBoxItem item)
        {
            filter = item.Content.ToString() ?? "全部";
        }

        IEnumerable<NotificationHistoryItem> filtered = filter switch
        {
            "价格预警" => _allNotifications.Where(n => n.NotificationType == "价格预警"),
            "持仓预警" => _allNotifications.Where(n => n.NotificationType == "持仓预警"),
            "系统通知" => _allNotifications.Where(n => n.NotificationType == "系统通知"),
            _ => _allNotifications
        };

        foreach (var notification in filtered.OrderByDescending(n => n.Timestamp))
        {
            _filteredNotifications.Add(notification);
        }
    }

    private void CreateAlert_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("新建预警功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreatePriceAlert_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("新建价格预警功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeletePriceAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is PriceAlertItem alert)
        {
            var result = MessageBox.Show(
                $"确定要删除预警 {alert.Symbol} {alert.Condition} {alert.TriggerPrice} 吗?",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                _priceAlerts.Remove(alert);
                UpdateStatistics();
            }
        }
    }

    private void GlobalEnable_Changed(object sender, RoutedEventArgs e)
    {
        _globalEnabled = GlobalEnableCheckBox.IsChecked == true;

        string status = _globalEnabled ? "已启用" : "已暂停";
        MessageBox.Show($"预警系统{status}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NotificationTypeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        FilterNotifications();
    }

    private void SendTestEmail_Click(object sender, RoutedEventArgs e)
    {
        string email = EmailAddressInput.Text;
        if (string.IsNullOrEmpty(email))
        {
            MessageBox.Show("请输入邮箱地址", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show($"测试邮件已发送到 {email}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SendTestTelegram_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Telegram测试消息已发送", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

public class PriceAlertItem
{
    public string Symbol { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public decimal TriggerPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Distance { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; }
}

public class PositionAlertItem
{
    public string Icon { get; set; } = string.Empty;
    public Brush IconBackground { get; set; } = Brushes.Gray;
    public string AlertName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class NotificationHistoryItem
{
    public DateTime Timestamp { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
