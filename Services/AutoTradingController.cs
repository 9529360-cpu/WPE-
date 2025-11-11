using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

/// <summary>
/// 一键自动交易控制器
/// - 读取配置的 API Key（Binance/DeepSeek）
/// - 初始化所需组件
/// - 启动/停止 AI 自动交易（基于 WebSocket 实时行情）
/// </summary>
public sealed class AutoTradingController
{
    private AITradingAutomation? _automation;
    private TradingAccountManager? _accountManager;
    private PositionManager? _positionManager;

    public bool IsRunning => _automation?.IsRunning == true;

    /// <summary>
    /// 启动 AI 自动化交易
    /// </summary>
    /// <param name="symbols">交易对数组，默认 BTCUSDT</param>
    /// <param name="accountType">账户类型，默认模拟账户</param>
    public async Task StartAsync(string[]? symbols = null, AccountType accountType = AccountType.Simulated, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        // 1) 准备 Binance API 凭证
        (string apiKey, string secretKey) = ConfigurationService.GetBinanceCredentials();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("Binance API 未配置，请在 [设置 > API 管理] 中保存 API Key/Secret");
        }

        // 2) 设置到全局 Binance 客户端
        ServiceLocator.Api.SetApiCredentials(apiKey, secretKey);

        // 3) 准备 DeepSeek API Key
        string deepSeekKey = ConfigurationService.GetDeepSeekApiKey();
        if (string.IsNullOrWhiteSpace(deepSeekKey))
        {
            throw new InvalidOperationException("DeepSeek API 未配置，请在 [设置 > API 管理] 中保存 DeepSeek API Key");
        }

        // 4) 组装自动化交易管线
        _accountManager = new TradingAccountManager(ServiceLocator.Cache);
        if (_accountManager.ActiveAccount == null)
        {
            _accountManager.CreateSimulatedAccount("模拟账户", 10_000m);
        }
        _accountManager.SwitchAccount(accountType);

        var dataProcessor = new MarketDataPreprocessor(ServiceLocator.Api, ServiceLocator.Cache);
        var aiAgent = new DeepSeekTradingAgent(deepSeekKey);
        var riskManager = new AIRiskManager();
        var execEngine = new AIOrderExecutionEngine(_accountManager, ServiceLocator.Api, ServiceLocator.Cache, riskManager);
        _positionManager = new PositionManager(_accountManager, ServiceLocator.Api, execEngine);

        _automation = new AITradingAutomation(
            ServiceLocator.Stream,
            dataProcessor,
            aiAgent,
            execEngine,
            _accountManager,
            _positionManager
        );

        // 5) 启动
        string[] syms = (symbols is { Length: > 0 }) ? symbols : new[] { "BTCUSDT" };
        await _automation.StartAsync(syms, accountType);
    }

    /// <summary>
    /// 停止 AI 自动化交易
    /// </summary>
    public async Task StopAsync()
    {
        if (_automation != null && _automation.IsRunning)
        {
            await _automation.StopAsync();
        }
    }
}
