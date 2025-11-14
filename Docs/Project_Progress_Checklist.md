# 项目进度清单（Project Progress Checklist）

> 文件目的：用于在代码仓库中以 Markdown 形式保存当前项目状态、已完成与未完成项目、所用技术栈，以及达到“专业级智能体交易模块”所需的关键补充事项，便于团队查看与跟踪。

---

## 一、项目基本信息

- 语言：`C#`（使用 C# 12）
- 平台 / 目标框架：`.NET 8`
- UI：`WPF`（XAML + C#）
- 架构要点：模块化（Modules）、服务总线/ServiceLocator、回测引擎、AI 策略模块、执行器（实盘/仿真）
- 主要目录（示例）：`Modules/`, `Services/`, `Application/Backtesting/`, `Infrastructure/Data/`, `Docs/`

---

## 二、已完成 / 可用的功能（核验）

- 策略组合管理：`StrategyPortfolioManager`（载入/保存、列表、自动均衡）
- 策略生命周期：启动/停止接口（`StartStrategyAsync`, `StopStrategy`）
- 账户管理：`TradingAccountManager`（支持 Live/Simulated 切换）
- 回测引擎：`EnhancedBacktestEngine`、性能/成本/滑点计算（`PerformanceCalculator`、`CostCalculator`、`SlippageCalculator`）
- 执行器骨架：`LiveOrderExecutor`, `SimulatedOrderExecutor`、`BinanceApiClient`（交易所接入）
- AI 组件骨架：`AIStrategyGenerator`, `AIStrategySuggestionService`, `AITradingBot`、协调器等
- Observability：`ObservabilityService`、`AlertManager`、`MetricsCollector`、日志服务
- 单元测试：若干单元测试（回测/服务层面）
- UI 模块：策略组合界面（`StrategyPortfolioView`）、回测/优化/性能等界面

---

## 三、未完成 / 明显缺失项

- 策略新增/编辑 UI 与持久化（`Add_Click`、`Edit_Click` 仍为占位）
- 策略发布/部署流水线（从回测->仿真->实盘的自动审批与灰度发布）
- 完整 E2E 与回放驱动的集成测试（覆盖实盘/仿真）
- 模型治理（模型版本、注册、A/B 测试）与训练流水线
- 生产级高可用/容器化/自动伸缩配置（Kubernetes 等）
- 更细致的仿真（订单簿仿真、滑点动态模型）
- 风控成熟度：自动熔断、强制限仓、合规审计、密钥轮换策略
- 监控与告警策略的完善（SLO、报警频道、健康检查）

---

## 四、达到“专业级智能体交易模块”所需的关键补充（按优先级）

### 高优先级（必须）
1. 实现策略编辑/新增界面与后端持久化（版本化存储）
2. 沙箱（paper）→ 实盘的分阶段发布、审批/回退机制（灰度）
3. 强制风控引擎与自动熔断（实时限仓、单日/分钟损失阈值）
4. 实时监控指标与告警（PnL、延迟、填单率、异常行为）
5. 端到端回放/回测驱动的 CI 集成测试

### 中优先级
1. 模型治理（注册、版本、回测基线、A/B）
2. 数据质量/特征仓库（时间序列补齐、漂移检测）
3. 生产部署策略（容器化、部署自动化）
4. 密钥与权限治理（轮换、审计）

### 低优先级
1. 自动超参搜索/在线学习（需严格沙箱测试）
2. 策略可解释性与审计链路

---

## 五、短期可执行任务（建议顺序）

1. 在 `Modules/Strategy` 中实现策略编辑窗口（UI + ViewModel + 保存接口）并连接 `StrategyPortfolioManager`（1-2 周）
2. 为启动实盘增加人工确认 + 审计日志（在 UI 与服务层均记录）（1 周）
3. 实施一条关键风控规则（如单策略当日损失阈值）并添加自动停止测试用例（1 周）
4. 增加回放驱动的 E2E 测试脚本（历史行情回放 + 下单回放）并加入 CI（2-3 周）
5. 编写模型/数据治理 RFC 并建立最小可行的模型注册表（2 周）

---

## 六、仓库中需要修改/新增的关键文件建议

- `Modules/Strategy/StrategyPortfolioView.xaml.cs`：实现 `Add_Click` / `Edit_Click` 弹出策略编辑窗并做验证
- 新增 `Modules/Strategy/StrategyEditorWindow.xaml(.cs)`：策略创建/编辑与验证 UI
- `Services/StrategyPortfolioManager.cs`：暴露版本化保存/加载接口
- 新增 `Tests/E2E/ReplaySmokeTests.cs`：回放驱动 E2E 测试
- `Docs/Project_Progress_Checklist.md`（本文件）用于持续追踪

---

## 七、结语

当前代码库已具备构建可运行量化交易平台的核心模块（回测、策略管理、执行器、AI 骨架）。为达到“专业级智能体交易模块”需补强发布治理、风控自动化、模型治理、E2E 测试与监控告警等关键工程能力。

如果确认，我可以：
- 立即生成 `StrategyEditorWindow` 的示例 XAML 与代码文件并创建在 `Modules/Strategy/`，或
- 生成一份更细化的 `tasks.md`（任务跟踪）并创建在 `.github/upgrades/` 或 `Docs/`。

---

（文件结束）
