# ?? Phase 1 数据持久化与绩效追踪使用指南

## 概述
Phase 1 已完成的数据持久化系统为你的交易机器人提供了完整的订单历史、交易记录、策略绩效和每日盈亏追踪功能。

---

## ??? 数据库结构

### 新增的 7 张表:

1. **funding_rates** - 资金费率历史
2. **price_history** - 价格历史
3. **accounts** - 账户配置
4. **orders** ? - 订单完整生命周期
5. **trades** ? - 成交记录
6. **strategy_performance** ? - 策略绩效统计
7. **daily_pnl** ? - 每日盈亏汇总

---

## ?? 使用示例

### 1?? 记录订单到数据库

```csharp
using 币安量化机器人.Services;
using 币安量化机器人.Models;

// 下单后自动保存到数据库
var orderService = ServiceLocator.OrderHistory;

// 方式1: 从 BinanceApiClient 响应记录
var response = await ServiceLocator.Api.PlaceOrderAsync(request);
await orderService.RecordOrderPlacedAsync(response, strategyName: "MeanReversion");

// 方式2: 手动构建记录
var orderRecord = new OrderHistoryRecord
{
    OrderId = "12345678",
    Symbol = "BTCUSDT",
    Side = "BUY",
    Type = "LIMIT",
    Quantity = 0.01,
    Price = 50000,
    Status = "NEW",
    FilledQuantity = 0,
    StrategyName = "MeanReversion",
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};
await ServiceLocator.Cache.SaveOrderAsync(orderRecord);
```

### 2?? 查询订单历史

```csharp
// 查询所有订单(最近100条)
var allOrders = await ServiceLocator.OrderHistory.GetOrderHistoryAsync();

// 查询特定交易对
var btcOrders = await ServiceLocator.OrderHistory.GetOrderHistoryAsync("BTCUSDT", limit: 50);

// 在UI中显示
foreach (var order in btcOrders)
{
    Console.WriteLine($"{order.CreatedAt:yyyy-MM-dd HH:mm:ss} | {order.Symbol} | {order.Side} | {order.Status} | {order.FilledQuantity}/{order.Quantity}");
}
```

### 3?? 记录交易并自动更新绩效

```csharp
var perfService = ServiceLocator.PerformanceTracking;

// 交易成交后记录
var trade = new TradeRecord
{
    TradeId = Guid.NewGuid().ToString(),
    OrderId = "12345678",
    Symbol = "BTCUSDT",
    Side = "BUY",
    Quantity = 0.01,
    Price = 50000,
    Commission = 1.0,  // USDT
    RealizedPnl = 50,  // 盈利50 USDT
    Timestamp = DateTime.UtcNow
};

// 一键记录交易 + 更新策略绩效 + 更新每日盈亏
await perfService.RecordTradeAsync("MeanReversion", trade);
```

### 4?? 查看策略绩效报告

```csharp
var report = await perfService.GetPerformanceReportAsync("MeanReversion", "BTCUSDT");

if (report != null)
{
    Console.WriteLine($"策略: {report.Basic.StrategyName}");
    Console.WriteLine($"交易对: {report.Basic.Symbol}");
    Console.WriteLine($"总交易: {report.Basic.TotalTrades}");
    Console.WriteLine($"胜率: {report.Basic.WinRate:P2}");
    Console.WriteLine($"盈亏比: {report.Basic.ProfitFactor:F2}");
    Console.WriteLine($"夏普比率: {report.Basic.SharpeRatio:F2}");
    Console.WriteLine($"最大回撤: {report.Basic.MaxDrawdown:P2}");
    Console.WriteLine($"累计盈亏: {report.Basic.TotalPnl:F2} USDT");
    Console.WriteLine($"平均盈利: {report.AvgWinSize:F2} USDT");
    Console.WriteLine($"平均亏损: {report.AvgLossSize:F2} USDT");
    Console.WriteLine($"最大单笔盈利: {report.LargestWin:F2} USDT");
    Console.WriteLine($"最大单笔亏损: {report.LargestLoss:F2} USDT");
    Console.WriteLine($"最长连胜: {report.ConsecutiveWins} 次");
    Console.WriteLine($"最长连亏: {report.ConsecutiveLosses} 次");
    Console.WriteLine($"平均持仓时间: {report.AvgHoldingTime.TotalHours:F1} 小时");
    Console.WriteLine($"每笔期望收益: {report.ExpectancyPerTrade:F2} USDT");
}
```

### 5?? 查看每日盈亏趋势

```csharp
var dailyPnl = await perfService.GetDailyPnlTrendAsync(days: 30);

foreach (var day in dailyPnl)
{
    Console.WriteLine($"{day.Date:yyyy-MM-dd} | 盈亏: {day.TotalPnl:F2} | 交易: {day.TradeCount} | 胜率: {day.WinRate:P0}");
}

// 用于图表展示
var dates = dailyPnl.Select(d => d.Date).ToArray();
var pnls = dailyPnl.Select(d => d.TotalPnl).ToArray();
// 绘制累计盈亏曲线...
```

### 6?? 在实盘交易中集成

