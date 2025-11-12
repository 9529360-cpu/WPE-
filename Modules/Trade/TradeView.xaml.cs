using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using 币安量化机器人.Modules;

namespace 币安量化机器人.Modules.Trade;

public partial class TradeView : UserControl, IModuleLifecycle
{
    private readonly ObservableCollection<OrderRequest> _batchOrders = new();
    private readonly BinanceApiClient _api = ServiceLocator.Api;
    private CancellationTokenSource? _cts;

    public TradeView()
    {
        InitializeComponent();
        BatchGrid.ItemsSource = _batchOrders;
        SideBox.SelectedIndex = 0;
        TypeBox.SelectedIndex = 0;
        TifBox.SelectedIndex = 0;
        // 移除构造中的 LoadSymbolsAsync，使用 StartAsync 启动
    }

    private async Task LoadSymbolsAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (this.FindName("StatusText") is TextBlock status)
            {
                status.Text = "状态：加载交易对...";
            }

            IReadOnlyList<TickerQuote> tickers = await _api.GetMiniTickersAsync();

            ct.ThrowIfCancellationRequested();

            var list = tickers.Select(t => t.Symbol).OrderBy(s => s).ToList();
            if (this.FindName("SymbolBox") is ComboBox symBox)
            {
                symBox.ItemsSource = list;
                if (symBox.Items.Count > 0)
                {
                    symBox.SelectedIndex = 0;
                }
            }

            if (this.FindName("StatusText") is TextBlock status2)
            {
                status2.Text = "状态：交易对已刷新";
            }
        }
        catch (OperationCanceledException)
        {
            if (this.FindName("StatusText") is TextBlock status)
            {
                status.Text = "状态：交易对加载已取消";
            }
            LogService.Info("[TradeView] LoadSymbolsAsync 已取消");
        }
        catch (Exception ex)
        {
            if (this.FindName("StatusText") is TextBlock statusErr)
            {
                statusErr.Text = "状态：交易对获取失败";
            }
            MessageBox.Show(ex.Message, "交易对", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private OrderRequest BuildRequest()
    {
        var request = new OrderRequest
        {
            Symbol = SymbolBox.Text.Trim().ToUpperInvariant(),
            Side = (SideBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Sell" ? OrderSide.Sell : OrderSide.Buy,
            Type = Enum.TryParse<OrderType>((TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out OrderType type) ? type : OrderType.Market,
            TimeInForce = Enum.TryParse<TimeInForce>((TifBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out TimeInForce tif) ? tif : TimeInForce.Gtc
        };

        if (decimal.TryParse(QuantityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal qty))
        {
            request.Quantity = qty;
        }

        if (decimal.TryParse(PriceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal price))
        {
            request.Price = price;
        }

        if (decimal.TryParse(StopPriceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal stop))
        {
            request.StopPrice = stop;
        }

        return request;
    }

    private async void SubmitOrder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OrderRequest request = BuildRequest();
            ValidateRequest(request);
            if (this.FindName("StatusText") is TextBlock st)
            {
                st.Text = $"状态：正在发送 {request.Symbol} 单笔订单";
            }
            OrderResponse result = await _api.PlaceOrderAsync(request);
            if (this.FindName("StatusText") is TextBlock st2)
            {
                st2.Text = $"状态：订单 {result.OrderId} 已提交，成交 {result.ExecutedQuantity}";
            }
            MessageBox.Show($"订单 {result.OrderId} 状态：{result.Status}\n平均成交价：{result.AvgPrice}", "下单成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (this.FindName("StatusText") is TextBlock st)
            {
                st.Text = "状态：下单失败";
            }
            MessageBox.Show(ex.Message, "下单失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OrderRequest request = BuildRequest();
            ValidateRequest(request);
            _batchOrders.Add(request);
            if (this.FindName("StatusText") is TextBlock st)
            {
                st.Text = $"状态：已加入批量（{_batchOrders.Count}）";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "参数校验", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExecuteBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_batchOrders.Count == 0)
        {
            MessageBox.Show("请先添加批量订单。", "批量下单", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (this.FindName("StatusText") is TextBlock st)
            {
                st.Text = "状态：执行批量订单中...";
            }
            var batch = new BatchOrderRequest { Orders = _batchOrders.ToArray() };
            IReadOnlyList<OrderResponse> results = await _api.PlaceBatchOrdersAsync(batch);
            if (this.FindName("StatusText") is TextBlock st2)
            {
                st2.Text = $"状态：批量下单完成（{results.Count}）";
            }
            MessageBox.Show($"批量下单完成，返回 {results.Count} 条结果。", "批量下单", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (this.FindName("StatusText") is TextBlock st)
            {
                st.Text = "状态：批量下单失败";
            }
            MessageBox.Show(ex.Message, "批量下单", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (BatchGrid.SelectedItem is OrderRequest req)
        {
            _batchOrders.Remove(req);
            if (this.FindName("StatusText") is TextBlock st)
            {
                st.Text = $"状态：已移除一条批量订单（剩余 {_batchOrders.Count}）";
            }
        }
    }

    private void ClearBatch_Click(object sender, RoutedEventArgs e)
    {
        _batchOrders.Clear();
        if (this.FindName("StatusText") is TextBlock st)
        {
            st.Text = "状态：批量列表已清空";
        }
    }

    private void RefreshSymbols_Click(object sender, RoutedEventArgs e) => _ = LoadSymbolsAsync(CancellationToken.None);

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string? type = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        bool requiresPrice = type is "Limit" or "StopLossLimit" or "TakeProfitLimit";
        PriceBox.IsEnabled = requiresPrice;
        StopPriceBox.IsEnabled = type is "StopLoss" or "StopLossLimit" or "TakeProfit" or "TakeProfitLimit";
    }

    private static void ValidateRequest(OrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            throw new InvalidOperationException("请填写交易对");
        }

        if (request.Quantity <= 0)
        {
            throw new InvalidOperationException("数量需大于 0");
        }

        if (request.Type is OrderType.Limit or OrderType.StopLossLimit or OrderType.TakeProfitLimit)
        {
            if (request.Price <= 0)
            {
                throw new InvalidOperationException("限价单需要填写价格");
            }
        }
        if (request.Type is OrderType.StopLoss or OrderType.StopLossLimit or OrderType.TakeProfit or OrderType.TakeProfitLimit)
        {
            if (request.StopPrice <= 0)
            {
                throw new InvalidOperationException("触发单需要填写触发价");
            }
        }
    }

    // IModuleLifecycle
    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        await LoadSymbolsAsync(_cts.Token);
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
            LogService.Error(ex, "[TradeView] StopAsync 失败");
            return Task.CompletedTask;
        }
    }
}
