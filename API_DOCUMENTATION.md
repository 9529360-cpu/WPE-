# API 文档 (API Documentation)

本文档描述了币安量化交易机器人的核心接口和扩展点。

This document describes the core interfaces and extension points of the Binance Quantitative Trading Bot.

## 📋 目录 (Table of Contents)

- [核心接口](#核心接口-core-interfaces)
- [策略开发](#策略开发-strategy-development)
- [数据源扩展](#数据源扩展-data-source-extension)
- [风险规则](#风险规则-risk-rules)
- [特征工程](#特征工程-feature-engineering)
- [事件系统](#事件系统-event-system)

## 🎯 核心接口 (Core Interfaces)

### ITradingStrategy

交易策略的核心接口。

```csharp
namespace 币安量化机器人.Core.Abstractions;

public interface ITradingStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 策略参数
    /// </summary>
    StrategyParameters Parameters { get; }
    
    /// <summary>
    /// 初始化策略
    /// </summary>
    void Initialize(IStrategyContext context);
    
    /// <summary>
    /// 评估市场观察并生成交易决策
    /// </summary>
    /// <param name="observation">市场观察数据</param>
    /// <returns>交易决策</returns>
    ValueTask<StrategyDecision> EvaluateAsync(MarketObservation observation);
    
    /// <summary>
    /// 运行策略（流式处理）
    /// </summary>
    /// <param name="observations">市场观察流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>决策流</returns>
    IAsyncEnumerable<StrategyDecision> RunAsync(
        IAsyncEnumerable<MarketObservation> observations,
        CancellationToken cancellationToken = default);
}
```

### IMarketDataService

市场数据服务接口。

```csharp
namespace 币安量化机器人.Core.Abstractions;

public interface IMarketDataService
{
    /// <summary>
    /// 获取实时市场数据流
    /// </summary>
    /// <param name="symbol">交易对</param>
    /// <param name="timeframes">时间周期列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>市场观察流</returns>
    IAsyncEnumerable<MarketObservation> StreamAsync(
        string symbol,
        IEnumerable<TimeSpan> timeframes,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取历史数据序列
    /// </summary>
    /// <param name="symbol">交易对</param>
    /// <param name="timeframe">时间周期</param>
    /// <param name="start">开始时间</param>
    /// <param name="end">结束时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>时间序列数据</returns>
    ValueTask<TimeframeSeries> GetSeriesAsync(
        string symbol,
        TimeSpan timeframe,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken = default);
}
```

### IRiskManager

风险管理接口。

```csharp
namespace 币安量化机器人.Core.Risk;

public interface IRiskManager
{
    /// <summary>
    /// 风险触发事件
    /// </summary>
    event EventHandler<RiskEvent> RiskTriggered;
    
    /// <summary>
    /// 更新仓位快照并评估风险
    /// </summary>
    /// <param name="snapshot">仓位快照</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask UpdateAsync(
        PositionSnapshot snapshot,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 检查交易行动是否被允许
    /// </summary>
    /// <param name="action">交易行动</param>
    /// <returns>是否允许</returns>
    ValueTask<bool> IsActionAllowedAsync(TradeAction action);
}
```

## 🤖 策略开发 (Strategy Development)

### 创建自定义策略

#### 步骤 1: 实现 ITradingStrategy

```csharp
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;

public class MyCustomStrategy : ITradingStrategy
{
    private readonly IMultiTimeframeAnalyzer _analyzer;
    private readonly IMachineLearningSignalGenerator _mlGenerator;
    private IStrategyContext _context;
    
    public string Name => "My Custom Strategy";
    public StrategyParameters Parameters { get; }
    
    public MyCustomStrategy(
        IMultiTimeframeAnalyzer analyzer,
        IMachineLearningSignalGenerator mlGenerator,
        StrategyParameters parameters)
    {
        _analyzer = analyzer;
        _mlGenerator = mlGenerator;
        Parameters = parameters;
    }
    
    public void Initialize(IStrategyContext context)
    {
        _context = context;
    }
    
    public async ValueTask<StrategyDecision> EvaluateAsync(
        MarketObservation observation)
    {
        // 1. 多时间框架分析
        var mtfSignal = await _analyzer.AnalyzeAsync(
            observation.Symbol,
            observation.Timeframe
        );
        
        // 2. 获取ML信号
        var mlSignal = await _mlGenerator.GenerateSignalAsync(
            observation.Symbol,
            observation.Timeframe
        );
        
        // 3. 结合信号生成决策
        var action = DetermineAction(observation, mtfSignal, mlSignal);
        var confidence = CalculateConfidence(mtfSignal, mlSignal);
        
        return new StrategyDecision(
            action,
            confidence,
            mlSignal,
            $"MTF: {mtfSignal}, ML: {mlSignal}"
        );
    }
    
    public async IAsyncEnumerable<StrategyDecision> RunAsync(
        IAsyncEnumerable<MarketObservation> observations,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var observation in observations.WithCancellation(cancellationToken))
        {
            var decision = await EvaluateAsync(observation);
            yield return decision;
        }
    }
    
    private TradeAction DetermineAction(
        MarketObservation observation,
        double mtfSignal,
        double mlSignal)
    {
        // 实现你的策略逻辑
        var combinedSignal = (mtfSignal + mlSignal) / 2;
        
        if (combinedSignal > Parameters.Get("buy_threshold", 0.6))
        {
            var quantity = Parameters.Get("base_quantity", 1.0);
            return TradeAction.Buy(quantity, observation.Close);
        }
        
        if (combinedSignal < Parameters.Get("sell_threshold", -0.6))
        {
            var quantity = Parameters.Get("base_quantity", 1.0);
            return TradeAction.Sell(quantity, observation.Close);
        }
        
        return TradeAction.Hold();
    }
    
    private double CalculateConfidence(double mtfSignal, double mlSignal)
    {
        // 计算置信度
        var agreement = 1.0 - Math.Abs(mtfSignal - mlSignal);
        var strength = (Math.Abs(mtfSignal) + Math.Abs(mlSignal)) / 2;
        return agreement * strength;
    }
}
```

#### 步骤 2: 注册策略

```csharp
// 在 ServiceLocator 或 DI 容器中注册
public class StrategyFactory
{
    public ITradingStrategy CreateStrategy(string strategyType, StrategyParameters parameters)
    {
        return strategyType switch
        {
            "MeanReversion" => new MeanReversionStrategy(/* dependencies */, parameters),
            "Momentum" => new MomentumStrategy(/* dependencies */, parameters),
            "MyCustom" => new MyCustomStrategy(/* dependencies */, parameters),
            _ => throw new ArgumentException($"Unknown strategy: {strategyType}")
        };
    }
}
```

### 策略参数

使用 `StrategyParameters` 管理策略参数：

```csharp
var parameters = new StrategyParameters(new Dictionary<string, double>
{
    ["buy_threshold"] = 0.6,
    ["sell_threshold"] = -0.6,
    ["base_quantity"] = 1.0,
    ["stop_loss_percent"] = 0.02,
    ["take_profit_percent"] = 0.05
});

// 访问参数
var buyThreshold = parameters.Get("buy_threshold", 0.5); // 默认值0.5
```

## 📊 数据源扩展 (Data Source Extension)

### IDataSource 接口

```csharp
namespace 币安量化机器人.Core.Data;

public interface IDataSource
{
    /// <summary>
    /// 数据源名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 读取数据
    /// </summary>
    /// <param name="query">数据查询</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>数据帧</returns>
    ValueTask<DataFrame> ReadAsync(
        DataQuery query,
        CancellationToken cancellationToken = default);
}
```

### 实现自定义数据源

```csharp
public class CsvDataSource : IDataSource
{
    private readonly string _dataDirectory;
    
    public string Name => "CSV File Source";
    
    public CsvDataSource(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }
    
    public async ValueTask<DataFrame> ReadAsync(
        DataQuery query,
        CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(
            _dataDirectory,
            $"{query.Symbol}_{query.Timeframe}.csv"
        );
        
        if (!File.Exists(filePath))
            return DataFrame.Empty;
        
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var dataPoints = new List<DataPoint>();
        
        foreach (var line in lines.Skip(1)) // Skip header
        {
            var parts = line.Split(',');
            dataPoints.Add(new DataPoint
            {
                Timestamp = DateTime.Parse(parts[0]),
                Open = double.Parse(parts[1]),
                High = double.Parse(parts[2]),
                Low = double.Parse(parts[3]),
                Close = double.Parse(parts[4]),
                Volume = double.Parse(parts[5])
            });
        }
        
        return new DataFrame(query.Symbol, query.Timeframe, dataPoints);
    }
}
```

### 注册数据源

```csharp
// 在 RealTimeDataPipeline 中添加
var pipeline = new RealTimeDataPipeline(
    sources: new IDataSource[]
    {
        new DatabaseDataSource(connectionString),
        new ApiDataSource(httpClient, apiUrl),
        new CsvDataSource(csvDirectory), // 你的自定义数据源
        new FileDataSource(importPath)
    },
    qualityRules: qualityRules,
    engineers: engineers,
    featureStore: featureStore
);
```

## ⚠️ 风险规则 (Risk Rules)

### IRiskRule 接口

```csharp
namespace 币安量化机器人.Core.Risk;

public interface IRiskRule
{
    /// <summary>
    /// 规则名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 评估风险
    /// </summary>
    /// <param name="snapshot">仓位快照</param>
    /// <returns>风险评估结果</returns>
    ValueTask<RiskRuleResult> EvaluateAsync(PositionSnapshot snapshot);
}
```

### 实现自定义风险规则

```csharp
public class MaxLeverageRule : IRiskRule
{
    private readonly double _maxLeverage;
    
    public string Name => "Maximum Leverage Rule";
    
    public MaxLeverageRule(double maxLeverage = 3.0)
    {
        _maxLeverage = maxLeverage;
    }
    
    public ValueTask<RiskRuleResult> EvaluateAsync(PositionSnapshot snapshot)
    {
        // 计算当前杠杆
        var currentLeverage = snapshot.TotalPositionValue / snapshot.AccountEquity;
        
        if (currentLeverage > _maxLeverage)
        {
            return ValueTask.FromResult(RiskRuleResult.Failure(
                Name,
                $"Current leverage {currentLeverage:F2}x exceeds maximum {_maxLeverage:F2}x",
                RiskSeverity.High
            ));
        }
        
        return ValueTask.FromResult(RiskRuleResult.Success(Name));
    }
}
```

### 添加风险规则到 RiskManager

```csharp
var riskManager = new RiskManager();

// 添加内置规则
riskManager.AddRule(new DynamicStopLossRule(0.02));
riskManager.AddRule(new MaxDrawdownRule(0.10));
riskManager.AddRule(new MaxPositionRule(0.20));

// 添加自定义规则
riskManager.AddRule(new MaxLeverageRule(3.0));
```

## 🔧 特征工程 (Feature Engineering)

### IFeatureEngineer 接口

```csharp
namespace 币安量化机器人.Core.Data;

public interface IFeatureEngineer
{
    /// <summary>
    /// 特征工程名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 转换数据并生成特征
    /// </summary>
    /// <param name="input">输入数据帧</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含特征的数据帧</returns>
    ValueTask<DataFrame> TransformAsync(
        DataFrame input,
        CancellationToken cancellationToken = default);
}
```

### 实现自定义特征工程

```csharp
public class CustomFeatureEngineer : IFeatureEngineer
{
    public string Name => "Custom Feature Engineer";
    
    public ValueTask<DataFrame> TransformAsync(
        DataFrame input,
        CancellationToken cancellationToken = default)
    {
        var enriched = input.Clone();
        
        // 添加价格变化率特征
        enriched.AddFeature("price_change_pct", CalculatePriceChangePct(input));
        
        // 添加波动率特征
        enriched.AddFeature("volatility", CalculateVolatility(input, window: 20));
        
        // 添加趋势强度特征
        enriched.AddFeature("trend_strength", CalculateTrendStrength(input));
        
        return ValueTask.FromResult(enriched);
    }
    
    private double[] CalculatePriceChangePct(DataFrame data)
    {
        var changes = new double[data.Length];
        for (int i = 1; i < data.Length; i++)
        {
            changes[i] = (data.Close[i] - data.Close[i - 1]) / data.Close[i - 1];
        }
        return changes;
    }
    
    private double[] CalculateVolatility(DataFrame data, int window)
    {
        var volatility = new double[data.Length];
        for (int i = window; i < data.Length; i++)
        {
            var returns = new double[window];
            for (int j = 0; j < window; j++)
            {
                returns[j] = (data.Close[i - j] - data.Close[i - j - 1]) / data.Close[i - j - 1];
            }
            volatility[i] = StandardDeviation(returns);
        }
        return volatility;
    }
    
    private double[] CalculateTrendStrength(DataFrame data)
    {
        // 实现趋势强度计算
        // 例如：使用线性回归斜率
        // ...
        return new double[data.Length];
    }
    
    private double StandardDeviation(double[] values)
    {
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / values.Length);
    }
}
```

## 📡 事件系统 (Event System)

### 监控事件

```csharp
// 订阅风险事件
ServiceLocator.AdvancedRisk.RiskTriggered += (sender, riskEvent) =>
{
    Console.WriteLine($"Risk Alert: {riskEvent.Message}");
    
    if (riskEvent.Severity == RiskSeverity.Critical)
    {
        // 暂停所有交易
        StopAllTrading();
    }
};

// 订阅交易事件
ServiceLocator.MonitoringHub.OnTradeExecuted += (sender, trade) =>
{
    Console.WriteLine($"Trade executed: {trade.Symbol} {trade.Side} {trade.Quantity}");
};
```

### 自定义事件

```csharp
public class MyStrategy : ITradingStrategy
{
    public event EventHandler<StrategyEvent> StrategyEvent;
    
    public async ValueTask<StrategyDecision> EvaluateAsync(MarketObservation observation)
    {
        // 策略逻辑
        var decision = /* ... */;
        
        // 触发自定义事件
        StrategyEvent?.Invoke(this, new StrategyEvent
        {
            Type = "SignalGenerated",
            Data = decision,
            Timestamp = DateTime.UtcNow
        });
        
        return decision;
    }
}
```

## 📝 最佳实践

### 1. 异步编程

```csharp
// ✅ 好的做法
public async ValueTask<Result> ProcessDataAsync()
{
    var data = await FetchDataAsync();
    var processed = await ProcessAsync(data);
    return processed;
}

// ❌ 避免阻塞
public Result ProcessData()
{
    var data = FetchDataAsync().Result; // 可能死锁
    return data;
}
```

### 2. 错误处理

```csharp
public async ValueTask<StrategyDecision> EvaluateAsync(MarketObservation observation)
{
    try
    {
        // 策略逻辑
        return await PerformEvaluationAsync(observation);
    }
    catch (DataException ex)
    {
        _logger.LogWarning(ex, "Data quality issue, using fallback");
        return TradeAction.Hold().ToDecision();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error in strategy evaluation");
        throw; // 重新抛出未预期的错误
    }
}
```

### 3. 资源管理

```csharp
public class MyDataSource : IDataSource, IDisposable
{
    private readonly HttpClient _httpClient;
    
    public MyDataSource()
    {
        _httpClient = new HttpClient();
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
```

## 🔗 相关文档

- [用户指南](USER_GUIDE.md) - 终端用户使用指南
- [贡献指南](CONTRIBUTING.md) - 开发者贡献指南
- [架构文档](Docs/Architecture.md) - 系统架构详解

---

**版本**: 1.0.0  
**最后更新**: 2024-11-17

需要帮助？请查看 [GitHub Discussions](https://github.com/9529360-cpu/WPE-/discussions)