```csharp
// 在 TradeView.xaml.cs 的下单后回调中
private async void SubmitOrder_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var request = BuildRequest();
        ValidateRequest(request);
        
        StatusText.Text = $"状态：正在发送 {request.Symbol} 单笔订单";
        var result = await _api.PlaceOrderAsync(request);
        
        // ?? 立即保存到数据库
        await ServiceLocator.OrderHistory.RecordOrderPlacedAsync(result, "Manual");
        
        StatusText.Text = $"状态：订单 {result.OrderId} 已提交并记录";
        MessageBox.Show($"订单 {result.OrderId} 已提交\n成交量: {result.ExecutedQuantity}", 
                        "下单成功", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        StatusText.Text = "状态：下单失败";
        MessageBox.Show(ex.Message, "下单失败", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
```

### 7?? 定时更新订单状态

```csharp
// 创建一个后台任务定时更新订单状态
private async Task MonitorOrdersAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            // 查询当前未完成订单
            var openOrders = await ServiceLocator.Api.GetOpenOrdersAsync();
            
            foreach (var order in openOrders)
            {
                // 更新数据库中的订单状态
                await ServiceLocator.OrderHistory.UpdateOrderStatusAsync(
                    order.OrderId.ToString(),
                    order.Status,
                    (double)order.ExecutedQuantity,
                    order.AvgPrice > 0 ? (double)order.AvgPrice : null,
                    order.Time
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"订单状态更新失败: {ex.Message}");
        }
        
        await Task.Delay(TimeSpan.FromSeconds(10), ct); // 每10秒更新一次
    }
}
```

---

## ?? UI 集成建议

### 在 RiskCenterView 中显示策略绩效

```csharp
private async void LoadStrategyPerformance()
{
    try
    {
        var report = await ServiceLocator.PerformanceTracking
            .GetPerformanceReportAsync("MeanReversion", "BTCUSDT");
        
        if (report != null)
        {
            // 更新UI控件
            StrategyNameText.Text = report.Basic.StrategyName;
            TotalTradesText.Text = report.Basic.TotalTrades.ToString();
            WinRateText.Text = report.Basic.WinRate.ToString("P2");
            ProfitFactorText.Text = report.Basic.ProfitFactor.ToString("F2");
            TotalPnlText.Text = $"{report.Basic.TotalPnl:F2} USDT";
            
            // 设置颜色(盈利绿色,亏损红色)
            TotalPnlText.Foreground = report.Basic.TotalPnl >= 0 
                ? Brushes.Green 
                : Brushes.Red;
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show($"加载策略绩效失败: {ex.Message}");
    }
}
```

### 在 DiagnosticsView 中显示每日盈亏

```csharp
private async void LoadDailyPnl()
{
    try
    {
        var dailyData = await ServiceLocator.PerformanceTracking.GetDailyPnlTrendAsync(30);
        
        // 绑定到DataGrid
        DailyPnlGrid.ItemsSource = dailyData;
        
        // 或者用ScottPlot绘制图表
        var plt = PnlChart.Plot;
        plt.Clear();
        
        double[] xs = Enumerable.Range(0, dailyData.Count).Select(i => (double)i).ToArray();
        double[] ys = dailyData.Select(d => d.TotalPnl).ToArray();
        
        plt.Add.Scatter(xs, ys);
        plt.Title("30天累计盈亏曲线");
        plt.Axes.Left.Label.Text = "盈亏 (USDT)";
        plt.Axes.Bottom.Label.Text = "日期";
        
        PnlChart.Refresh();
    }
    catch (Exception ex)
    {
        MessageBox.Show($"加载每日盈亏失败: {ex.Message}");
    }
}
```

---

## ?? 数据库查询技巧

### 直接SQL查询(高级)

```csharp
// 如果需要复杂查询,可以直接用SQLite连接
var dbPath = Path.Combine(AppContext.BaseDirectory, "Data", "terminal_cache.db");
using var connection = new SqliteConnection($"Data Source={dbPath}");
await connection.OpenAsync();

// 查询最近7天每个策略的表现
var cmd = connection.CreateCommand();
cmd.CommandText = @"
    SELECT strategy_name, symbol, total_pnl, win_rate, profit_factor
    FROM strategy_performance
    WHERE updated_at > $cutoff
    ORDER BY total_pnl DESC;
";
cmd.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds());

using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"{reader.GetString(0)} @ {reader.GetString(1)}: {reader.GetDouble(2):F2} USDT");
}
```

---

## ?? 性能优化建议

1. **批量插入**: 对于大量订单,使用事务批量插入
2. **索引优化**: 已在 `symbol` 和 `timestamp` 列建立索引
3. **定期清理**: 定期归档6个月前的数据到备份表
4. **异步操作**: 所有数据库操作都是异步的,不阻塞UI

---

## ? 完成度检查

| 功能 | 状态 |
|------|------|
| 订单历史保存 | ? |
| 交易记录保存 | ? |
| 策略绩效统计 | ? |
| 每日盈亏追踪 | ? |
| 自动更新绩效 | ? |
| 查询API完整 | ? |
| 服务注册 | ? |
| 数据库初始化 | ? |

---

## ?? 下一步

数据持久化已完成!你可以:

1. **继续API健壮性**: 添加重试+限速+熔断
2. **继续回测引擎**: 添加滑点+手续费+真实撮合
3. **UI集成**: 在风控/诊断界面展示绩效数据
4. **导出功能**: 添加CSV/Excel导出订单历史

需要我继续哪一部分?
