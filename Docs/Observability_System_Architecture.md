# 🔭 可观测性系统架构设计

**版本**: v1.0  
**日期**: 2025-01-XX  
**状态**: ✅ 设计完成

---

## 📊 架构概述

### 三大支柱
```
┌─────────────────────────────────────────────────────┐
│          可观测性系统 (Observability)                │
├─────────────────────────────────────────────────────┤
│                                                      │
│  📝 Logs              📊 Metrics          🔍 Traces │
│  (日志)               (指标)              (追踪)     │
│     │                    │                   │      │
│     ├─ 结构化日志        ├─ 性能指标         ├─ 分布式追踪 │
│     ├─ 日志等级          ├─ 业务指标         ├─ 调用链     │
│     ├─ 上下文信息        ├─ 系统指标         ├─ 耗时分析   │
│     └─ 日志聚合          └─ 自定义指标       └─ 依赖关系   │
│                                                      │
└─────────────────────────────────────────────────────┘
                          ↓
                    统一观测平台
```

---

## 🎯 设计目标

### 核心目标
```
1. ✅ 全链路可追踪 - 任何操作都能追溯
2. ✅ 实时监控    - 系统状态实时可见
3. ✅ 主动告警    - 异常及时通知
4. ✅ 性能分析    - 瓶颈快速定位
5. ✅ 故障诊断    - 问题快速排查
```

### 性能指标
```
日志延迟:     < 100ms
指标延迟:     < 50ms
追踪延迟:     < 200ms
存储压力:     最小化
CPU开销:      < 3%
内存开销:     < 50MB
```

---

## 📝 Logs - 结构化日志系统

### 日志架构

```
┌──────────────────────────────────────────────────┐
│              结构化日志系统                       │
├──────────────────────────────────────────────────┤
│                                                   │
│  应用层                                           │
│  ├─ AICentralCoordinator  → StructuredLogger    │
│  ├─ WorkflowEngine        → StructuredLogger    │
│  ├─ DecisionEngine        → StructuredLogger    │
│  └─ ...其他模块           → StructuredLogger    │
│                                                   │
│  日志处理层                                       │
│  ├─ LogFormatter    (格式化)                     │
│  ├─ LogEnricher     (增强)                       │
│  ├─ LogFilter       (过滤)                       │
│  └─ LogBatcher      (批量)                       │
│                                                   │
│  输出层                                           │
│  ├─ ConsoleOutput   (控制台)                     │
│  ├─ FileOutput      (文件)                       │
│  ├─ DatabaseOutput  (数据库)                     │
│  └─ RemoteOutput    (远程)                       │
│                                                   │
└──────────────────────────────────────────────────┘
```

### 日志级别

```csharp
public enum LogLevel
{
    Trace    = 0,  // 最详细的信息
    Debug    = 1,  // 调试信息
    Info     = 2,  // 一般信息
    Warning  = 3,  // 警告信息
    Error    = 4,  // 错误信息
    Critical = 5   // 致命错误
}
```

### 日志格式 (JSON)

```json
{
  "timestamp": "2025-01-XX 12:34:56.789",
  "level": "Info",
  "logger": "AICentralCoordinator",
  "message": "工作流自动转换成功",
  "context": {
    "traceId": "abc-123-def",
    "spanId": "span-456",
    "userId": "system",
    "operation": "WorkflowTransition",
    "duration": 125.5
  },
  "properties": {
    "fromStage": "Backtest",
    "toStage": "Simulation",
    "ruleName": "BacktestSuccess_To_Simulation"
  },
  "exception": null
}
```

### 日志字段规范

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| timestamp | DateTime | ✅ | 时间戳（UTC） |
| level | LogLevel | ✅ | 日志等级 |
| logger | string | ✅ | 日志源 |
| message | string | ✅ | 日志消息 |
| traceId | string | ✅ | 追踪ID |
| spanId | string | ⚪ | 跨度ID |
| operation | string | ✅ | 操作名称 |
| duration | double | ⚪ | 操作耗时(ms) |
| properties | object | ⚪ | 自定义属性 |
| exception | object | ⚪ | 异常信息 |

