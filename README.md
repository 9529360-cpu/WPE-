# 币安量化交易机器人 (Binance Quantitative Trading Bot)

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows)

一个功能完善的币安加密货币量化交易机器人，基于 WPF 构建，支持多策略、AI 预测、风险管理和实时监控。

A comprehensive Binance cryptocurrency quantitative trading bot built with WPF, supporting multiple strategies, AI forecasting, risk management, and real-time monitoring.

## ✨ 核心特性 (Core Features)

### 📊 市场数据 (Market Data)
- **实时行情监控**: WebSocket 实时 K 线数据流
- **资金费率追踪**: 永续合约资金费率分析
- **多时间框架分析**: 支持 1m, 5m, 15m, 1h, 4h, 1d 等多个时间周期
- **历史数据回放**: 支持从数据库和文件导入历史数据

### 🤖 智能策略 (Trading Strategies)
- **均值回归策略**: 基于统计套利的均值回归交易
- **动量策略**: 趋势跟踪和动量捕捉
- **多策略组合**: 支持策略叠加和风险分散
- **策略模板库**: 预置多种经典量化策略

### 🧠 AI 模型 (AI & Machine Learning)
- **LSTM 价格预测**: 深度学习时序预测模型
- **随机森林信号**: 多因子机器学习信号生成
- **特征工程**: 自动技术指标计算和特征提取
- **模型训练与评估**: 在线训练和性能监控

### ⚖️ 风险管理 (Risk Management)
- **动态止损**: 基于 ATR 的自适应止损
- **最大回撤控制**: 实时回撤监控和仓位调整
- **Kelly 资金分配**: 科学的仓位管理
- **VaR 计算**: 风险价值评估
- **黑名单管理**: 高风险交易对自动过滤

### 🔬 回测与优化 (Backtesting & Optimization)
- **历史回测**: 支持多年历史数据回测
- **Walk-Forward 优化**: 前向分析优化
- **网格搜索**: 参数空间自动搜索
- **性能基准测试**: 策略性能评估和对比

### 📝 交易执行 (Trading Execution)
- **实盘交易**: 连接币安 API 进行实际交易
- **纸交易模拟**: 零风险的模拟交易环境
- **智能订单路由**: 最优价格执行
- **持仓订单管理**: 实时持仓和订单跟踪

### 📈 监控告警 (Monitoring & Alerts)
- **实时监控面板**: 关键指标可视化
- **多渠道告警**: 支持 Telegram、钉钉通知
- **性能诊断**: 系统健康检查和性能分析
- **日志记录**: 完整的操作审计日志

## 🏗️ 架构设计 (Architecture)

```
├── Core/                    # 核心领域层
│   ├── Abstractions/       # 接口定义
│   ├── Models/             # 领域模型
│   ├── Strategies/         # 交易策略
│   └── Risk/               # 风险管理
├── Application/            # 应用服务层
│   ├── Backtesting/       # 回测引擎
│   └── Services/          # 应用服务
├── Infrastructure/         # 基础设施层
│   └── Data/              # 数据管道
├── Services/              # 外部服务
├── Modules/               # UI 模块
│   ├── Market/           # 市场模块
│   ├── Strategy/         # 策略模块
│   ├── Trade/            # 交易模块
│   ├── Risk/             # 风控模块
│   ├── AI/               # AI 模块
│   └── ...               # 其他模块
└── Data/                  # 数据存储
    └── ai/               # AI 模型文件
```

详细架构设计请参考 [架构文档](Docs/Architecture.md)。

## 🚀 快速开始 (Quick Start)

### 前置要求 (Prerequisites)

- **操作系统**: Windows 10/11 (64-bit)
- **.NET SDK**: 8.0 或更高版本
- **Visual Studio**: 2022 或更高版本 (推荐)
- **币安账户**: 需要 API Key 和 Secret

### 安装步骤 (Installation)

1. **克隆仓库**
```bash
git clone https://github.com/9529360-cpu/WPE-.git
cd WPE-
```

2. **恢复依赖**
```bash
dotnet restore
```

3. **配置 API 密钥**

首次运行后，在 `Data/appsettings.json` 中配置您的币安 API:

```json
{
  "AutoReconnect": true,
  "EnableNotifications": true,
  "Environment": "Production",
  "RefreshIntervalSeconds": 5,
  "LogLevel": "Info",
  "TelegramBotToken": "your-telegram-bot-token",
  "TelegramChatId": "your-telegram-chat-id",
  "DingTalkWebhook": "your-dingtalk-webhook-url"
}
```

4. **运行应用**
```bash
dotnet run
```

或在 Visual Studio 中按 F5 运行。

### 首次使用 (First-Time Setup)

1. **配置 API 密钥**: 在"API"模块中添加币安 API Key
2. **选择交易对**: 在"市场"模块中选择要交易的币对
3. **配置策略**: 在"策略配置"中设置交易策略参数
4. **回测验证**: 在"回测"模块中验证策略效果
5. **纸交易测试**: 在"纸交易"模块中进行模拟交易
6. **实盘交易**: 确认无误后在"交易"模块开启实盘

## 📖 使用指南 (User Guide)

### 基础功能

#### 1. 市场监控
- 打开"行情"模块查看实时价格
- 订阅多个交易对的实时数据
- 查看技术指标和 K 线图表

#### 2. 策略配置
- 在"策略配置"中选择策略类型
- 调整策略参数（入场条件、出场条件等）
- 设置仓位大小和风险参数

#### 3. 回测验证
- 选择历史时间范围
- 运行回测查看策略表现
- 分析夏普比率、最大回撤等指标

#### 4. AI 模型
- 加载预训练的 LSTM 模型
- 查看价格预测结果
- 训练自定义模型（高级功能）

