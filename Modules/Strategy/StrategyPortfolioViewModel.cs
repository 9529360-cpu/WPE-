using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using 币安量化机器人.Models;
using 币安量化机器人.Modules.Strategy;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Strategy
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class StrategyPortfolioViewModel : INotifyPropertyChanged
    {
        private readonly StrategyPortfolioManager _portfolioManager;
        private readonly IDialogService _dialogService;

        public ObservableCollection<StrategyRow> Strategies { get; } = new();
        public ICollectionView StrategiesView { get; private set; }

        public ICommand RefreshCommand { get; }
        public ICommand BalanceCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ClearSearchCommand { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value)
                {
                    return;
                }
                _searchText = value;
                OnPropertyChanged();
                StrategiesView?.Refresh();
            }
        }

        private bool _futuresOnly = true;
        public bool FuturesOnly
        {
            get => _futuresOnly;
            set
            {
                if (_futuresOnly == value)
                {
                    return;
                }
                _futuresOnly = value;
                OnPropertyChanged();
                StrategiesView?.Refresh();
            }
        }

        // Portfolio stats exposed to View
        private decimal _portfolioTotalReturn;
        public decimal PortfolioTotalReturn
        {
            get => _portfolioTotalReturn;
            set { _portfolioTotalReturn = value; OnPropertyChanged(); }
        }

        private string _totalReturnText = "0%";
        public string TotalReturnText
        {
            get => _totalReturnText;
            set { _totalReturnText = value; OnPropertyChanged(); }
        }

        private string _sharpeText = "0.00";
        public string SharpeText
        {
            get => _sharpeText;
            set { _sharpeText = value; OnPropertyChanged(); }
        }

        private string _runningCountText = "0/0";
        public string RunningCountText
        {
            get => _runningCountText;
            set { _runningCountText = value; OnPropertyChanged(); }
        }

        private string _totalStrategiesText = "(0 个策略)";
        public string TotalStrategiesText
        {
            get => _totalStrategiesText;
            set { _totalStrategiesText = value; OnPropertyChanged(); }
        }

        private int _filteredCount;
        public int FilteredCount
        {
            get => _filteredCount;
            private set { _filteredCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredCountText)); }
        }

        public string FilteredCountText => $"已筛选 {FilteredCount} / {Strategies.Count}";

        public StrategyPortfolioViewModel(StrategyPortfolioManager portfolioManager) : this(portfolioManager, new DialogService()) { }

        public StrategyPortfolioViewModel(StrategyPortfolioManager portfolioManager, IDialogService dialogService)
        {
            _portfolioManager = portfolioManager ?? throw new ArgumentNullException(nameof(portfolioManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            RefreshCommand = new RelayCommand(_ => Refresh());
            BalanceCommand = new RelayCommand(_ => Balance());
            StartCommand = new RelayCommand(async p => await StartAsync(p));
            StopCommand = new RelayCommand(p => Stop(p));
            AddCommand = new RelayCommand(_ => Add());
            EditCommand = new RelayCommand(p => Edit(p));
            DeleteCommand = new RelayCommand(p => Delete(p));
            ClearSearchCommand = new RelayCommand(_ => { SearchText = string.Empty; UpdateFilteredCount(); });

            // create view
            StrategiesView = CollectionViewSource.GetDefaultView(Strategies);
            StrategiesView.Filter = StrategyFilter;
            StrategiesView.CollectionChanged += (_, __) => UpdateFilteredCount();

            // load initial
            Refresh();

            // subscribe
            _portfolioManager.StrategiesChanged += () => Refresh();
            UpdateFilteredCount();
        }

        private bool StrategyFilter(object obj)
        {
            if (obj is not StrategyRow row)
            {
                return false;
            }
            if (FuturesOnly && row.Market != MarketType.Futures)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }
            var t = SearchText.Trim().ToLowerInvariant();
            return (row.Name?.ToLowerInvariant().Contains(t) ?? false) || (row.Type?.ToLowerInvariant().Contains(t) ?? false) || (row.Id?.ToLowerInvariant().Contains(t) ?? false);
        }

        private void Refresh()
        {
            Strategies.Clear();
            var all = _portfolioManager.GetAllStrategies();
            var list = _futuresOnly ? all.Where(s => s.Market == MarketType.Futures) : all;
            foreach (var s in list)
            {
                Strategies.Add(new StrategyRow
                {
                    Id = s.Id,
                    Name = s.Name,
                    Type = s.Type,
                    Weight = s.Weight,
                    IsRunning = s.IsRunning,
                    TotalReturn = s.TotalReturn,
                    SharpeRatio = s.SharpeRatio,
                    TotalTrades = s.TotalTrades,
                    WinningTrades = s.WinningTrades,
                    WinRate = s.WinRate,
                    RunningTime = s.RunningTime,
                    Market = s.Market,
                    Stage = s.Stage,
                    AuditSource = s.Audit?.Source ?? string.Empty,
                    AuditTimestampUtc = s.Audit?.CreatedUtc
                });
            }

            // update portfolio stats
            var stats = _portfolioManager.GetPortfolioStats();
            PortfolioTotalReturn = stats.TotalReturn;
            TotalReturnText = $"{stats.TotalReturn:P2}";
            SharpeText = $"{stats.SharpeRatio:F2}";
            RunningCountText = $"{stats.RunningStrategies}/{stats.TotalStrategies}";
            TotalStrategiesText = $"({stats.TotalStrategies} 个策略)";

            StrategiesView?.Refresh();
            UpdateFilteredCount();
            OnPropertyChanged(nameof(Strategies));
        }

        private void Balance()
        {
            var ok = _dialogService.Confirm("确定要自动平衡所有策略的权重吗？每个策略将获得相等的资金分配。", "确认");
            if (!ok)
            {
                return;
            }
            _portfolioManager.AutoBalanceWeights();
            Refresh();
            _dialogService.ShowInfo("权重已自动平衡！", "提示");
        }

        private async Task StartAsync(object? param)
        {
            if (param is string id)
            {
                var confirm = _dialogService.Confirm("你选择了实盘模式，真实下单可能造成资金损失。确认要以实盘模式启动此策略吗？", "确认实盘启动");
                if (!confirm)
                {
                    return;
                }
                await _portfolioManager.StartStrategyAsync(id, StrategyStage.LiveRunning, null);
                Refresh();
                _dialogService.ShowInfo("策略已启动", "提示");
            }
        }

        private void Stop(object? param)
        {
            if (param is string id)
            {
                _portfolioManager.StopStrategy(id);
                Refresh();
                _dialogService.ShowInfo("策略已停止", "提示");
            }
        }

        private void Add()
        {
            _dialogService.ShowInfo("添加策略功能开发中...", "提示");
        }

        private void Edit(object? param)
        {
            if (param is string id)
            {
                _dialogService.ShowInfo($"编辑策略: {id} 功能开发中...", "提示");
            }
        }

        private void Delete(object? param)
        {
            if (param is string id)
            {
                var row = Strategies.FirstOrDefault(s => s.Id == id);
                if (row == null)
                {
                    return;
                }
                var ok = _dialogService.Confirm($"确定要删除策略 '{row.Name}' 吗？", "确认删除");
                if (!ok)
                {
                    return;
                }
                _portfolioManager.RemoveStrategy(id);
                Refresh();
                _dialogService.ShowInfo("策略已删除", "提示");
            }
        }

        private void UpdateFilteredCount()
        {
            if (StrategiesView is null)
            {
                FilteredCount = 0;
                return;
            }

            try
            {
                int count = 0;
                foreach (var _ in StrategiesView)
                {
                    count++;
                }
                FilteredCount = count;
            }
            catch
            {
                FilteredCount = 0;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