---

## 📊 Metrics - 指标收集系统

### 指标架构

```
┌──────────────────────────────────────────────────┐
│              指标收集系统                         │
├──────────────────────────────────────────────────┤
│                                                   │
│  指标采集                                         │
│  ├─ Counter      (计数器)                        │
│  ├─ Gauge        (仪表盘)                        │
│  ├─ Histogram    (直方图)                        │
│  └─ Summary      (摘要)                          │
│                                                   │
│  指标聚合                                         │
│  ├─ MetricAggregator                             │
│  ├─ TimeWindow (时间窗口)                        │
│  └─ Percentile (百分位)                          │
│                                                   │
│  指标导出                                         │
│  ├─ Prometheus   (Prometheus格式)               │
│  ├─ JSON         (JSON格式)                      │
│  └─ Custom       (自定义格式)                    │
│                                                   │
└──────────────────────────────────────────────────┘
```

### 指标类型

#### 1. 业务指标
```
✅ 决策数量        - decision_count
✅ 决策成功率      - decision_success_rate
✅ 交易数量        - trade_count
✅ 盈亏金额        - profit_loss_amount
✅ 胜率            - win_rate
✅ 工作流转换次数  - workflow_transition_count
✅ 因子计算次数    - factor_calculation_count
```

#### 2. 性能指标
```
✅ 响应时间        - response_time_ms
✅ 吞吐量          - throughput_per_second
✅ 错误率          - error_rate
✅ 队列长度        - queue_length
✅ 并发数          - concurrent_requests
```

#### 3. 系统指标
```
✅ CPU使用率       - cpu_usage_percent
✅ 内存使用        - memory_usage_mb
✅ 网络延迟        - network_latency_ms
✅ 线程数          - thread_count
✅ GC回收          - gc_collection_count
```

### 指标命名规范

```
<namespace>_<subsystem>_<metric>_<unit>

示例:
ai_coordinator_decision_count_total
ai_coordinator_decision_latency_ms
trading_execution_success_rate_percent
system_cpu_usage_percent
```

---

## 🔍 Traces - 分布式追踪系统

### 追踪架构

```
┌──────────────────────────────────────────────────┐
│              分布式追踪系统                       │
├──────────────────────────────────────────────────┤
│                                                   │
│  追踪采集                                         │
│  ├─ Activity      (活动)                         │
│  ├─ Span          (跨度)                         │
│  └─ Baggage       (行李)                         │
│                                                   │
│  追踪传播                                         │
│  ├─ TraceContext  (W3C标准)                      │
│  ├─ Baggage       (键值对)                       │
│  └─ Custom        (自定义)                       │
│                                                   │
│  追踪存储                                         │
│  ├─ InMemory      (内存)                         │
│  ├─ File          (文件)                         │
│  └─ Remote        (远程)                         │
│                                                   │
└──────────────────────────────────────────────────┘
```

### 追踪示例

```
TraceId: abc-123-def

MainControlLoop (200ms)
├─ CollectSystemState (50ms)
│  ├─ CollectMarketCondition (20ms)
│  ├─ CollectAccountStatus (10ms)
│  └─ CollectRiskMetrics (20ms)
├─ CalculateDecisionFactors (80ms)
│  ├─ PrepareMarketData (30ms)
│  └─ CalculateAllFactors (50ms)
├─ EvaluateAndTransition (30ms)
└─ UpdateKnowledge (40ms)
```

### Span属性

```csharp
{
  "traceId": "abc-123-def",
  "spanId": "span-456",
  "parentSpanId": "span-789",
  "name": "CalculateDecisionFactors",
  "startTime": "2025-01-XX 12:34:56.000",
  "endTime": "2025-01-XX 12:34:56.080",
  "duration": 80,
  "status": "Ok",
  "attributes": {
    "factor.count": 50,
    "data.quality": 1.0,
    "cache.hit": true
  }
}
```