#### 5. 风险控制
- 在"风控"模块设置止损规则
- 配置最大持仓限额
- 监控实时风险指标

#### 6. 实盘交易
- 确保 API 密钥已配置
- 在"交易"模块启动策略
- 实时监控订单和持仓

### 高级功能

#### Walk-Forward 优化
在"优化"模块中进行前向优化:
1. 设置训练窗口和测试窗口
2. 定义参数搜索空间
3. 运行优化获取最优参数

#### 多策略组合
1. 创建多个策略实例
2. 为每个策略分配资金比例
3. 系统自动进行组合管理

## 🔧 配置说明 (Configuration)

### API 配置
在应用的"API"模块中配置币安 API:
- **API Key**: 您的币安 API 密钥
- **Secret Key**: 对应的密钥
- **权限要求**: 需要"现货交易"和"读取"权限

**安全提示**: 
- 不要将 API 密钥提交到版本控制
- 建议使用子账户和 IP 白名单
- 定期轮换 API 密钥

### 应用配置
配置文件位于 `Data/appsettings.json`:

| 参数 | 说明 | 默认值 |
|------|------|--------|
| AutoReconnect | 自动重连 | true |
| EnableNotifications | 启用通知 | true |
| Environment | 运行环境 | Production |
| RefreshIntervalSeconds | 刷新间隔（秒） | 5 |
| LogLevel | 日志级别 | Info |

### 数据库配置
应用使用 SQLite 存储历史数据，数据库文件位于 `Data/trading.sqlite`，首次运行时自动创建。

## 🧪 测试 (Testing)

### 运行单元测试
```bash
dotnet test
```

### 运行性能基准测试
```bash
dotnet run --configuration Release -c BENCHMARKS
```

## 📦 依赖项 (Dependencies)

- **Binance.Net** (8.3.0): 币安 API 客户端
- **ScottPlot.WPF** (5.0.56): 数据可视化
- **Microsoft.Data.Sqlite** (8.0.4): SQLite 数据库

完整依赖列表请查看 [币安量化机器人.csproj](币安量化机器人.csproj)。

## 🛠️ 开发指南 (Development)

### 添加新策略

1. 实现 `ITradingStrategy` 接口:
```csharp
public class MyCustomStrategy : ITradingStrategy
{
    public string Name => "My Custom Strategy";
    
    public async ValueTask<StrategyDecision> EvaluateAsync(
        MarketObservation observation)
    {
        // 实现您的策略逻辑
    }
}
```

2. 在 `ServiceLocator` 中注册策略
3. 在 UI 的策略库中添加配置界面

### 添加自定义指标

在 `TechnicalIndicatorEngineer` 中添加新指标:
```csharp
public async Task<DataFrame> TransformAsync(
    DataFrame input, 
    CancellationToken cancellationToken = default)
{
    // 计算自定义指标
    return enrichedData;
}
```

### 代码风格
- 遵循 C# 编码规范
- 使用有意义的变量名
- 添加 XML 文档注释
- 编写单元测试

## 🐛 故障排查 (Troubleshooting)

### 常见问题

**Q: 无法连接到币安 API**
- 检查网络连接和防火墙设置
- 确认 API 密钥配置正确
- 检查 API 密钥权限

**Q: 历史数据加载失败**
- 确保 Data 目录存在
- 检查数据库文件权限
- 查看日志文件获取详细错误信息

**Q: 策略不执行交易**
- 检查策略参数配置
- 确认余额充足
- 查看风控规则是否触发

**Q: UI 模块加载失败**
- 检查 XAML 文件路径
- 确认命名空间正确
- 查看应用日志

## 📊 性能优化 (Performance)

### 数据流优化
- 使用异步流处理实时数据
- 实现数据缓存减少 API 调用
- 批量处理历史数据查询

### 策略执行优化
- 并行运行多个策略实例
- 使用内存特征存储
- 优化技术指标计算

## 🔐 安全建议 (Security)

1. **API 密钥安全**
   - 使用环境变量存储敏感信息
   - 启用 IP 白名单
   - 限制 API 权限范围

2. **网络安全**
   - 使用 HTTPS 连接
   - 验证 SSL 证书
   - 实现请求签名验证

3. **数据安全**
   - 加密敏感数据存储
   - 定期备份交易数据
   - 实现访问控制

## 📝 更新日志 (Changelog)

查看 [CHANGELOG.md](CHANGELOG.md) 了解版本更新历史。

## 🤝 贡献 (Contributing)

欢迎贡献代码！请查看 [CONTRIBUTING.md](CONTRIBUTING.md) 了解贡献指南。

### 贡献方式
1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 📄 许可证 (License)

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## ⚠️ 免责声明 (Disclaimer)

**本软件仅供学习和研究使用。**

- 加密货币交易存在极高风险，可能导致全部资金损失
- 使用本软件进行实盘交易的所有风险由用户自行承担
- 作者不对任何使用本软件造成的损失负责
- 请在充分了解风险的前提下谨慎使用
- 建议先在测试环境和纸交易中充分验证

**投资有风险，入市需谨慎！**

## 📞 联系方式 (Contact)

- **Issues**: [GitHub Issues](https://github.com/9529360-cpu/WPE-/issues)
- **Discussions**: [GitHub Discussions](https://github.com/9529360-cpu/WPE-/discussions)

## 🙏 致谢 (Acknowledgments)

感谢以下开源项目和社区:
- [Binance.Net](https://github.com/JKorf/Binance.Net) - 优秀的币安 API 客户端
- [ScottPlot](https://github.com/ScottPlot/ScottPlot) - 强大的数据可视化库
- .NET 和 WPF 社区的支持

---

**注意**: 本项目处于持续开发中，欢迎提出建议和反馈！

Made with ❤️ by the community
