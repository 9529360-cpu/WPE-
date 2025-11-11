using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot;
using ScottPlot.Plottables;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using WpfColor = System.Windows.Media.Color; // 🔧 添加别名避免冲突

namespace 币安量化机器人.Modules.Performance;

/// <summary>
/// 绩效分析仪表盘
/// 展示完整的交易绩效统计和可视化
/// </summary>
public partial class PerformanceDashboardView : UserControl
{
    private readonly TradingAccountManager _accountManager;
    private readonly PerformanceAnalyzer _performanceAnalyzer;
    private readonly DataCacheService _cacheService;

    private AccountType _currentAccountType = AccountType.Simulated;

    public PerformanceDashboardView()
    {
        InitializeComponent();

        // 初始化服务
        _cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(_cacheService);
        _performanceAnalyzer = new PerformanceAnalyzer(_accountManager, _cacheService);
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadPerformanceDataAsync();
            LogService.Info("[PerformanceDashboardView] 绩效仪表盘已加载");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 加载绩效数据失败");
            MessageBox.Show($"加载绩效数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 加载绩效数据
    /// </summary>
    private async System.Threading.Tasks.Task LoadPerformanceDataAsync()
    {
        try
        {
            // 🔧 添加空检查
            if (StatusText != null)
            {
                StatusText.Text = "正在加载绩效数据...";
            }

            // 🔧 确保账户存在
            if (_accountManager.SimulatedAccount == null)
            {
                LogService.Info("[PerformanceDashboardView] 模拟账户不存在，正在创建...");
                _accountManager.CreateSimulatedAccount("模拟账户", 100m);
            }

            // 🔧 确保有激活账户
            if (!_accountManager.HasActiveAccount)
            {
                LogService.Info("[PerformanceDashboardView] 没有激活账户，正在激活模拟账户...");
                _accountManager.SwitchToSimulated();
            }

            // 生成绩效报告
            PerformanceReport report = await _performanceAnalyzer.GenerateReportAsync(_currentAccountType);

            // 更新UI (添加空检查)
            if (report != null)
            {
                UpdateSummaryCards(report);
                UpdateReturnStatistics(report);
                UpdateTradeStatistics(report);
                UpdateRiskMetrics(report);
                RenderEquityCurve(report);
            }
            else
            {
                LogService.Warning("[PerformanceDashboardView] 绩效报告为空");
                if (StatusText != null)
                {
                    StatusText.Text = "绩效数据不可用";
                }
                return;
            }

            if (StatusText != null)
            {
                StatusText.Text = $"绩效数据已更新 - {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 加载绩效数据异常");
            if (StatusText != null)
            {
                StatusText.Text = $"加载失败: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// 更新汇总卡片
    /// </summary>
    private void UpdateSummaryCards(PerformanceReport report)
    {
        try
        {
            // 总收益率
            if (TotalReturnText != null)
            {
                TotalReturnText.Text = $"{report.TotalReturn:P2}";
                TotalReturnText.Foreground = report.TotalReturn >= 0
                    ? new SolidColorBrush(WpfColor.FromRgb(16, 185, 129))
                    : new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
            }

            if (TotalReturnChange != null)
            {
                TotalReturnChange.Text = $"本月 {report.MonthlyReturn:+0.00%;-0.00%;0.00%}";
                TotalReturnChange.Foreground = report.MonthlyReturn >= 0
                    ? new SolidColorBrush(WpfColor.FromRgb(16, 185, 129))
                    : new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
            }

            // 夏普比率
            if (SharpeRatioText != null)
            {
                SharpeRatioText.Text = $"{report.SharpeRatio:F2}";
                SharpeRatioText.Foreground = GetSharpeRatioColor(report.SharpeRatio);
            }

            if (SharpeRatingText != null)
            {
                SharpeRatingText.Text = GetSharpeRatioRating(report.SharpeRatio);
                SharpeRatingText.Foreground = GetSharpeRatioColor(report.SharpeRatio);
            }

            // 最大回撤
            if (MaxDrawdownText != null)
            {
                MaxDrawdownText.Text = $"{report.MaxDrawdown:P2}";
                MaxDrawdownText.Foreground = GetDrawdownColor(report.MaxDrawdown);
            }

            if (DrawdownStatusText != null)
            {
                DrawdownStatusText.Text = GetDrawdownStatus(report.MaxDrawdown);
                DrawdownStatusText.Foreground = GetDrawdownColor(report.MaxDrawdown);
            }

            // 胜率
            if (WinRateText != null)
            {
                WinRateText.Text = $"{report.WinRate:P0}";
                WinRateText.Foreground = report.WinRate >= 0.5
                    ? new SolidColorBrush(WpfColor.FromRgb(16, 185, 129))
                    : new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
            }

            if (WinRateStatusText != null)
            {
                WinRateStatusText.Text = $"{report.WinningTrades}/{report.TotalTrades} 笔";
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 更新汇总卡片失败");
        }
    }

    /// <summary>
    /// 更新收益率统计
    /// </summary>
    private void UpdateReturnStatistics(PerformanceReport report)
    {
        try
        {
            if (DailyReturnText != null)
            {
                DailyReturnText.Text = $"{report.DailyReturn:P2}";
                DailyReturnText.Foreground = GetReturnColor(report.DailyReturn);
            }

            if (WeeklyReturnText != null)
            {
                WeeklyReturnText.Text = $"{report.WeeklyReturn:P2}";
                WeeklyReturnText.Foreground = GetReturnColor(report.WeeklyReturn);
            }

            if (MonthlyReturnText != null)
            {
                MonthlyReturnText.Text = $"{report.MonthlyReturn:P2}";
                MonthlyReturnText.Foreground = GetReturnColor(report.MonthlyReturn);
            }

            if (AnnualizedReturnText != null)
            {
                AnnualizedReturnText.Text = $"{report.AnnualizedReturn:P2}";
                AnnualizedReturnText.Foreground = GetReturnColor(report.AnnualizedReturn);
            }

            if (CumulativeReturnText != null)
            {
                CumulativeReturnText.Text = $"{report.TotalReturn:P2}";
                CumulativeReturnText.Foreground = GetReturnColor(report.TotalReturn);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 更新收益率统计失败");
        }
    }

    /// <summary>
    /// 更新交易统计
    /// </summary>
    private void UpdateTradeStatistics(PerformanceReport report)
    {
        try
        {
            if (TotalTradesText != null)
            {
                TotalTradesText.Text = $"{report.TotalTrades} 笔";
            }

            if (TradeWinRateText != null)
            {
                TradeWinRateText.Text = $"{report.WinRate:P0}";
            }

            if (AvgWinText != null)
            {
                AvgWinText.Text = $"{report.AverageWin:F2} USDT";
            }

            if (AvgLossText != null)
            {
                AvgLossText.Text = $"{Math.Abs(report.AverageLoss):F2} USDT";
            }

            if (ProfitFactorText != null)
            {
                ProfitFactorText.Text = $"{report.ProfitFactor:F2}";
                ProfitFactorText.Foreground = report.ProfitFactor >= 1.5
                    ? new SolidColorBrush(WpfColor.FromRgb(16, 185, 129))
                    : new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 更新交易统计失败");
        }
    }

    /// <summary>
    /// 更新风险指标
    /// </summary>
    private void UpdateRiskMetrics(PerformanceReport report)
    {
        try
        {
            if (RiskSharpeText != null)
            {
                RiskSharpeText.Text = $"{report.SharpeRatio:F2}";
                RiskSharpeText.Foreground = GetSharpeRatioColor(report.SharpeRatio);
            }

            if (SortinoRatioText != null)
            {
                SortinoRatioText.Text = $"{report.SortinoRatio:F2}";
                SortinoRatioText.Foreground = GetSharpeRatioColor(report.SortinoRatio);
            }

            if (CalmarRatioText != null)
            {
                CalmarRatioText.Text = $"{report.CalmarRatio:F2}";
                CalmarRatioText.Foreground = report.CalmarRatio >= 1.0
                    ? new SolidColorBrush(WpfColor.FromRgb(16, 185, 129))
                    : new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
            }

            if (ExpectancyText != null)
            {
                ExpectancyText.Text = $"{report.Expectancy:F2} USDT";
                ExpectancyText.Foreground = report.Expectancy >= 0
                    ? new SolidColorBrush(WpfColor.FromRgb(16, 185, 129))
                    : new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 更新风险指标失败");
        }
    }

    /// <summary>
    /// 渲染净值曲线
    /// </summary>
    private void RenderEquityCurve(PerformanceReport report)
    {
        try
        {
            if (EquityPlot == null)
            {
                LogService.Warning("[PerformanceDashboardView] EquityPlot 控件未加载");
                return;
            }

            Plot plt = EquityPlot.Plot;
            plt.Clear();

            if (report.EquityCurve == null || !report.EquityCurve.Any())
            {
                plt.Title("暂无净值数据");
                EquityPlot.Refresh();
                return;
            }

            // 准备数据
            double[] dates = report.EquityCurve.Select(p => p.Date.ToOADate()).ToArray();
            double[] netValues = report.EquityCurve.Select(p => p.NetValue).ToArray();
            double[] balances = report.EquityCurve.Select(p => p.Balance).ToArray();
            double[] positions = report.EquityCurve.Select(p => p.PositionValue).ToArray();

            // 绘制净值曲线 (主线)
            Scatter netValueLine = plt.Add.Scatter(dates, netValues);
            netValueLine.LineWidth = 3;
            netValueLine.Color = ScottPlot.Color.FromHex("#3B82F6");
            netValueLine.LegendText = "净值";
            netValueLine.MarkerSize = 0;

            // 绘制余额曲线
            Scatter balanceLine = plt.Add.Scatter(dates, balances);
            balanceLine.LineWidth = 2;
            balanceLine.Color = ScottPlot.Color.FromHex("#10B981");
            balanceLine.LegendText = "可用余额";
            balanceLine.MarkerSize = 0;
            balanceLine.LinePattern = LinePattern.Dotted;

            // 绘制持仓价值
            Scatter positionLine = plt.Add.Scatter(dates, positions);
            positionLine.LineWidth = 2;
            positionLine.Color = ScottPlot.Color.FromHex("#F59E0B");
            positionLine.LegendText = "持仓价值";
            positionLine.MarkerSize = 0;
            positionLine.LinePattern = LinePattern.Dashed;

            // 图表设置
            plt.Title("账户净值曲线");
            plt.Axes.Left.Label.Text = "净值 (USDT)";
            plt.Axes.Bottom.Label.Text = "日期";
            plt.Axes.DateTimeTicksBottom();
            plt.Legend.IsVisible = true;
            plt.Legend.Location = Alignment.UpperLeft;
            plt.Grid.MajorLineColor = ScottPlot.Color.FromHex("#E5E7EB");

            EquityPlot.Refresh();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 渲染净值曲线失败");
        }
    }

    /// <summary>
    /// 账户类型切换
    /// </summary>
    private async void AccountType_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (AccountTypeCombo == null || AccountTypeCombo.SelectedItem == null)
            {
                return;
            }

            if (AccountTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _currentAccountType = tag == "Live" ? AccountType.Live : AccountType.Simulated;
                await LoadPerformanceDataAsync();
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 账户类型切换失败");
        }
    }

    /// <summary>
    /// 刷新按钮
    /// </summary>
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadPerformanceDataAsync();
    }

    /// <summary>
    /// 导出按钮
    /// </summary>
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 实现导出功能
        MessageBox.Show("导出功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    #region 辅助方法

    private Brush GetReturnColor(double returnValue)
    {
        return returnValue >= 0
            ? new SolidColorBrush(WpfColor.FromRgb(16, 185, 129))
            : new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
    }

    private Brush GetSharpeRatioColor(double sharpe)
    {
        if (sharpe >= 2.0)
        {
            return new SolidColorBrush(WpfColor.FromRgb(16, 185, 129)); // 优秀 - 绿色
        }

        if (sharpe >= 1.0)
        {
            return new SolidColorBrush(WpfColor.FromRgb(59, 130, 246));  // 良好 - 蓝色
        }

        if (sharpe >= 0)
        {
            return new SolidColorBrush(WpfColor.FromRgb(245, 158, 11));    // 一般 - 黄色
        }

        return new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));                       // 差 - 红色
    }

    private string GetSharpeRatioRating(double sharpe)
    {
        if (sharpe >= 2.0)
        {
            return "优秀";
        }

        if (sharpe >= 1.0)
        {
            return "良好";
        }

        if (sharpe >= 0)
        {
            return "一般";
        }

        return "较差";
    }

    private Brush GetDrawdownColor(double drawdown)
    {
        double absDrawdown = Math.Abs(drawdown);
        if (absDrawdown <= 0.1)
        {
            return new SolidColorBrush(WpfColor.FromRgb(16, 185, 129)); // 良好
        }

        if (absDrawdown <= 0.2)
        {
            return new SolidColorBrush(WpfColor.FromRgb(245, 158, 11));  // 一般
        }

        return new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));                            // 较差
    }

    private string GetDrawdownStatus(double drawdown)
    {
        double absDrawdown = Math.Abs(drawdown);
        if (absDrawdown <= 0.1)
        {
            return "良好";
        }

        if (absDrawdown <= 0.2)
        {
            return "警告";
        }

        return "危险";
    }

    #endregion
}
