using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using 币安量化机器人.Modules;

namespace 币安量化机器人.Modules.Market;

public partial class RealtimeView : UserControl, IModuleLifecycle
{
    private readonly ObservableCollection<TickerQuote> _quotes = new();
    private readonly BinanceApiClient _api = ServiceLocator.Api;
    private readonly BinanceStreamClient _stream = ServiceLocator.Stream;
    private readonly AiForecastService _aiService = ServiceLocator.Ai;
    private ICollectionView? _view;
    private bool _initialized;
    private string _interval = "1m";
    private CancellationTokenSource? _streamCts;
    private readonly object _syncRoot = new();

    public RealtimeView()
    {
        InitializeComponent();
    }

    public async Task StartAsync()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        ConnectionText.Text = "连接：准备连接";
        _stream.ConnectionStatusChanged += OnConnectionStatusChanged;

        try
        {
            StatusText.Text = "状态：正在从 Binance 拉取 24h 行情...";
            await LoadInitialAsync();
            StatusText.Text = $"状态：已加载 {_quotes.Count} 条合约 · 正在订阅实时数据";
        }
        catch (Exception ex)
        {
            StatusText.Text = "状态：加载失败";
            MessageBox.Show(ex.Message, "实时行情", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public Task StopAsync()
    {
        _stream.MiniTickerReceived -= OnMiniTicker;
        _stream.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _streamCts?.Cancel();
        _streamCts = null;
        return Task.CompletedTask;
    }

    private async Task LoadInitialAsync()
    {
        _quotes.Clear();
        IReadOnlyList<TickerQuote> tickers = await _api.GetMiniTickersAsync(cancellationToken: CancellationToken.None);
        foreach (TickerQuote? quote in tickers.OrderByDescending(q => q.Volume).Take(120))
        {
            quote.AddPricePoint(quote.LastPrice);
            _quotes.Add(quote);
        }

        if (_view is null)
        {
            _view = CollectionViewSource.GetDefaultView(_quotes);
            _view.Filter = FilterQuotes;
            QuotesGrid.ItemsSource = _view;
        }

        SymbolSelector.ItemsSource = _quotes;
        SymbolSelector.DisplayMemberPath = nameof(TickerQuote.Symbol);
        SymbolSelector.SelectedValuePath = nameof(TickerQuote.Symbol);
        SymbolSelector.SelectedIndex = 0;
        IntervalSelector.SelectedIndex = 0;
        _view?.Refresh();
        QuotesGrid.SelectedIndex = 0;
        await SubscribeStreamAsync(_quotes.Select(q => q.Symbol).Take(50));
    }

    private async Task SubscribeStreamAsync(IEnumerable<string> symbols)
    {
        _stream.MiniTickerReceived -= OnMiniTicker;
        _stream.MiniTickerReceived += OnMiniTicker;
        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();
        await _stream.ConnectMiniTickerAsync(symbols, _streamCts.Token).ConfigureAwait(false);
    }

    private void OnConnectionStatusChanged(string status)
    {
        Dispatcher.Invoke(() => ConnectionText.Text = $"连接：{status}");
    }

    private void OnMiniTicker(MiniTickerUpdate update)
    {
        Dispatcher.Invoke(() =>
        {
            TickerQuote? quote = _quotes.FirstOrDefault(q => string.Equals(q.Symbol, update.Symbol, StringComparison.OrdinalIgnoreCase));
            if (quote is null)
            {
                return;
            }

            lock (_syncRoot)
            {
                quote.LastPrice = update.LastPrice;
                quote.IndexPrice = update.IndexPrice;
                quote.ChangePercent = update.ChangePercent;
                quote.Volume = update.Volume;
                quote.HighPrice = update.HighPrice;
                quote.LowPrice = update.LowPrice;
                quote.AddPricePoint(update.LastPrice);
            }

            if (QuotesGrid.SelectedItem is TickerQuote selected && ReferenceEquals(selected, quote))
            {
                UpdateDetails(selected);
            }
        });
    }

    private bool FilterQuotes(object obj)
    {
        if (obj is not TickerQuote quote)
        {
            return false;
        }

        string keyword = SearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return quote.Symbol.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        StatusText.Text = _view is null
            ? "状态：未初始化"
            : $"状态：过滤后剩余 {_view.Cast<object>().Count()} 条";
    }

    private void QuotesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuotesGrid.SelectedItem is TickerQuote quote)
        {
            SymbolSelector.SelectedValue = quote.Symbol;
            UpdateDetails(quote);
        }
    }

