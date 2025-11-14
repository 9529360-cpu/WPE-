# 变更记录 (Change Log)

本文件用于记录每次对项目的新增/修改/删除操作，条目遵循以下格式：

- ID: 唯一编号，例如 `CHG-YYYYMMDD-XX`
- 日期: `YYYY-MM-DD`
- 任务关联: 对应计划项 ID（例如 `A1` / `U2`）
- 文件路径: 列出新增/修改的文件（使用相对路径，如 `Services/MarketDataService.cs`）
- 类型: `新增` / `修改` / `删除`
- 目的: 一句话说明改动理由
- 状态: `Pending` / `InProgress` / `Done`
- 提交: 提交或分支信息（若已提交，写 commit id 或 分支名）
- 备注: 任何额外说明或回退指引


示例条目：

- ID: `CHG-20251114-01`
  日期: `2025-11-14`
  任务关联: `A1`
  文件路径: `Core/IMarketDataService.cs`, `Services/MarketDataService.cs`
  类型: `新增`
  目的: `抽象行情订阅与重连逻辑，供 Strategy 和 UI 消费`
  状态: `Done`
  提交: `branch: upgrade-to-NET10, commit: a1b5c6e`
  备注: `创建接口与空实现骨架，供后续填充实现。`

- ID: `CHG-20251114-02`
  日期: `2025-11-14`
  任务关联: `A3`
  文件路径: `Services/IOrderExecutionService.cs`, `Services/OrderExecutionService.cs`
  类型: `新增`
  目的: `下单与撤单服务骨架，供 UI 测试与后续实现幂等/重试/持久化逻辑`
  状态: `Done`
  提交: `branch: upgrade-to-NET10, commit: a1b5c6e`
  备注: `当前为内存实现，后续需实现持久化与交易所适配。`

- ID: `CHG-20251114-03`
  日期: `2025-11-14`
  任务关联: `U1, U2, U7`
  文件路径: `ViewModels/MainWindowViewModel.cs`, `ViewModels/ObservableObject.cs`, `Modules/Strategy/StrategyManagerView.xaml`, `ViewModels/StrategyManagerViewModel.cs`, `Controls/OrderExecutionControl.xaml`
  类型: `新增`
  目的: `添加 UI 与 ViewModel 骨架以便与服务联动测试`
  状态: `Done`
  提交: `branch: upgrade-to-NET10, commit: a1b5c6e`
  备注: `UI 控件为占位，后续需要绑定命令与数据源。`

- ID: `CHG-20251114-04`
  日期: `2025-11-14`
  任务关联: `A2`
  文件路径: `Core/IEventBus.cs`, `Services/EventBus.cs`, `Core/Events.cs`, `Services/MarketDataService.cs`
  类型: `新增`
  目的: `实现进程内事件总线，MarketDataService 将原始行情发布到事件总线以供下游处理。`
  状态: `Done`
  提交: `branch: upgrade-to-NET10, commit: a1b5c6e`
  备注: `事件总线为轻量实现，不保证跨进程持久化或高级路由。`
