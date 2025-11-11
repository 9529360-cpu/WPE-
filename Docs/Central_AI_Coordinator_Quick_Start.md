# 中央AI协调器 - 快速入门指南

## 🎯 5分钟快速上手

### 第一步：配置API密钥

在 `appsettings.json` 中配置：

```json
{
  "Api": {
    "DeepSeek": {
      "ApiKey": "sk-your-deepseek-api-key"
    },
    "Binance": {
      "ApiKey": "your-binance-api-key",
      "SecretKey": "your-binance-secret-key"
    }
  }
}
```

或者设置环境变量：

```bash
# Windows PowerShell
$env:DEEPSEEK_API_KEY="sk-your-deepseek-api-key"
$env:BINANCE_API_KEY="your-binance-api-key"
$env:BINANCE_SECRET_KEY="your-binance-secret-key"
```

### 第二步：启动协调器

```csharp
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

// 1. 获取中央AI协调器
var coordinator = ServiceLocator.GetAICentralCoordinator();

// 2. 启动主控制循环
await coordinator.StartAsync();

Console.WriteLine("✅ 中央AI协调器已启动！");
Console.WriteLine($"当前阶段: {coordinator.CurrentStage.GetDisplayName()}");
```

### 第三步：监控运行状态

```csharp
// 实时查看系统状态
while (coordinator.IsRunning)
{
    var state = coordinator.CurrentState;
    
    Console.WriteLine($"""
        📊 系统状态
        ├─ 阶段: {state.CurrentStage.GetIcon()} {state.CurrentStage.GetDisplayName()}
        ├─ 市场: 波动率={state.MarketCondition.Volatility:P0}, 趋势={state.MarketCondition.TrendDirection}
        ├─ 账户: 净值={state.AccountStatus.NetValue:N2} USDT, 盈亏={state.AccountStatus.TodayPnL:+N2;-N2}
        ├─ 风险: 回撤={state.RiskMetrics.MaxDrawdown:P2}, 安全={state.RiskMetrics.IsSafe}
        └─ 策略: 表现={state.StrategyStatus.PerformanceScore:P0}
        """);
    
    await Task.Delay(TimeSpan.FromSeconds(10));
}
```

### 第四步：订阅关键事件

```csharp
// 获取事件总线
var eventBus = coordinator._eventBus;

// 监听阶段切换
eventBus.Subscribe<StageTransitionEvent>(async evt =>
{
    Console.WriteLine($"🔄 阶段切换: {evt.FromStage.GetDisplayName()} → {evt.ToStage.GetDisplayName()}");
    
    if (evt.ToStage == WorkflowStage.Live)
    {
        Console.WriteLine("⚠️ 注意：系统已进入实盘交易模式！");
    }
});

// 监听风险警报
eventBus.Subscribe<RiskAlertEvent>(async evt =>
{
    Console.WriteLine($"🚨 风险警报: {evt.Message} (严重程度: {evt.Severity})");
});

// 监听回测完成
eventBus.Subscribe<BacktestCompletedEvent>(async evt =>
{
    Console.WriteLine($"""
        📊 回测完成: {evt.StrategyName}
        ├─ 总收益: {evt.TotalReturn:P2}
        ├─ 夏普比率: {evt.SharpeRatio:F2}
        ├─ 最大回撤: {evt.MaxDrawdown:P2}
        └─ 胜率: {evt.WinRate:P2}
        """);
});
```

## 🎮 完整示例程序

创建 `Program.cs`：

