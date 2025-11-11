using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Signal;

/// <summary>
/// 交易信号可视化视图
/// 实时展示 AI 生成的交易信号
/// </summary>
public partial class SignalVisualizationView : UserControl
{
    private readonly ObservableCollection<SignalRow> _signals = new();
    private readonly SignalBroadcaster _broadcaster = SignalBroadcaster.Instance;
    private const string SubscriberId = "SignalVisualizationView";

    // 统计数据
    private int _todaySignalsCount = 0;
    private readonly int _executedSignalsCount = 0;
    private double _totalConfidence = 0;

    public SignalVisualizationView()
    {
        InitializeComponent();
        SignalsGrid.ItemsSource = _signals;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // 订阅信号广播
        _broadcaster.Subscribe(SubscriberId, OnSignalReceived);

        // 加载历史信号
        LoadHistoricalSignals();

        LogService.Info("[SignalVisualizationView] 视图已加载,开始监听交易信号");
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        // 取消订阅
        _broadcaster.Unsubscribe(SubscriberId);
        LogService.Info("[SignalVisualizationView] 视图已卸载,停止监听交易信号");
    }

    /// <summary>
    /// 接收新信号
    /// </summary>
    private void OnSignalReceived(TradingSignalEvent signal)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                // 添加到列表顶部
                var signalRow = new SignalRow
                {
                    SignalId = signal.SignalId,
                    Timestamp = signal.Timestamp,
                    Symbol = signal.Symbol,
                    Action = signal.Action,
                    Confidence = signal.Confidence,
                    EntryPrice = signal.EntryPrice,
                    TargetPrice = signal.TargetPrice,
                    StopLoss = signal.StopLoss,
                    PositionSize = signal.PositionSize,
                    Timeframe = signal.Timeframe,
                    RiskLevel = signal.RiskLevel,
                    Reason = signal.Reason,
                    TechnicalIndicators = signal.TechnicalIndicators,
                    RiskReward = signal.RiskRewardRatio
                };

                _signals.Insert(0, signalRow);

                // 只保留最近 200 条
                while (_signals.Count > 200)
                {
                    _signals.RemoveAt(_signals.Count - 1);
                }

                // 更新统计
                UpdateStatistics();

