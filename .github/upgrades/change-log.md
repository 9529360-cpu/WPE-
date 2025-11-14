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


示例：

- ID: `CHG-20251114-01`
  日期: `2025-11-14`
  任务关联: `A1`
  文件路径: `Services/IMarketDataService.cs`, `Services/MarketDataService.cs`
  类型: `新增`
  目的: `抽象行情订阅与重连逻辑，供 Strategy 和 UI 消费`
  状态: `Done`
  提交: `branch: upgrade-to-NET10, commit: <待填写>`
  备注: `创建接口与空实现骨架，供后续填充实现。`
