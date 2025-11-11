# ??? Phase 1 API 健壮性使用指南

## 概述
Phase 1 API健壮性模块为你的交易机器人提供了企业级的错误处理、速率限制和健康监控能力,确保与Binance API的交互稳定可靠。

---

## ?? 核心功能

### 1?? **指数退避重试**
- ? 自动重试 429 (速率限制) 和 5xx (服务器错误)
- ? 重试延迟: 1s → 2s → 4s
- ? 最多重试 3 次
- ? 用户取消请求不会重试

### 2?? **速率限制保护**
- ? REST API: 1200 weight/分钟 (Binance限制)
- ? 订单API: 300 orders/10秒
- ? 令牌桶算法,自动限流
- ? 超限时自动等待,不抛错

### 3?? **熔断器模式**
- ? 连续失败5次自动熔断
- ? 熔断后拒绝请求1分钟
- ? 自动尝试恢复(半开状态)
- ? 防止雪崩效应

### 4?? **API健康监控**
- ? 追踪成功率/失败率
- ? 响应时间监控(平均/P95/P99)
- ? 慢请求告警(>2秒)
- ? 错误分类统计
- ? 端点级别分析

---

## ?? 使用示例

### 1?? 自动重试(无需修改代码)

```csharp
// 原有代码保持不变,自动获得重试能力
try
{
    var tickers = await ServiceLocator.Api.GetMiniTickersAsync();
    // 如果遇到429或5xx,会自动重试3次
}
catch (InvalidOperationException ex) when (ex.Message.Contains("API熔断"))
{
    // 熔断器已打开,API暂时不可用
    MessageBox.Show("API服务暂时不可用,请稍后重试", "提示");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("速率限制"))
{
    // 已达最大重试次数,速率限制依然存在
    MessageBox.Show("请求过于频繁,请降低频率", "速率限制");
}
catch (HttpRequestException ex)
{
    // 服务器错误(已重试3次仍失败)
    MessageBox.Show($"服务器错误: {ex.Message}", "错误");
}
```

### 2?? 查看API健康状态

```csharp
// 在 DiagnosticsView.xaml.cs 中
private void LoadApiHealth()
{
    try
    {
        // 查看最近5分钟的健康状态
        var report = ServiceLocator.Api.GetHealthReport(TimeSpan.FromMinutes(5));
        
        // 显示关键指标
        HealthStatusText.Text = report.IsHealthy ? "? 健康" : "?? 降级";
        SuccessRateText.Text = $"{report.SuccessRate:P2}";
        AvgLatencyText.Text = $"{report.AverageLatency.TotalMilliseconds:F0}ms";
        P95LatencyText.Text = $"{report.P95Latency.TotalMilliseconds:F0}ms";
        ErrorCountText.Text = report.ErrorCount.ToString();
        SlowRequestCountText.Text = report.SlowRequestCount.ToString();
        
        // 设置颜色
        HealthStatusText.Foreground = report.IsHealthy ? Brushes.Green : Brushes.Orange;
        SuccessRateText.Foreground = report.SuccessRate >= 0.95 ? Brushes.Green : Brushes.Red;
        
        // 显示Top错误
        if (report.TopErrors.Any())
        {
            var errorList = string.Join("\n", report.TopErrors.Select(e => 
                $"? {e.ErrorMessage} (x{e.Count}, 最近: {e.LastOccurrence:HH:mm:ss})"));
            TopErrorsText.Text = errorList;
        }
        else
        {
            TopErrorsText.Text = "无错误";
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show($"加载健康状态失败: {ex.Message}");
    }
}

// 定时刷新
private void StartHealthMonitoring()
{
    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
    timer.Tick += (s, e) => LoadApiHealth();
    timer.Start();
}
```

### 3?? 查看熔断器状态

```csharp
// 显示熔断器状态
private void UpdateCircuitBreakerStatus()
{
    var state = ServiceLocator.Api.CircuitBreakerState;
    
    CircuitBreakerStatusText.Text = state switch
    {
        CircuitBreakerState.Closed => "? 正常",
        CircuitBreakerState.Open => "?? 熔断中",
        CircuitBreakerState.HalfOpen => "?? 恢复测试",
        _ => "未知"
    };
    
    CircuitBreakerStatusText.Foreground = state switch
    {
        CircuitBreakerState.Closed => Brushes.Green,
        CircuitBreakerState.Open => Brushes.Red,
        CircuitBreakerState.HalfOpen => Brushes.Orange,
        _ => Brushes.Gray
    };
}
```

### 4?? 手动速率控制(高级)

```csharp
// 批量操作前检查是否有足够令牌
var rateLimiter = new RateLimiter();

if (rateLimiter.TryConsume("rest_api", tokens: 10))
{
    // 可以立即执行
    await BatchOperationAsync();
}
else
{
    // 需要等待
    await rateLimiter.WaitForRestApiAsync(weight: 10);
    await BatchOperationAsync();
}

// 查看可用令牌数
var available = rateLimiter.GetAvailableTokens("rest_api");
StatusText.Text = $"剩余API配额: {available}/1200";
```

### 5?? 日志查看(启动诊断)

```csharp
// 所有重试/熔断事件都会自动记录到 Data/startup.log

/*
示例日志:
[14:32:15] API RateLimit: /fapi/v1/ticker/24hr, 等待 1s 重试 (attempt 1)
[14:32:16] API ServerError: /fapi/v1/account 503, 等待 2s 重试
[14:32:20] CircuitBreaker: -> Open (连续失败 5 次)
[14:33:20] CircuitBreaker: Open -> HalfOpen (尝试恢复)
[14:33:21] CircuitBreaker: HalfOpen -> Closed (已恢复)
[14:35:10] SlowRequest: /fapi/v1/klines took 3500ms
[14:36:05] FailedRequest: /fapi/v1/order - Insufficient balance (HTTP 400)
*/

// 在UI中显示日志
private async Task LoadRecentLogs()
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "Data", "startup.log");
    if (File.Exists(logPath))
    {
        var lines = await File.ReadAllLinesAsync(logPath);
        var recent = lines.TakeLast(50).Reverse(); // 最近50条,倒序显示
        LogTextBox.Text = string.Join(Environment.NewLine, recent);
    }
}
```

