using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ScottPlot;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using System.Threading;
using System.Threading.Tasks;
using WpfColor = System.Windows.Media.Color;
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
    }

    private async Task LoadPerformanceDataAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (_accountManager.SimulatedAccount == null)
            {
                LogService.Info("[PerformanceDashboardView] 模拟账户不存在，正在创建...");
                _accountManager.CreateSimulatedAccount("模拟账户", 100m);
            }

            if (!_accountManager.HasActiveAccount)
            {
                LogService.Info("[PerformanceDashboardView] 没有激活账户，正在激活模拟账户...");
                _accountManager.SwitchToSimulated();
            }

            PerformanceReport report = await _performanceAnalyzer.GenerateReportAsync(_currentAccountType);

            ct.ThrowIfCancellationRequested();

            if (report != null)
            {
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

    private void RenderEquityCurve(PerformanceReport report)
    {
        try
        {
            if (EquityPlot == null)
            {
                LogService.Warning("[PerformanceDashboardView] EquityPlot 控件未加载");
                return;
            }

            var plt = EquityPlot.Plot;
            plt.Clear();

            if (report.EquityCurve == null || !report.EquityCurve.Any())
            {
                plt.Title("暂无净值数据");
                EquityPlot.Refresh();
                return;
            }

            double[] dates = report.EquityCurve.Select(p => p.Date.ToOADate()).ToArray();
            double[] netValues = report.EquityCurve.Select(p => p.NetValue).ToArray();
            double[] balances = report.EquityCurve.Select(p => p.Balance).ToArray();
            double[] positions = report.EquityCurve.Select(p => p.PositionValue).ToArray();

            plt.AddScatter(dates, netValues, color: System.Drawing.ColorTranslator.FromHtml("#3B82F6"), label: "净值", lineWidth: 3);
            plt.AddScatter(dates, balances, color: System.Drawing.ColorTranslator.FromHtml("#10B981"), label: "可用余额", lineWidth: 2, lineStyle: ScottPlot.LineStyle.Dot);
            plt.AddScatter(dates, positions, color: System.Drawing.ColorTranslator.FromHtml("#F59E0B"), label: "持仓价值", lineWidth: 2, lineStyle: ScottPlot.LineStyle.Dash);

            plt.Title("账户净值曲线");
            plt.YLabel("净值 (USDT)");
            plt.XLabel("日期");
            plt.SetAxisLimits(xMin: dates.FirstOrDefault(), xMax: dates.LastOrDefault());

            plt.Legend(location: ScottPlot.Alignment.UpperLeft);
            plt.Grid(true);

            EquityPlot.Refresh();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceDashboardView] 渲染净值曲线失败");
        }
    }

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