                StatusText.Text = $"最新信号: {signal.Symbol} {signal.Action} (置信度 {signal.Confidence:P0})";
                LogService.Info($"[SignalVisualizationView] 收到新信号: {signal.Symbol} {signal.Action}");
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[SignalVisualizationView] 处理信号失败");
            }
        });
    }

    /// <summary>
    /// 加载历史信号
    /// </summary>
    private void LoadHistoricalSignals()
    {
        try
        {
            var historicalSignals = _broadcaster.GetRecentSignals(100);

            foreach (var signal in historicalSignals)
            {
                var signalRow = new SignalRow
                {
                    SignalId = signal.SignalId,
                    Timestamp = signal.Timestamp,
                    Symbol = signal.Symbol,
                    Action = signal.Action,
                    Confidence = signal.Confidence,
                    EntryPrice = signal.EntryPrice,
                    TargetPrice = signal.TargetPrice,
                    StopLoss = signal.StopLoss,
                    PositionSize = signal.PositionSize,
                    Timeframe = signal.Timeframe,
                    RiskLevel = signal.RiskLevel,
                    Reason = signal.Reason,
                    TechnicalIndicators = signal.TechnicalIndicators,
                    RiskReward = signal.RiskRewardRatio
                };
                _signals.Add(signalRow);
            }

            UpdateStatistics();
            StatusText.Text = $"已加载 {historicalSignals.Count} 条历史信号";
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[SignalVisualizationView] 加载历史信号失败");
        }
    }

    /// <summary>
    /// 更新统计数据
    /// </summary>
    private void UpdateStatistics()
    {
        var today = DateTime.Today;
        var todaySignals = _signals.Where(s => s.Timestamp.Date == today).ToList();

        _todaySignalsCount = todaySignals.Count;
        _totalConfidence = todaySignals.Any() ? todaySignals.Average(s => s.Confidence) : 0;

        TodaySignalsText.Text = _todaySignalsCount.ToString();
        ExecutionRateText.Text = _todaySignalsCount > 0
            ? $"{(_executedSignalsCount * 100.0 / _todaySignalsCount):F0}%"
            : "0%";
        AvgConfidenceText.Text = $"{_totalConfidence:P0}";
        SignalCountText.Text = $"({_signals.Count} 条)";
    }

    /// <summary>
    /// 信号选择变化
    /// </summary>
    private void SignalsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SignalsGrid.SelectedItem is SignalRow signal)
        {
            UpdateDetailPanel(signal);
        }
    }

    /// <summary>
    /// 更新详情面板
    /// </summary>
    private void UpdateDetailPanel(SignalRow signal)
    {
        DetailSymbol.Text = $"交易对: {signal.Symbol}";
        DetailAction.Text = $"动作: {signal.Action}";
        DetailTimeframe.Text = $"时间框架: {signal.Timeframe}";
        DetailRiskLevel.Text = $"风险等级: {signal.RiskLevel}";

        DetailEntry.Text = $"入场价: {signal.EntryPrice:F4}";
        DetailTarget.Text = $"目标价: {signal.TargetPrice:F4} ({signal.ProfitPotential:P2})";
        DetailStopLoss.Text = $"止损价: {signal.StopLoss:F4}";
        DetailPositionSize.Text = $"建议仓位: {signal.PositionSize:P0}";

        DetailReason.Text = signal.Reason;

        // 显示技术指标
        if (signal.TechnicalIndicators.Any())
        {
            string indicators = string.Join("\n", signal.TechnicalIndicators.Select(kv => $"{kv.Key}: {kv.Value:F2}"));
            DetailIndicators.Text = indicators;
        }
        else
        {
            DetailIndicators.Text = "暂无技术指标数据";
        }
    }

    /// <summary>
    /// 筛选按钮
    /// </summary>
    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 实现筛选对话框
        MessageBox.Show("筛选功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 清空按钮
    /// </summary>
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要清空所有信号记录吗?",
            "确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            _signals.Clear();
            _broadcaster.ClearHistory();
            UpdateStatistics();
            StatusText.Text = "信号记录已清空";
            LogService.Info("[SignalVisualizationView] 信号记录已清空");
        }
    }

    /// <summary>
    /// 刷新按钮
    /// </summary>
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _signals.Clear();
        LoadHistoricalSignals();
        StatusText.Text = "数据已刷新";
    }
}

/// <summary>
/// 信号行数据
/// </summary>
public class SignalRow
{
    public string SignalId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public double EntryPrice { get; set; }
    public double TargetPrice { get; set; }
    public double StopLoss { get; set; }
    public double PositionSize { get; set; }
    public string Timeframe { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public System.Collections.Generic.Dictionary<string, double> TechnicalIndicators { get; set; } = new();
    public double RiskReward { get; set; }

    // UI 绑定属性
    public string DisplayTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string ConfidenceText => $"{Confidence:P0}";
    public int ConfidencePercent => (int)(Confidence * 100);
    public string EntryPriceText => $"{EntryPrice:F4}";
    public string TargetPriceText => $"{TargetPrice:F4}";
    public string StopLossText => $"{StopLoss:F4}";
    public string RiskRewardText => $"{RiskReward:F1}";

    public double ProfitPotential => TargetPrice > 0 && EntryPrice > 0
        ? (TargetPrice - EntryPrice) / EntryPrice
        : 0;

    public Brush ActionColor => Action.ToUpperInvariant() switch
    {
        "BUY" => new SolidColorBrush(Color.FromRgb(16, 185, 129)),  // 绿色
        "SELL" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),  // 红色
        "HOLD" => new SolidColorBrush(Color.FromRgb(107, 114, 128)), // 灰色
        _ => new SolidColorBrush(Color.FromRgb(59, 130, 246))        // 蓝色
    };

    public Brush ConfidenceColor
    {
        get
        {
            if (Confidence >= 0.8)
            {
                return new SolidColorBrush(Color.FromRgb(16, 185, 129));  // 高置信度 - 绿色
            }

            if (Confidence >= 0.6)
            {
                return new SolidColorBrush(Color.FromRgb(59, 130, 246));  // 中置信度 - 蓝色
            }

            if (Confidence >= 0.4)
            {
                return new SolidColorBrush(Color.FromRgb(245, 158, 11));  // 中低置信度 - 黄色
            }

            return new SolidColorBrush(Color.FromRgb(239, 68, 68));                           // 低置信度 - 红色
        }
    }
}