---

## 🚨 Alert - 告警通知系统

### 告警架构

```
┌──────────────────────────────────────────────────┐
│              告警通知系统                         │
├──────────────────────────────────────────────────┤
│                                                   │
│  告警规则                                         │
│  ├─ ThresholdRule   (阈值规则)                   │
│  ├─ AnomalyRule     (异常规则)                   │
│  ├─ TrendRule       (趋势规则)                   │
│  └─ CompositeRule   (复合规则)                   │
│                                                   │
│  告警评估                                         │
│  ├─ RuleEvaluator                                │
│  ├─ AlertAggregator (聚合)                       │
│  └─ AlertDeduplicator (去重)                     │
│                                                   │
│  告警通知                                         │
│  ├─ EmailNotifier   (邮件)                       │
│  ├─ SMSNotifier     (短信)                       │
│  ├─ WebhookNotifier (Webhook)                    │
│  └─ UINotifier      (界面)                       │
│                                                   │
└──────────────────────────────────────────────────┘
```

### 告警级别

```csharp
public enum AlertSeverity
{
    Info     = 0,  // 信息
    Warning  = 1,  // 警告
    Error    = 2,  // 错误
    Critical = 3   // 严重
}
```

### 告警规则示例

```csharp
// 规则1: CPU使用率告警
{
  "name": "HighCpuUsage",
  "condition": "cpu_usage_percent > 80",
  "duration": "5m",
  "severity": "Warning",
  "message": "CPU使用率过高: {{value}}%",
  "actions": ["email", "ui"]
}

// 规则2: 决策失败率告警
{
  "name": "HighDecisionFailureRate",
  "condition": "decision_failure_rate > 0.2",
  "duration": "10m",
  "severity": "Error",
  "message": "决策失败率过高: {{value}}%",
  "actions": ["email", "sms", "ui"]
}

// 规则3: 系统异常告警
{
  "name": "SystemAnomaly",
  "condition": "error_count > 10",
  "duration": "1m",
  "severity": "Critical",
  "message": "系统异常: {{count}}个错误",
  "actions": ["email", "sms", "webhook", "ui"]
}
```

---

## 📊 Dashboard - 监控仪表板

### 仪表板架构

```
┌──────────────────────────────────────────────────┐
│              监控仪表板                           │
├──────────────────────────────────────────────────┤
│                                                   │
│  概览面板                                         │
│  ├─ 系统健康度                                    │
│  ├─ 实时性能                                      │
│  ├─ 活跃告警                                      │
│  └─ 关键指标                                      │
│                                                   │
│  详细面板                                         │
│  ├─ AI决策监控                                    │
│  ├─ 工作流监控                                    │
│  ├─ 性能监控                                      │
│  ├─ 系统资源监控                                  │
│  └─ 日志查看                                      │
│                                                   │
│  分析面板                                         │
│  ├─ 趋势分析                                      │
│  ├─ 对比分析                                      │
│  └─ 异常分析                                      │
│                                                   │
└──────────────────────────────────────────────────┘
```

### 关键图表

```
1. ✅ 系统健康度仪表盘
2. ✅ AI决策趋势图
3. ✅ 工作流转换时间线
4. ✅ 性能指标热力图
5. ✅ CPU/内存使用率折线图
6. ✅ 错误率柱状图
7. ✅ 响应时间分布图
8. ✅ 实时日志流
```

---

## 🔧 技术选型

### 核心组件

```
日志:    自研StructuredLogger
指标:    自研MetricsCollector
追踪:    System.Diagnostics.Activity (内置)
存储:    SQLite (本地) + 可选远程
导出:    Prometheus格式
可视化:  WPF自定义控件
```

### 依赖库

```xml
<!-- 无需额外依赖，使用.NET 8内置功能 -->
<PackageReference Include="System.Diagnostics.DiagnosticSource" Version="8.0.0" />
```

