# 币安量化机器人 (Binance Quantitative Trading Bot)

## 项目简介
这是一个基于 WPF 的币安量化交易机器人系统，支持多策略交易、实时数据处理、风险管理和回测优化。

## 如何正确克隆和使用本仓库

### 问题说明
如果您在使用 Visual Studio 或 Git 客户端时遇到"拉取不完整"或"提取不完整"的问题，这是因为 Git 的 fetch 配置被限制为只获取特定分支。

### 解决方案

#### 方法 1: 使用正确的克隆命令
```bash
# 完整克隆仓库（推荐）
git clone https://github.com/9529360-cpu/WPE-.git
cd WPE-

# 查看所有分支
git branch -a
```

#### 方法 2: 修复现有仓库的配置
如果您已经克隆了仓库但无法看到所有分支，请执行以下命令：

```bash
# 进入仓库目录
cd WPE-

# 更新 fetch 配置以获取所有分支
git config remote.origin.fetch "+refs/heads/*:refs/remotes/origin/*"

# 重新获取所有分支
git fetch --all

# 查看所有可用分支
git branch -r
```

#### 方法 3: 在 Visual Studio 中的操作
1. 打开 Visual Studio
2. 转到 **Git** > **设置**
3. 在 Git 设置中，确保启用了 "拉取所有分支"
4. 使用 **Git** > **拉取** 来更新仓库

### 验证配置是否正确
运行以下命令检查配置：
```bash
git config --get remote.origin.fetch
```

应该显示：
```
+refs/heads/*:refs/remotes/origin/*
```

## 项目结构
```
WPE-/
├── Application/        # 应用层逻辑
├── Core/              # 核心业务逻辑
├── Data/              # 数据层
├── Docs/              # 文档
├── Infrastructure/    # 基础设施层
├── Models/            # 数据模型
├── Modules/           # 功能模块
├── Services/          # 服务层
└── Tests/             # 测试
```

## 系统要求
- .NET 6.0 或更高版本
- Visual Studio 2022 或更高版本
- Windows 10/11

## 构建项目
```bash
# 使用 .NET CLI
dotnet restore
dotnet build

# 或者在 Visual Studio 中
# 打开 币安量化机器人.slnx
# 按 Ctrl+Shift+B 构建
```

## 运行项目
```bash
dotnet run --project 币安量化机器人.csproj
```

## 主要功能
- **多策略支持**: 均值回归策略、动量策略
- **实时数据处理**: 多数据源聚合、质量检测
- **风险管理**: 动态止损、最大回撤控制、Kelly 资金分配
- **回测优化**: Walk-Forward 优化、参数调优
- **监控告警**: 实时交易监控、风险事件告警

## 架构文档
详细架构信息请参考 [Docs/Architecture.md](Docs/Architecture.md)

## 贡献指南
1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 常见问题

### Q: 为什么我的 Visual Studio 无法看到所有分支？
A: 请按照上述"方法 2"或"方法 3"修复 Git 配置。

### Q: 克隆时显示"权限被拒绝"
A: 确保您有仓库的访问权限，并正确配置了 Git 凭据。

### Q: 项目无法构建
A: 确保已安装 .NET 6.0 SDK，并运行 `dotnet restore` 恢复依赖。

## 许可证
请参考项目中的 LICENSE 文件。

## 联系方式
如有问题或建议，请在 GitHub Issues 中提出。
