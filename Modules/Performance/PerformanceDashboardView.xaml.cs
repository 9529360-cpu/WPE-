using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        // Temporarily disabled: ScottPlot rendering logic
        // Will be restored after Visual Studio restart resolves XAML codegen
        LogService.Info("[PerformanceDashboardView] 图表渲染已暂时禁用（等待 VS 重启恢复 XAML 编译）");
    }

    public void ExportCurrentPlot(string path, int w = 1200, int h = 600)
    {
        LogService.Warning("图表导出功能暂时禁用");
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