---

## 📐 数据模型

### LogEntry
```csharp
public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Logger { get; init; }
    public string Message { get; init; }
    public string TraceId { get; init; }
    public string? SpanId { get; init; }
    public string Operation { get; init; }
    public double? Duration { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public Exception? Exception { get; init; }
}
```

### MetricPoint
```csharp
public class MetricPoint
{
    public DateTime Timestamp { get; init; }
    public string Name { get; init; }
    public MetricType Type { get; init; }
    public double Value { get; init; }
    public Dictionary<string, string> Tags { get; init; }
}
```

### TraceSpan
```csharp
public class TraceSpan
{
    public string TraceId { get; init; }
    public string SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string Name { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public TimeSpan Duration { get; init; }
    public SpanStatus Status { get; init; }
    public Dictionary<string, object> Attributes { get; init; }
}
```

### AlertEvent
```csharp
public class AlertEvent
{
    public Guid Id { get; init; }
    public string RuleName { get; init; }
    public AlertSeverity Severity { get; init; }
    public string Message { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, object> Context { get; init; }
    public bool IsResolved { get; set; }
}
```

---

## 🎯 实施计划

### Phase 1: 基础设施 (1-2天)
```
✅ 创建StructuredLogger
✅ 创建MetricsCollector
✅ 创建TraceManager
✅ 创建AlertManager
```

### Phase 2: 集成 (1天)
```
✅ 集成到AICentralCoordinator
✅ 集成到WorkflowEngine
✅ 集成到DecisionEngine
✅ 集成到其他关键组件
```

### Phase 3: 可视化 (1-2天)
```
✅ 创建ObservabilityDashboard
✅ 实现实时监控图表
✅ 实现告警通知界面
```

### Phase 4: 测试和优化 (1天)
```
✅ 性能测试
✅ 压力测试
✅ 可用性测试
✅ 文档编写
```

---

## 📊 性能估算

### 资源开销

```
日志系统:
- CPU: < 1%
- 内存: < 20MB
- 磁盘: ~100MB/天 (压缩后)

指标系统:
- CPU: < 1%
- 内存: < 10MB
- 存储: ~10MB/天

追踪系统:
- CPU: < 1%
- 内存: < 20MB
- 存储: ~50MB/天

总计:
- CPU: < 3%
- 内存: < 50MB
- 磁盘: ~160MB/天
```

---

## 🏆 预期收益

### 直接收益
```
✅ 问题定位时间: 从小时级 → 分钟级
✅ 系统可见性: 从0% → 95%
✅ 故障预警: 被动 → 主动
✅ 性能分析: 困难 → 简单
✅ 调试效率: 提升 10x
```

### 间接收益
```
✅ 降低维护成本
✅ 提升用户信心
✅ 加速迭代速度
✅ 提高系统质量
✅ 支持数据驱动决策
```

---

## 📝 编码规范

### 日志规范
```csharp
// ✅ 好的做法
logger.Info("工作流转换成功", new {
    fromStage = "Backtest",
    toStage = "Simulation",
    duration = 125.5
});

// ❌ 避免的做法
logger.Info($"工作流从{fromStage}转换到{toStage}");
```

### 指标规范
```csharp
// ✅ 好的做法
metrics.Increment("ai_coordinator_decision_count_total", new {
    result = "success",
    stage = "live"
});

// ❌ 避免的做法
metrics.Increment("DecisionCount");
```

### 追踪规范
```csharp
// ✅ 好的做法
using var activity = trace.StartActivity("CalculateFactors");
activity?.AddTag("factor.count", 50);
// 操作...
activity?.SetStatus(ActivityStatusCode.Ok);

// ❌ 避免的做法
// 不记录追踪信息
```

---

**架构设计完成！** 🎉

**下一步**: 实现结构化日志系统

**预计耗时**: 3-5天完成全部实施

**目标**: 将系统可观测性从80分提升到95分！🚀