```csharp
using System;
using System.Threading.Tasks;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🤖 启动中央AI协调器...");
        
        try
        {
            // 1. 获取协调器
            var coordinator = ServiceLocator.GetAICentralCoordinator();
            
            // 2. 订阅事件
            SubscribeToEvents(coordinator);
            
            // 3. 启动协调器
            await coordinator.StartAsync();
            
            Console.WriteLine("✅ 系统正在运行，按 Ctrl+C 停止...");
            
            // 4. 等待用户中断
            Console.CancelKeyPress += async (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n🛑 正在停止系统...");
                await coordinator.StopAsync();
                Console.WriteLine("✅ 系统已安全停止");
            };
            
            // 5. 定期显示状态
            while (coordinator.IsRunning)
            {
                DisplayStatus(coordinator);
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 系统异常: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
    
    static void SubscribeToEvents(AICentralCoordinator coordinator)
    {
        var eventBus = coordinator._eventBus;
        
        // 阶段切换
        eventBus.Subscribe<StageTransitionEvent>(async evt =>
        {
            Console.WriteLine($"\n🔄 [{DateTime.Now:HH:mm:ss}] 阶段切换: {evt.FromStage.GetDisplayName()} → {evt.ToStage.GetDisplayName()}");
        });
        
        // 风险警报
        eventBus.Subscribe<RiskAlertEvent>(async evt =>
        {
            var color = evt.Severity switch
            {
                RiskSeverity.Critical => ConsoleColor.Red,
                RiskSeverity.High => ConsoleColor.Yellow,
                _ => ConsoleColor.White
            };
            
            Console.ForegroundColor = color;
            Console.WriteLine($"\n🚨 [{DateTime.Now:HH:mm:ss}] 风险警报: {evt.Message}");
            Console.ResetColor();
        });
        
        // 回测完成
        eventBus.Subscribe<BacktestCompletedEvent>(async evt =>
        {
            Console.WriteLine($"""
                
                📊 [{DateTime.Now:HH:mm:ss}] 回测完成
                ├─ 策略: {evt.StrategyName}
                ├─ 总收益: {evt.TotalReturn:P2}
                ├─ 夏普比率: {evt.SharpeRatio:F2}
                ├─ 最大回撤: {evt.MaxDrawdown:P2}
                ├─ 胜率: {evt.WinRate:P2}
                └─ 交易次数: {evt.TotalTrades}
                """);
        });
        
        // 模拟交易更新
        eventBus.Subscribe<SimulationUpdateEvent>(async evt =>
        {
            Console.WriteLine($"""
                
                🎮 [{DateTime.Now:HH:mm:ss}] 模拟交易更新
                ├─ 当前余额: {evt.CurrentBalance:N2} USDT
                ├─ 盈利: {evt.ProfitPercent:P2}
                ├─ 最大回撤: {evt.MaxDrawdown:P2}
                ├─ 胜率: {evt.WinRate:P2}
                └─ 交易次数: {evt.TotalTrades}
                """);
        });
        
        // 实盘交易
        eventBus.Subscribe<LiveTradeEvent>(async evt =>
        {
            Console.WriteLine($"""
                
                💰 [{DateTime.Now:HH:mm:ss}] 实盘交易
                ├─ 交易对: {evt.Symbol}
                ├─ 动作: {evt.Action}
                ├─ 价格: {evt.Price:F4}
                ├─ 数量: {evt.Quantity:F4}
                └─ 今日亏损: {evt.TodayLoss:N2} USDT
                """);
        });
    }
    
    static void DisplayStatus(AICentralCoordinator coordinator)
    {
        var state = coordinator.CurrentState;
        
        Console.WriteLine($"""
            
            ═══════════════════════════════════════════════════════
            📊 系统状态 [{DateTime.Now:HH:mm:ss}]
            ═══════════════════════════════════════════════════════
            
            🎯 工作流阶段
            ├─ 当前阶段: {state.CurrentStage.GetIcon()} {state.CurrentStage.GetDisplayName()}
            └─ 阶段描述: {state.CurrentStage.GetDescription()}
            
            📈 市场状况
            ├─ 波动率: {state.MarketCondition.Volatility:P2}
            ├─ 趋势: {state.MarketCondition.TrendDirection}
            ├─ 流动性: {state.MarketCondition.Liquidity:P2}
            └─ 市场状态: {(state.MarketCondition.IsStable ? "✅ 平稳" : "⚠️ 波动")}
            
            💰 账户状态
            ├─ 类型: {state.AccountStatus.Type}
            ├─ 净值: {state.AccountStatus.NetValue:N2} USDT
            ├─ 可用余额: {state.AccountStatus.AvailableBalance:N2} USDT
            ├─ 持仓价值: {state.AccountStatus.PositionValue:N2} USDT
            ├─ 今日盈亏: {state.AccountStatus.TodayPnL:+N2;-N2} USDT ({state.AccountStatus.TodayReturnPercent:+P2;-P2})
            ├─ 总盈亏: {state.AccountStatus.TotalPnL:+N2;-N2} USDT ({state.AccountStatus.TotalReturnPercent:+P2;-P2})
            ├─ 胜率: {state.AccountStatus.WinRate:P2}
            └─ 持仓数: {state.AccountStatus.OpenPositionCount}
            
            🎯 策略状态
            ├─ 激活: {(state.StrategyStatus.IsActive ? "✅ 是" : "❌ 否")}
            ├─ 表现评分: {state.StrategyStatus.PerformanceScore:P2}
            ├─ 信号数量: {state.StrategyStatus.RecentSignalCount}
            ├─ 平均信心度: {state.StrategyStatus.AverageConfidence:P2}
            └─ 状态: {(state.StrategyStatus.IsPerformingWell ? "✅ 良好" : "⚠️ 需优化")}
            
            ⚠️ 风险指标
            ├─ 最大回撤: {state.RiskMetrics.MaxDrawdown:P2}
            ├─ 当前回撤: {state.RiskMetrics.CurrentDrawdown:P2}
            ├─ 今日亏损: {state.RiskMetrics.DailyLoss:N2} USDT
            ├─ 杠杆倍数: {state.RiskMetrics.Leverage:F1}x
            └─ 安全性: {(state.RiskMetrics.IsSafe ? "✅ 安全" : "🚨 警报")}
            
            🖥️ 系统资源
            ├─ CPU使用率: {state.SystemResources.CpuUsage:P2}
            ├─ 内存使用率: {state.SystemResources.MemoryUsage:P2}
            ├─ 网络延迟: {state.SystemResources.NetworkLatency:F0}ms
            └─ 健康状态: {(state.SystemResources.IsHealthy ? "✅ 健康" : "⚠️ 异常")}
            
            ═══════════════════════════════════════════════════════
            """);
    }
}
```