---

## ?? UI 集成示例

### DiagnosticsView.xaml 增强

```xaml
<GroupBox Header="API 健康监控" Margin="0,8,0,0">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- 状态面板 -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="健康状态:" Margin="0,0,8,0"/>
            <TextBlock x:Name="HealthStatusText" FontWeight="Bold"/>
            <TextBlock Text="  |  熔断器:" Margin="16,0,8,0"/>
            <TextBlock x:Name="CircuitBreakerStatusText" FontWeight="Bold"/>
            <TextBlock Text="  |  成功率:" Margin="16,0,8,0"/>
            <TextBlock x:Name="SuccessRateText" FontWeight="Bold"/>
        </StackPanel>
        
        <!-- 延迟指标 -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="平均延迟:" Margin="0,0,8,0"/>
            <TextBlock x:Name="AvgLatencyText"/>
            <TextBlock Text="  |  P95延迟:" Margin="16,0,8,0"/>
            <TextBlock x:Name="P95LatencyText"/>
            <TextBlock Text="  |  慢请求:" Margin="16,0,8,0"/>
            <TextBlock x:Name="SlowRequestCountText"/>
            <TextBlock Text="  |  错误数:" Margin="16,0,8,0"/>
            <TextBlock x:Name="ErrorCountText"/>
        </StackPanel>
        
        <!-- Top错误 -->
        <StackPanel Grid.Row="2">
            <TextBlock Text="Top错误:" FontWeight="Bold" Margin="0,0,0,4"/>
            <TextBlock x:Name="TopErrorsText" TextWrapping="Wrap" FontSize="11" Foreground="#7A869A"/>
        </StackPanel>
        
        <Button Grid.Row="3" Content="刷新健康状态" Click="RefreshHealth_Click" Margin="0,8,0,0"/>
    </Grid>
</GroupBox>
```

### 实时警报

```csharp
// 在主窗口启动时监控API健康
private async Task MonitorApiHealthAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            var report = ServiceLocator.Api.GetHealthReport(TimeSpan.FromMinutes(1));
            
            // 成功率低于90%告警
            if (report.SuccessRate < 0.90)
            {
                await ServiceLocator.Notification.SendTelegramAsync(
                    ServiceLocator.Settings.TelegramBotToken,
                    ServiceLocator.Settings.TelegramChatId,
                    $"?? API健康度降级\n成功率: {report.SuccessRate:P2}\n错误数: {report.ErrorCount}"
                );
            }
            
            // 熔断器打开告警
            if (ServiceLocator.Api.CircuitBreakerState == CircuitBreakerState.Open)
            {
                await ServiceLocator.Notification.SendTelegramAsync(
                    ServiceLocator.Settings.TelegramBotToken,
                    ServiceLocator.Settings.TelegramChatId,
                    "?? API熔断器已触发,交易暂停"
                );
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"HealthMonitor error: {ex.Message}");
        }
        
        await Task.Delay(TimeSpan.FromMinutes(1), ct);
    }
}
```

---

## ?? 性能对比

### 改造前:
```
? 遇到429直接抛错,交易中断
? 服务器5xx错误需要手动重启
? 无速率控制,容易触发限制
? 无健康监控,问题难以排查
? 连续失败导致雪崩
```

### 改造后:
```
? 429自动重试,透明处理
? 5xx自动重试最多3次
? 自动限速,永不超限
? 实时健康监控,可视化展示
? 熔断保护,自动恢复
? 所有事件记录日志,便于调试
```

---

## ?? 高级配置

### 自定义重试策略

```csharp
// 在 BinanceApiClient 构造函数中
private static readonly TimeSpan[] CustomRetryDelays = new[]
{
    TimeSpan.FromMilliseconds(500),  // 快速重试
    TimeSpan.FromSeconds(1),
    TimeSpan.FromSeconds(3),
    TimeSpan.FromSeconds(10)         // 长延迟
};
```

### 自定义熔断器阈值

```csharp
// 更激进的熔断(连续3次失败就熔断)
var circuitBreaker = new ApiCircuitBreaker(
    failureThreshold: 3,
    timeout: TimeSpan.FromSeconds(15),
    cooldown: TimeSpan.FromSeconds(30)
);
```

### 自定义速率限制

```csharp
// 更保守的限速(预留20%缓冲)
var rateLimiter = new RateLimiter();
// 修改为 960/min = 80% of 1200
// 在 RateLimiter.cs 中调整 capacity 参数
```

---

## ? 完成度检查

| 功能 | 状态 |
|------|------|
| 指数退避重试 | ? |
| 429 速率限制重试 | ? |
| 5xx 服务器错误重试 | ? |
| 令牌桶限速 | ? |
| 熔断器保护 | ? |
| 健康监控 | ? |
| 慢请求告警 | ? |
| 错误统计 | ? |
| 日志记录 | ? |
| 自动恢复 | ? |

---

## ?? 下一步

API健壮性已完成!Phase 1 仅剩最后一个模块:

**增强回测引擎** ? 2-3天
- 真实订单撮合逻辑
- 滑点模拟
- 手续费精确计算
- Maker/Taker费率
- 详细绩效指标

需要我继续回测引擎吗?回复 **"继续回测"**!
