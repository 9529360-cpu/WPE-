using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows; // for MessageBox
using System.Windows.Media;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Performance;

public partial class PerformanceDashboardViewModel : ObservableObject
{
    private readonly TradingAccountManager _accountManager;
    private readonly PerformanceAnalyzer _performanceAnalyzer;
    private readonly DataCacheService _cache;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private AccountType _accountType = AccountType.Simulated;

    public ObservableCollection<KpiItem> CoreKpis { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ExportCommand { get; }

    public PerformanceDashboardViewModel()
    {
        _cache = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(_cache);
        _performanceAnalyzer = new PerformanceAnalyzer(_accountManager, _cache);

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCommand = new RelayCommand(Export);
    }

    public async Task InitializeAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Status = "加载中...";

            if (_accountManager.SimulatedAccount == null)
            {
                _accountManager.CreateSimulatedAccount("模拟账户", 100m);
            }

            if (!_accountManager.HasActiveAccount)
            {
                _accountManager.SwitchToSimulated();
            }

            var report = await _performanceAnalyzer.GenerateReportAsync(AccountType);
            if (report == null)
            {
                Status = "无数据";
                return;
            }

            UpdateKpis(report);
            Status = $"已更新 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Status = $"失败: {ex.Message}";
            LogService.Error(ex, "[PerformanceDashboardViewModel] 加载失败");
        }
    }

    private void UpdateKpis(PerformanceReport r)
    {
        CoreKpis.Clear();
        CoreKpis.Add(new KpiItem
        {
            Title = "总收益率",
            ValueText = r.TotalReturn.ToString("P2"),
            SubText = $"本月 {r.MonthlyReturn:+0.00%;-0.00%;0.00%}",
            ValueBrush = r.TotalReturn >= 0 ? Brushes.SeaGreen : Brushes.IndianRed,
            SubBrush = r.MonthlyReturn >= 0 ? Brushes.SeaGreen : Brushes.IndianRed
        });
        CoreKpis.Add(new KpiItem
        {
            Title = "夏普比率",
            ValueText = r.SharpeRatio.ToString("F2"),
            SubText = GetSharpeRating(r.SharpeRatio),
            ValueBrush = GetSharpeColor(r.SharpeRatio),
            SubBrush = GetSharpeColor(r.SharpeRatio)
        });
        CoreKpis.Add(new KpiItem
        {
            Title = "最大回撤",
            ValueText = r.MaxDrawdown.ToString("P2"),
            SubText = GetDrawdownStatus(r.MaxDrawdown),
            ValueBrush = GetDrawdownColor(r.MaxDrawdown),
            SubBrush = GetDrawdownColor(r.MaxDrawdown)
        });
        CoreKpis.Add(new KpiItem
        {
            Title = "胜率",
            ValueText = r.WinRate.ToString("P0"),
            SubText = $"{r.WinningTrades}/{r.TotalTrades} 笔",
            ValueBrush = r.WinRate >= 0.5 ? Brushes.SeaGreen : Brushes.IndianRed
        });

        // 组合指标来自 StrategyPortfolio
        var stats = ServiceLocator.StrategyPortfolio.GetPortfolioStats();
        CoreKpis.Add(new KpiItem
        {
            Title = "组合年化波动率",
            ValueText = stats.Volatility.ToString("F2"),
            SubText = "加权近似",
            ValueBrush = Brushes.DodgerBlue
        });
        CoreKpis.Add(new KpiItem
        {
            Title = "风险利用率",
            ValueText = stats.RiskUtilization.ToString("F2"),
            SubText = "≈组合夏普",
            ValueBrush = Brushes.MediumPurple
        });
    }

    private void Export()
    {
        // 先简单保留原逻辑指向服务层（可后续抽象）
        try
        {
            var report = _performanceAnalyzer.GenerateReportAsync(AccountType).GetAwaiter().GetResult();
            if (report == null || report.EquityCurve == null || report.EquityCurve.Count == 0)
            {
                MessageBox.Show("无可导出数据", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Exports");
            System.IO.Directory.CreateDirectory(dir);
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string csv = System.IO.Path.Combine(dir, $"Equity_{AccountType}_{ts}.csv");
            using (var sw = new System.IO.StreamWriter(csv, false, System.Text.Encoding.UTF8))
            {
                sw.WriteLine("Date,NetValue,Balance,PositionValue");
                foreach (var p in report.EquityCurve)
                {
                    sw.WriteLine($"{p.Date:yyyy-MM-dd HH:mm:ss},{p.NetValue:F4},{p.Balance:F4},{p.PositionValue:F4}");
                }
            }
            MessageBox.Show($"导出成功: {csv}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败: {ex.Message}");
        }
    }

    private Brush GetSharpeColor(double s) => s switch
    {
        >= 2.0 => Brushes.SeaGreen,
        >= 1.0 => Brushes.DodgerBlue,
        >= 0 => Brushes.Goldenrod,
        _ => Brushes.IndianRed
    };

    private string GetSharpeRating(double s) => s switch
    {
        >= 2.0 => "优秀",
        >= 1.0 => "良好",
        >= 0 => "一般",
        _ => "较差"
    };

    private Brush GetDrawdownColor(double d)
    {
        double a = Math.Abs(d);
        if (a <= 0.1)
        {
            return Brushes.SeaGreen;
        }
        if (a <= 0.2)
        {
            return Brushes.Goldenrod;
        }
        return Brushes.IndianRed;
    }

    private string GetDrawdownStatus(double d)
    {
        double a = Math.Abs(d);
        if (a <= 0.1)
        {
            return "良好";
        }
        if (a <= 0.2)
        {
            return "警告";
        }
        return "危险";
    }

    partial void OnAccountTypeChanged(AccountType value)
    {
        // 切换账户后刷新
        _ = RefreshCommand.ExecuteAsync(null);
    }
}
