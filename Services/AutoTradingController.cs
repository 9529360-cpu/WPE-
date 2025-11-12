using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

/// <summary>
/// 一键自动交易控制器
/// 支持两种模式：
/// 1. 标准模式：单交易对 AI 自动交易
/// 2. 高频剥头皮模式：多交易对并行监测 + 串行执行
/// </summary>
public sealed class AutoTradingController
{
    private AITradingAutomation? _automation;
    private TradingAccountManager? _accountManager;
    private PositionManager? _positionManager;
    private ParallelScalpingController? _scalpingController;
    
    private bool _isScalpingMode;

    public bool IsRunning => _isScalpingMode 
        ? _scalpingController?.IsRunning == true 
        : _automation?.IsRunning == true;

    /// <summary>
    /// 启动 AI 自动化交易
    /// </summary>
    /// <param name="symbols">交易对数组，默认 BTCUSDT</param>
    /// <param name="accountType">账户类型，默认模拟账户</param>
    /// <param name="enableScalping">启用高频剥头皮模式</param>
    public async Task StartAsync(string[]? symbols = null, AccountType accountType = AccountType.Simulated, bool enableScalping = false, CancellationToken cancellationToken = default)
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

        // 4) 根据模式选择启动方式
        if (enableScalping)
        {
            // 高频剥头皮模式
            _isScalpingMode = true;
            _scalpingController = new ParallelScalpingController();
            await _scalpingController.StartAsync(accountType);
            LogService.Info("[AutoTrading] 已启动高频剥头皮模式");
        }
        else
        {
            // 标准模式
            _isScalpingMode = false;
            
            // 组装自动化交易管线
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

            // 启动
            string[] syms = (symbols is { Length: > 0 }) ? symbols : new[] { "BTCUSDT" };
            await _automation.StartAsync(syms, accountType);
            LogService.Info("[AutoTrading] 已启动标准模式");
        }
    }

    /// <summary>
    /// 停止 AI 自动化交易
    /// </summary>
    public async Task StopAsync()
    {
        if (_isScalpingMode && _scalpingController != null)
        {
            await _scalpingController.StopAsync();
            _scalpingController = null;
        }
        else if (_automation != null && _automation.IsRunning)
        {
            await _automation.StopAsync();
        }
        
        _isScalpingMode = false;
    }
}
