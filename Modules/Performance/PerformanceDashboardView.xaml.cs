using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot;
using ScottPlot.Plottables;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using System.Threading;
using System.Threading.Tasks;
using WpfColor = System.Windows.Media.Color; // 🔧 添加别名避免冲突
using 币安量化机器人.Modules;

namespace 币安量化机器人.Modules.Performance;

/// <summary>
/// 绩效分析仪表盘
/// 展示完整的交易绩效统计和可视化
/// </summary>
public partial class PerformanceDashboardView : UserControl, IModuleLifecycle
{
    private readonly TradingAccountManager _accountManager;
    private readonly PerformanceAnalyzer _performanceAnalyzer;
    private readonly DataCacheService _cacheService;

    private AccountType _currentAccountType = AccountType.Simulated;
    private CancellationTokenSource? _cts;

    public PerformanceDashboardView()
    {
        InitializeComponent();

        // 初始化服务
        _cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(_cacheService);
        _performanceAnalyzer = new PerformanceAnalyzer(_accountManager, _cacheService);

        // 移除 Loaded 绑定，使用显式 StartAsync
    }

    private async Task LoadPerformanceDataAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

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

            ct.ThrowIfCancellationRequested();

            if (report != null)
            {
                // 在 UI 线程上渲染
                await Dispatcher.InvokeAsync(() => RenderEquityCurve(report));
            }
        }
        catch (OperationCanceledException)
        {
            LogService.Info("[PerformanceDashboardView] 加载绩效数据已取消");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 加载绩效数据异常");
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
            // ScottPlot v5: use Alignment property
            plt.Legend.Alignment = Alignment.UpperLeft;
            plt.Grid.MajorLineColor = ScottPlot.Color.FromHex("#E5E7EB");

            EquityPlot.Refresh();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 渲染净值曲线失败");
        }
    }

    // 保留导出按钮逻辑供 ViewModel 调用，或后续迁移到命令
    public void ExportCurrentPlot(string path, int w = 1200, int h = 600)
    {
        try
        {
            EquityPlot?.Plot.SavePng(path, w, h);
        }
        catch (Exception ex)
        {
            LogService.Warning("导出PNG失败: {0}", ex.Message);
        }
    }

    // IModuleLifecycle
    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        await LoadPerformanceDataAsync(_cts.Token);
    }

    public Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] StopAsync 失败");
            return Task.CompletedTask;
        }
    }
}