## 🔧 高级配置

### 调整控制循环间隔

在 `AICentralCoordinator.cs` 中修改：

```csharp
private TimeSpan GetControlLoopInterval()
{
    return _stateManager.CurrentStage switch
    {
        WorkflowStage.Backtest => TimeSpan.FromSeconds(60),     // 回测：1分钟
        WorkflowStage.Simulation => TimeSpan.FromSeconds(20),   // 模拟：20秒
        WorkflowStage.Live => TimeSpan.FromSeconds(10),         // 实盘：10秒
        WorkflowStage.Optimization => TimeSpan.FromMinutes(2),  // 优化：2分钟
        _ => TimeSpan.FromMinutes(1)
    };
}
```

### 调整回测评估标准

在 `DecisionEngine.cs` 中修改：

```csharp
public BacktestEvaluation EvaluateBacktestResult(BacktestCompletedEvent evt)
{
    // 调整评估标准
    var minSharpeRatio = 1.2;      // 降低夏普比率要求（原1.5）
    var maxDrawdown = 0.20;        // 提高回撤容忍度（原0.15）
    var minWinRate = 0.50;         // 降低胜率要求（原0.55）
    var minTotalReturn = 0.15;     // 降低收益要求（原0.20）
    
    // ...其余代码
}
```

### 调整模拟交易评估标准

```csharp
public SimulationEvaluation EvaluateSimulationPerformance(SimulationUpdateEvent evt)
{
    // 调整评估标准
    var minProfitPercent = 0.10;   // 降低盈利要求（原0.15）
    var maxDrawdown = 0.12;        // 提高回撤容忍度（原0.10）
    var minWinRate = 0.55;         // 降低胜率要求（原0.60）
    var minTradeDays = 5;          // 缩短测试周期（原7天）
    
    // ...其余代码
}
```

