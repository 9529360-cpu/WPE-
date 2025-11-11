# 💻 开发者快速参考手册

**目标受众**: 开发者  
**难度**: 中级  
**预计阅读时间**: 15分钟

---

## 📑 目录

- [开发环境设置](#开发环境设置)
- [项目结构详解](#项目结构详解)
- [核心API快速参考](#核心api快速参考)
- [常用代码片段](#常用代码片段)
- [调试技巧](#调试技巧)
- [性能优化建议](#性能优化建议)
- [代码规范](#代码规范)

---

## 开发环境设置

### 必需工具

```bash
# Windows
• Visual Studio 2022 (17.8+)
  - 工作负载: .NET桌面开发
  - 工作负载: .NET Core跨平台开发
• .NET 8.0 SDK
• Git for Windows

# 推荐扩展
• GitHub Copilot
• ReSharper (可选)
• Productivity Power Tools
```

### 快速设置

```bash
# 1. 克隆仓库
git clone https://github.com/9529360-cpu/WPE-.git
cd WPE-

# 2. 还原包
dotnet restore

# 3. 构建
dotnet build

# 4. 运行测试
dotnet test

# 5. 启动应用
dotnet run
```

---

## 项目结构详解

### 分层架构

```
┌─────────────────────────────────────────┐
│           Modules (UI层)                │
│  - 各种WPF UserControl                  │
│  - XAML + Code-behind                   │
└─────────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────────┐
│         Services (服务层)               │
│  - 业务逻辑实现                         │
│  - 与外部系统交互                       │
└─────────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────────┐
│      Application (应用层)               │
│  - 复杂业务流程                         │
│  - 如回测引擎                           │
└─────────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────────┐
│         Core (核心层)                   │
│  - 领域模型和接口                       │
│  - 业务规则                             │
└─────────────────────────────────────────┘
                  ↓
┌─────────────────────────────────────────┐
│    Infrastructure (基础设施层)          │
│  - 外部依赖封装                         │
│  - 数据访问、API客户端                  │
└─────────────────────────────────────────┘
```

### 关键目录

| 目录 | 用途 | 主要文件 |
|------|------|----------|
| `/Application/Backtesting/` | 回测引擎 | EnhancedBacktestEngine.cs |
| `/Core/Abstractions/` | 核心接口 | ITradingStrategy.cs |
| `/Core/Models/` | 领域模型 | TradingModels.cs |
| `/Core/Risk/` | 风险管理 | RiskManager.cs |
| `/Core/Strategies/` | 策略实现 | MomentumStrategy.cs |
| `/Infrastructure/Data/` | 数据访问 | DatabaseDataSource.cs |
| `/Services/AI/` | AI服务 | DeepSeekTradingAgent.cs |
| `/Services/Observability/` | 可观测性 | ObservabilityService.cs |
| `/Modules/` | UI模块 | 各种View.xaml |
| `/Models/` | 数据传输对象 | TradingAccount.cs |
| `/Utils/` | 工具类 | PerformanceUtils.cs |
| `/Tests/` | 单元测试 | *Tests.cs |

---

## 核心API快速参考

### 1. 策略接口

```csharp
// 所有策略必须实现此接口
public interface ITradingStrategy
{
    string Name { get; }
    StrategyParameters Parameters { get; }
    
    void Initialize(IStrategyContext context);
    
    ValueTask<StrategyDecision> EvaluateAsync(
        MarketObservation observation, 
        CancellationToken ct = default);
    
    IAsyncEnumerable<StrategyDecision> RunAsync(
        IAsyncEnumerable<MarketObservation> observations, 
        CancellationToken ct = default);
}
```

### 2. 回测引擎

```csharp
// 使用回测引擎
var engine = new EnhancedBacktestEngine(
    strategy: new MomentumStrategy(...),
    dataSource: dataSource,
    initialCapital: 10000m
);

BacktestResult result = await engine.RunBacktestAsync(
    symbol: "BTCUSDT",
    startDate: DateTime.Now.AddDays(-90),
    endDate: DateTime.Now
);

Console.WriteLine($"总收益: {result.TotalReturn:P2}");
Console.WriteLine($"夏普比: {result.SharpeRatio:F2}");
```

### 3. AI交易Agent

```csharp
// 创建AI Agent
var agent = new DeepSeekTradingAgent("your-api-key");

// 分析市场
string analysis = await agent.AnalyzeMarketAsync(
    symbol: "BTCUSDT",
    timeframe: "1h",
    historyData: marketData
);

// AI对话
string response = await agent.ChatAsync(
    userMessage: "当前应该买入还是卖出？",
    context: "账户净值: 10000 USDT..."
);

// 生成策略
string strategyCode = await agent.GenerateStrategyAsync(
    description: "双均线交叉策略",
    requirements: "适合震荡市"
);
```

### 4. 订单执行

```csharp
// 实盘订单
var liveExecutor = new LiveOrderExecutor(apiClient);

OrderExecutionResult result = await liveExecutor.ExecuteOrderAsync(
    symbol: "BTCUSDT",
    side: "BUY",
    quantity: 0.001m,
    orderType: "LIMIT",
    price: 50000m
);

// 模拟订单
var simulatedExecutor = new SimulatedOrderExecutor(accountManager);

result = await simulatedExecutor.ExecuteOrderAsync(
    symbol: "BTCUSDT",
    side: "SELL",
    quantity: 0.001m,
    orderType: "MARKET"
);
```

### 5. 风险管理

```csharp
// 创建风险管理器
var riskManager = new RiskManager(new[]
{
    new DailyLossLimitRule(maxLoss: 500m),
    new TrailingStopLossRule(trailPercent: 0.05),
    new TimeBasedExitRule(maxHoldingHours: 24)
});

// 评估订单风险
bool approved = riskManager.Approve(tradeAction);

if (!approved)
{
    Console.WriteLine("订单被风险管理器拒绝");
}

// 获取风险指标
RiskMetrics metrics = await riskEngine.GenerateReportAsync(...);
Console.WriteLine($"VaR: {metrics.ValueAtRisk}");
```

### 6. 可观测性

```csharp
// 创建可观测性服务
var observability = new ObservabilityService("MyComponent");

// 追踪操作
using (var trace = observability.StartTrace("PlaceOrder"))
{
    // 执行操作
    await PlaceOrderAsync();
    
    // 记录指标
    observability.RecordHistogram("order_latency_ms", latency);
}

// 记录日志
observability.LogInfo("订单已执行", new { orderId, quantity });

// 触发告警
await observability.TriggerAlert(
    "HighLatency",
    AlertSeverity.Warning,
    "订单延迟超过阈值"
);
```

---

## 常用代码片段

### 1. 创建新策略

```csharp
public class MyCustomStrategy : ITradingStrategy
{
    public string Name => "My Custom Strategy";
    public StrategyParameters Parameters { get; }
    
    private IStrategyContext? _context;

    public MyCustomStrategy(StrategyParameters parameters)
    {
        Parameters = parameters;
    }

    public void Initialize(IStrategyContext context)
    {
        _context = context;
    }

    public async ValueTask<StrategyDecision> EvaluateAsync(
        MarketObservation observation, 
        CancellationToken ct = default)
    {
        // 1. 提取参数
        double threshold = Parameters.Get("threshold", 0.5);
        
        // 2. 计算指标
        double signal = CalculateSignal(observation);
        
        // 3. 生成决策
        TradeActionType action = signal > threshold 
            ? TradeActionType.EnterLong 
            : TradeActionType.Hold;
        
        double confidence = Math.Abs(signal);
        
        // 4. 风险检查
        var tradeAction = new TradeAction(action, 1.0, "Signal based");
        if (!_context.RiskManager.Approve(tradeAction))
        {
            tradeAction = new TradeAction(TradeActionType.Hold, 0, "Risk rejected");
        }
        
        // 5. 返回决策
        return new StrategyDecision(
            tradeAction, 
            confidence, 
            new CompositeSignal(), 
            new MachineLearningSignal(...)
        );
    }

    public async IAsyncEnumerable<StrategyDecision> RunAsync(
        IAsyncEnumerable<MarketObservation> observations,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var obs in observations.WithCancellation(ct))
        {
            yield return await EvaluateAsync(obs, ct);
        }
    }

    private double CalculateSignal(MarketObservation obs)
    {
        // 策略逻辑
        return 0.0;
    }
}
```

### 2. 添加新的技术指标

```csharp
public static class TechnicalIndicators
{
    /// <summary>
    /// 计算RSI
    /// </summary>
    public static double CalculateRSI(IEnumerable<double> prices, int period = 14)
    {
        var priceList = prices.ToList();
        if (priceList.Count < period + 1)
            return 50; // 默认中性值

        double avgGain = 0;
        double avgLoss = 0;

        // 计算初始平均值
        for (int i = 1; i <= period; i++)
        {
            double change = priceList[i] - priceList[i - 1];
            if (change > 0)
                avgGain += change;
            else
                avgLoss += Math.Abs(change);
        }

        avgGain /= period;
        avgLoss /= period;

        // 计算RSI
        if (avgLoss == 0)
            return 100;

        double rs = avgGain / avgLoss;
        double rsi = 100 - (100 / (1 + rs));

        return rsi;
    }
}
```

### 3. 异步数据处理

```csharp
public class RealtimeDataProcessor
{
    private readonly Channel<MarketData> _channel;

    public RealtimeDataProcessor()
    {
        _channel = Channel.CreateUnbounded<MarketData>();
    }

    // 生产者
    public async Task ProduceAsync(MarketData data)
    {
        await _channel.Writer.WriteAsync(data);
    }

    // 消费者
    public async Task ProcessAsync(CancellationToken ct)
    {
        await foreach (var data in _channel.Reader.ReadAllAsync(ct))
        {
            // 处理数据
            await ProcessMarketDataAsync(data);
        }
    }

    private async Task ProcessMarketDataAsync(MarketData data)
    {
        // 处理逻辑
        await Task.CompletedTask;
    }
}
```

### 4. 错误处理和重试

```csharp
public async Task<T> ExecuteWithRetryAsync<T>(
    Func<Task<T>> operation,
    int maxRetries = 3,
    TimeSpan? delay = null)
{
    delay ??= TimeSpan.FromSeconds(1);
    
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (i < maxRetries - 1)
        {
            LogService.Warning($"操作失败，重试 {i + 1}/{maxRetries}: {ex.Message}");
            await Task.Delay(delay.Value);
        }
    }
    
    // 最后一次尝试，不捕获异常
    return await operation();
}
```

---

## 调试技巧

### 1. 日志配置

```csharp
// 设置日志级别
LogService.SetMinimumLevel(LogLevel.Debug);

// 记录不同级别日志
LogService.Debug("详细调试信息");
LogService.Info("一般信息");
LogService.Warning("警告信息");
LogService.Error(ex, "错误信息");
```

### 2. 断点调试

```
关键位置设置断点:
1. 策略的 EvaluateAsync 方法
2. 订单执行前后
3. 风险检查逻辑
4. API调用位置
```

### 3. 性能分析

```csharp
// 使用性能工具
using (var operation = _performanceService.RecordOperation("OrderExecution"))
{
    await ExecuteOrderAsync();
    
    // 记录自定义指标
    operation.AddMetric("quantity", quantity);
    operation.AddMetric("latency_ms", latency);
}
```

### 4. 单元测试

```csharp
[Fact]
public async Task TestStrategyEvaluation()
{
    // Arrange
    var strategy = new MyStrategy(...);
    var observation = CreateTestObservation();
    
    // Act
    var decision = await strategy.EvaluateAsync(observation);
    
    // Assert
    Assert.NotEqual(TradeActionType.Hold, decision.TradeAction.Type);
    Assert.InRange(decision.Confidence, 0, 1);
}
```

---

## 性能优化建议

### 1. 使用对象池

```csharp
// 避免频繁分配
private static readonly ObjectPool<StringBuilder> _stringBuilderPool 
    = ObjectPool.Create<StringBuilder>();

public string BuildMessage()
{
    var sb = _stringBuilderPool.Get();
    try
    {
        sb.Clear();
        sb.Append("Message");
        return sb.ToString();
    }
    finally
    {
        _stringBuilderPool.Return(sb);
    }
}
```

### 2. 缓存策略

```csharp
// 使用智能缓存
var result = await _cacheManager.GetOrCreateCachedAsync(
    key: "market_data_BTCUSDT",
    factory: async () => await FetchMarketDataAsync(),
    expiration: TimeSpan.FromMinutes(5)
);
```

### 3. 并行处理

```csharp
// 并行处理多个交易对
var tasks = symbols.Select(symbol => 
    ProcessSymbolAsync(symbol, ct)
);

await Task.WhenAll(tasks);
```

### 4. 避免阻塞

```csharp
// ❌ 错误 - 阻塞UI线程
var result = SomeAsyncMethod().Result;

// ✅ 正确 - 异步等待
var result = await SomeAsyncMethod();
```

---

## 代码规范

### 命名约定

```csharp
// 类名: PascalCase
public class TradingStrategy { }

// 接口: I + PascalCase
public interface ITradingStrategy { }

// 方法: PascalCase
public async Task ProcessAsync() { }

// 私有字段: _camelCase
private readonly ILogger _logger;

// 参数: camelCase
public void Execute(string symbol, decimal quantity) { }

// 常量: UPPER_SNAKE_CASE
private const int MAX_RETRIES = 3;
```

### 注释规范

```csharp
/// <summary>
/// 执行交易订单
/// </summary>
/// <param name="symbol">交易对符号</param>
/// <param name="quantity">交易数量</param>
/// <param name="ct">取消令牌</param>
/// <returns>订单执行结果</returns>
/// <exception cref="ArgumentException">当参数无效时</exception>
public async Task<OrderResult> ExecuteAsync(
    string symbol, 
    decimal quantity, 
    CancellationToken ct)
{
    // 验证参数
    ArgumentNullException.ThrowIfNull(symbol);
    
    // 执行逻辑
    return await ...;
}
```

### 异步模式

```csharp
// ✅ 推荐: 使用 Async 后缀
public async Task<Result> ProcessAsync(CancellationToken ct)
{
    // 传递取消令牌
    return await PerformOperationAsync(ct);
}

// ✅ 支持取消
public async Task LongRunningAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await DoWorkAsync();
    }
}
```

### 错误处理

```csharp
// ✅ 具体异常
throw new ArgumentException("Invalid symbol", nameof(symbol));

// ✅ 记录并重新抛出
catch (Exception ex)
{
    LogService.Error(ex, "Operation failed");
    throw;
}

// ✅ 使用自定义异常
public class TradingException : Exception
{
    public TradingException(string message) : base(message) { }
}
```

---

## 快速命令参考

```bash
# 构建
dotnet build
dotnet build --configuration Release

# 运行
dotnet run
dotnet run --configuration Release

# 测试
dotnet test
dotnet test --logger "console;verbosity=detailed"

# 清理
dotnet clean

# 发布
dotnet publish --configuration Release --output ./publish

# 代码格式化
dotnet format
```

---

## 有用的链接

| 资源 | 链接 |
|------|------|
| .NET 文档 | https://docs.microsoft.com/dotnet/ |
| C# 指南 | https://docs.microsoft.com/dotnet/csharp/ |
| xUnit 文档 | https://xunit.net/ |
| ScottPlot 文档 | https://scottplot.net/ |
| Binance API | https://binance-docs.github.io/ |
| DeepSeek API | https://platform.deepseek.com/api-docs |

---

**快速参考手册更新**: 2025-01-XX  
**适用版本**: v1.0.0

祝编码愉快！ 💻✨