    private void SymbolSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SymbolSelector.SelectedValue is string symbol)
        {
            TickerQuote? quote = _quotes.FirstOrDefault(q => q.Symbol == symbol);
            if (quote is not null)
            {
                QuotesGrid.SelectedItem = quote;
            }
        }
    }

    private void IntervalSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalSelector.SelectedItem is ComboBoxItem item && item.Content is string interval)
        {
            _interval = interval;
        }
    }

    private async void Forecast_Click(object sender, RoutedEventArgs e)
    {
        if (QuotesGrid.SelectedItem is not TickerQuote quote)
        {
            MessageBox.Show("请先选择一个交易对", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText.Text = $"状态：AI 正在分析 {quote.Symbol} ...";
            var request = new ForecastRequest
            {
                Symbol = quote.Symbol,
                Interval = _interval,
                Horizon = 12,
                HistoryPoints = 240
            };

            ForecastResult result = await _aiService.ForecastAsync(request).ConfigureAwait(true);

            if (result != null)
            {
                UpdateForecast(result);
                StatusText.Text = $"状态：AI 预测已完成（{result.Symbol}）";
            }
            else
            {
                StatusText.Text = "状态：AI 预测返回空结果";
            }
        }
        catch (KeyNotFoundException ex)
        {
            StatusText.Text = "状态：交易对不存在";
            MessageBox.Show($"找不到交易对 {quote.Symbol}\n\n{ex.Message}", "AI 预测", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = "状态：AI 预测失败";
            LogService.Error(ex, "AI预测失败");
            MessageBox.Show($"预测失败: {ex.Message}", "AI 预测", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateForecast(ForecastResult result)
    {
        ForecastSummary.Text = $"未来 {result.Predicted.Count} 根K线预测价格区间 {result.ConfidenceLower.Last():F2} ~ {result.ConfidenceUpper.Last():F2}";
        ForecastRisk.Text = $"预期收益 {result.ExpectedReturn:P2} · 预测波动 {result.ExpectedVolatility:P2} · 风险系数 {result.PredictedRisk:F4}";
    }

    private void UpdateDetails(TickerQuote quote)
    {
        DetailTitle.Text = $"{quote.Symbol} 实时行情";
        DetailPrice.Text = $"最新 {quote.LastPrice:F4} · 指数价 {quote.IndexPrice:F4}";

        DetailChange.Text = $"24h 涨跌：{quote.ChangePercent:+0.00;-0.00;0.00}%";
        DetailChange.Foreground = quote.ChangePercent switch
        {
            > 0.0 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)),
            < 0.0 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
            _ => Brushes.Gray
        };

        DetailRange.Text = $"24h 高 / 低：{quote.HighPrice:F4} / {quote.LowPrice:F4}（波动 {quote.RangePercent:P2}）";
        DetailVolume.Text = $"预估成交额：{quote.Volume:N0} USDT";

        RenderHistory(quote);
    }

    private void RenderHistory(TickerQuote quote)
    {
        var ctrl = this.FindName("PricePlot");
        if (ctrl is ScottPlot.WPF.WpfPlot wpfPlot)
        {
            var plt = wpfPlot.Plot;
            plt.Clear();

            if (quote.PriceHistory == null || quote.PriceHistory.Count == 0)
            {
                plt.Title("暂无历史数据");
                wpfPlot.Refresh();
                return;
            }

            double[] prices = quote.PriceHistory.ToArray();
            var signal = plt.Add.Signal(prices);
            signal.Color = ScottPlot.Color.FromHex("#3B82F6");
            plt.Title($"{quote.Symbol} 价格走势");
            plt.YLabel("价格");
            plt.XLabel("样本");
            wpfPlot.Refresh();
        }
    }

    private async void ResetView_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        try
        {
            StatusText.Text = "状态：正在刷新行情";
            await LoadInitialAsync();
            StatusText.Text = "状态：行情已刷新";
        }
        catch (Exception ex)
        {
            StatusText.Text = "状态：刷新失败";
            MessageBox.Show(ex.Message, "刷新行情", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