## 📊 学习统计查询

```csharp
var coordinator = ServiceLocator.GetAICentralCoordinator();
var learningModule = coordinator._learningModule;

// 获取学习统计
var stats = learningModule.GetStatistics();

Console.WriteLine($"""
    📚 学习模块统计
    ├─ 总决策记录: {stats.TotalRecords}
    ├─ 有结果记录: {stats.RecordsWithOutcome}
    ├─ 整体成功率: {stats.OverallSuccessRate:P2}
    ├─ 平均盈利: {stats.OverallAvgProfit:P2}
    └─ 更新时间: {stats.Timestamp:yyyy-MM-dd HH:mm:ss}
    """);
```

## 🛡️ 安全建议

### 1. 从模拟开始

```csharp
// 强制从回测阶段开始
var coordinator = ServiceLocator.GetAICentralCoordinator();
coordinator._stateManager.SetStage(WorkflowStage.Backtest);
await coordinator.StartAsync();

// 不要直接设置为实盘
// coordinator._stateManager.SetStage(WorkflowStage.Live); // ❌ 危险！
```

### 2. 设置风险限制

```csharp
// 在启动前配置风险参数
ServiceLocator.RiskConfig.MaxDrawdown = 0.12m;        // 最大回撤12%
ServiceLocator.RiskConfig.DailyLossLimit = 300m;      // 单日亏损限制300 USDT
ServiceLocator.RiskConfig.MaxPositionSize = 0.15;     // 单仓位最大15%
```

### 3. 监控关键事件

```csharp
// 必须监听风险警报
eventBus.Subscribe<RiskAlertEvent>(async evt =>
{
    if (evt.Severity == RiskSeverity.Critical)
    {
        // 立即停止系统
        await coordinator.StopAsync();
        
        // 发送通知
        await SendEmailAlert($"系统紧急停止: {evt.Message}");
    }
});
```

## 🎓 最佳实践

### ✅ DO（推荐）

1. **逐步升级**：回测 → 模拟 → 实盘
2. **充分测试**：模拟至少运行7-14天
3. **小资金开始**：实盘初始资金不超过总资金的10%
4. **监控日志**：定期检查 `Logs/app-*.log`
5. **设置警报**：订阅所有风险事件
6. **定期备份**：保存学习模块数据

### ❌ DON'T（避免）

1. ❌ 跳过回测直接实盘
2. ❌ 忽略风险警报
3. ❌ 在高波动市场启动实盘
4. ❌ 使用过大杠杆
5. ❌ 不监控系统状态
6. ❌ 在生产环境调试

## 📞 问题排查

### 问题1：协调器无法启动

```bash
# 检查API密钥
echo $env:DEEPSEEK_API_KEY
echo $env:BINANCE_API_KEY

# 查看日志
cat Logs/app-*.log | Select-String "AICentralCoordinator"
```

### 问题2：一直处于Idle阶段

```csharp
// 手动触发回测
coordinator._stateManager.SetStage(WorkflowStage.Backtest);
```

### 问题3：决策总是Hold

```csharp
// 检查市场数据
var state = coordinator.CurrentState;
Console.WriteLine($"波动率: {state.MarketCondition.Volatility}");
Console.WriteLine($"趋势: {state.MarketCondition.Trend}");
```

## 🚀 下一步

现在您已经掌握了中央AI协调器的基本使用，建议：

1. ✅ 阅读完整架构文档：`Docs/Central_AI_Coordinator_Architecture.md`
2. ✅ 理解各个组件的职责和交互
3. ✅ 在回测模式下运行24小时观察行为
4. ✅ 根据需求调整决策规则和评估标准
5. ✅ 在模拟模式下运行至少7天
6. ✅ 仅在充分验证后启用实盘

---

**祝您交易成功！** 🎉

如有问题，请查看完整文档或联系技术支持。
