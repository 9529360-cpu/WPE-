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

namespace 币安量化机器人.Modules.Trade;

public partial class TradeView : UserControl
{
    private readonly ObservableCollection<OrderRequest> _batchOrders = new();
    private readonly BinanceApiClient? _api = ServiceLocator.Api;
    private CancellationTokenSource? _cts = new();

    public TradeView()
    {
        InitializeComponent();

        BatchGrid.ItemsSource = _batchOrders;

        // 默认选择
        SideBox.SelectedIndex = 0;
        TypeBox.SelectedIndex = 0;
        TifBox.SelectedIndex = 0;

        // 异步加载合约（安全的 fire-and-forget，内部已处理异常）
        _ = LoadSymbolsAsync(CancellationToken.None);
    }

    private async Task LoadSymbolsAsync(CancellationToken ct)
    {
        try
        {
            // Ensure UI access for item operations
            await Dispatcher.InvokeAsync(() => SymbolBox.Items.Clear());

            var api = ServiceLocator.Api;
            if (api != null)
            {
                var tickers = await api.GetMiniTickersAsync(null, ct).ConfigureAwait(false);
                if (tickers != null && tickers.Count > 0)
                {
                    foreach (dynamic t in tickers)
                    {
                        // TickerQuote contains symbol property named Symbol or maybe s; use reflection-safe access
                        string sym = t?.Symbol ?? t?.symbol ?? t?.s ?? string.Empty;
                        if (!string.IsNullOrEmpty(sym))
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (!SymbolBox.Items.Contains(sym))
                                {
                                    SymbolBox.Items.Add(sym);
                                }
                            });
                        }
                    }
                }
            }

            // fallback
            await Dispatcher.InvokeAsync(() =>
            {
                if (SymbolBox.Items.Count == 0)
                {
                    SymbolBox.Items.Add("BTCUSDT");
                    SymbolBox.Items.Add("ETHUSDT");
                    SymbolBox.Items.Add("BNBUSDT");
                }

                SymbolBox.SelectedIndex = 0;
                StatusText.Text = "状态：合约列表已加载";
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[TradeView] 加载合约失败");
            // fallback symbols
            await Dispatcher.InvokeAsync(() =>
            {
                if (SymbolBox.Items.Count == 0)
                {
                    SymbolBox.Items.Add("BTCUSDT");
                    SymbolBox.Items.Add("ETHUSDT");
                }
                StatusText.Text = "状态：合约加载失败，使用默认列表";
            });
        }
    }

    private OrderRequest BuildRequest()
    {
        var req = new OrderRequest();

        req.Symbol = (SymbolBox.SelectedItem as string) ?? SymbolBox.Text ?? "BTCUSDT";

        if (SideBox.SelectedItem is ComboBoxItem sideItem && Enum.TryParse<OrderSide>(sideItem.Content?.ToString() ?? "Buy", out var side))
        {
            req.Side = side;
        }
        else
        {
            req.Side = OrderSide.Buy;
        }

        if (TypeBox.SelectedItem is ComboBoxItem typeItem && Enum.TryParse<OrderType>(typeItem.Content?.ToString() ?? "Market", out var type))
        {
            req.Type = type;
        }
        else
        {
            req.Type = OrderType.Market;
        }

        if (decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
        {
            req.Quantity = qty;
        }

        if (decimal.TryParse(PriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
        {
            req.Price = price;
        }

        if (decimal.TryParse(StopPriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var stop))
        {
            req.StopPrice = stop;
        }

        if (TifBox.SelectedItem is ComboBoxItem tifItem && Enum.TryParse<TimeInForce>(tifItem.Content?.ToString() ?? "Gtc", out var tif))
        {
            req.TimeInForce = tif;
        }

        return req;
    }

    private static void ValidateRequest(OrderRequest request)
    {
        if (string.IsNullOrEmpty(request.Symbol))
            throw new InvalidOperationException("Symbol is required");
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Quantity must be > 0");
        if (request.Type != OrderType.Market && request.Price <= 0)
            throw new InvalidOperationException("Price must be set for non-market orders");
    }

    private async void SubmitOrder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = BuildRequest();
            ValidateRequest(request);

            // Simulated by default
            if (RuntimeState.CurrentAccountType == AccountType.Simulated)
            {
                // simulate
                var resp = new OrderResponse
                {
                    Symbol = request.Symbol,
                    OrderId = DateTime.UtcNow.Ticks % int.MaxValue,
                    ClientOrderId = Guid.NewGuid().ToString("N").Substring(0, 8),
                    ExecutedQuantity = request.Quantity,
                    CumulativeQuoteQuantity = request.Quantity * (request.Price == 0 ? 0 : request.Price),
                    Price = request.Price,
                    AvgPrice = request.Price,
                    Status = "FILLED",
                    Time = DateTime.UtcNow
                };

                // persist to cache for history
                try
                {
                    var cache = ServiceLocator.Cache;
                    if (cache != null)
                    {
                        var rec = new OrderHistoryRecord
                        {
                            OrderId = resp.OrderId.ToString(),
                            Symbol = resp.Symbol,
                            Side = request.Side.ToString(),
                            Type = request.Type.ToString(),
                            Quantity = (double)resp.ExecutedQuantity,
                            Price = (double?)resp.AvgPrice,
                            Status = resp.Status,
                            FilledQuantity = (double)resp.ExecutedQuantity,
                            AvgFillPrice = (double?)resp.AvgPrice,
                            Commission = 0,
                            CreatedAt = resp.Time,
                            UpdatedAt = resp.Time,
                            FilledAt = resp.Time
                        };
                        _ = cache.SaveOrderAsync(rec);
                    }
                }
                catch { /* non-blocking */ }

                MessageBox.Show($"模拟下单成功: {resp.Symbol} {resp.ExecutedQuantity} {resp.Status}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = $"状态：模拟下单完成 {resp.Symbol} {resp.Status}";
                return;
            }

            // Live execution
            var api = ServiceLocator.Api;
            var liveResp = await api.PlaceOrderAsync(request);

            // persist
            try
            {
                var cache = ServiceLocator.Cache;
                if (cache != null)
                {
                    var rec = new OrderHistoryRecord
                    {
                        OrderId = liveResp.OrderId.ToString(),
                        Symbol = liveResp.Symbol,
                        Side = request.Side.ToString(),
                        Type = request.Type.ToString(),
                        Quantity = (double)liveResp.ExecutedQuantity,
                        Price = (double?)liveResp.AvgPrice,
                        Status = liveResp.Status,
                        FilledQuantity = (double)liveResp.ExecutedQuantity,
                        AvgFillPrice = (double?)liveResp.AvgPrice,
                        Commission = 0,
                        CreatedAt = liveResp.Time,
                        UpdatedAt = liveResp.Time,
                        FilledAt = liveResp.Time
                    };
                    await cache.SaveOrderAsync(rec);
                }
            }
            catch { /* ignore */ }

            MessageBox.Show($"下单成功: {liveResp.Symbol} {liveResp.Status}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = $"状态：下单成功 {liveResp.Symbol} {liveResp.Status}";
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[TradeView] 下单失败");
            MessageBox.Show($"下单失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "状态：下单失败";
        }
    }

    private void AddBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var req = BuildRequest();
            ValidateRequest(req);
            _batchOrders.Add(req);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExecuteBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_batchOrders.Count == 0)
        {
            MessageBox.Show("没有批量订单需要执行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (RuntimeState.CurrentAccountType == AccountType.Simulated)
            {
                foreach (var req in _batchOrders.ToList())
                {
                    // simulate small delay
                    await Task.Delay(200);
                    StatusText.Text = $"状态：模拟执行 {req.Symbol} ...";
                }

                MessageBox.Show("模拟批量执行完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "状态：模拟批量执行完成";
                _batchOrders.Clear();
                return;
            }

            // Live batch via API
            var batch = new BatchOrderRequest { Orders = _batchOrders.ToList(), UseOneWayTrigger = false, UseHedgeMode = false };
            var api = ServiceLocator.Api;
            var results = await api.PlaceBatchOrdersAsync(batch);

            // persist results
            var cache = ServiceLocator.Cache;
            if (cache != null)
            {
                foreach (var r in results)
                {
                    var rec = new OrderHistoryRecord
                    {
                        OrderId = r.OrderId.ToString(),
                        Symbol = r.Symbol,
                        Side = string.Empty,
                        Type = string.Empty,
                        Quantity = (double)r.ExecutedQuantity,
                        Price = (double?)r.AvgPrice,
                        Status = r.Status,
                        FilledQuantity = (double)r.ExecutedQuantity,
                        AvgFillPrice = (double?)r.AvgPrice,
                        Commission = 0,
                        CreatedAt = r.Time,
                        UpdatedAt = r.Time,
                        FilledAt = r.Time
                    };
                    await cache.SaveOrderAsync(rec);
                }
            }

            MessageBox.Show("批量下单完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "状态：批量下单完成";
            _batchOrders.Clear();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[TradeView] 执行批量失败");
            MessageBox.Show($"执行批量失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "状态：批量执行失败";
        }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (BatchGrid.SelectedItem is OrderRequest req)
        {
            _batchOrders.Remove(req);
        }
    }

    private void ClearBatch_Click(object sender, RoutedEventArgs e)
    {
        _batchOrders.Clear();
    }

    private void RefreshSymbols_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadSymbolsAsync(CancellationToken.None);
    }

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // placeholder - adjust UI if needed
    }

    public async Task StartAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        await LoadSymbolsAsync(_cts.Token).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { }
        return Task.CompletedTask;
    }
}
