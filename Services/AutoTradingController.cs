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
    private readonly object _sync = new();

    public bool IsRunning => _isScalpingMode
        ? _scalpingController?.IsRunning == true
        : _automation?.IsRunning == true;

    // Events for UI
    public event Action? Started;
    public event Action? Stopped;
    public event Action<string>? OrderPlaced; // message
    public event Action<string>? OrderUpdated; // message

    private void RaiseStarted() => Started?.Invoke();
    private void RaiseStopped() => Stopped?.Invoke();
    private void RaiseOrderPlaced(string msg) => OrderPlaced?.Invoke(msg);
    private void RaiseOrderUpdated(string msg) => OrderUpdated?.Invoke(msg);

    /// <summary>
    /// 启动 AI 自动化交易
    /// </summary>
    /// <param name="symbols">交易对数组，默认 BTCUSDT</param>
    /// <param name="accountType">账户类型，默认模拟账户</param>
    /// <param name="enableScalping">启用高频剥头皮模式</param>
    public async Task StartAsync(string[]? symbols = null, AccountType accountType = AccountType.Simulated, bool enableScalping = false, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                LogService.Warning("AutoTradingController: StartAsync called while already running");
                return;
            }
        }

        // honor caller cancellation early
        if (cancellationToken.IsCancellationRequested)
        {
            LogService.Warning("AutoTradingController: StartAsync canceled by caller token");
            return;
        }

        // 1) 准备 Binance API 凭证
        (string apiKey, string secretKey) = ConfigurationService.GetBinanceCredentials();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            LogService.Error("AutoTradingController: Binance API 未配置");
            throw new InvalidOperationException("Binance API 未配置，请在 [设置 > API 管理] 中保存 API Key/Secret");
        }

        // 2) 设置到全局 Binance 客户端
        try
        {
            ServiceLocator.Api.SetApiCredentials(apiKey, secretKey);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "AutoTradingController: 设置 Binance 凭证失败");
            throw;
        }

        // 3) 准备 DeepSeek API Key
        string deepSeekKey = ConfigurationService.GetDeepSeekApiKey();
        if (string.IsNullOrWhiteSpace(deepSeekKey))
        {
            LogService.Error("AutoTradingController: DeepSeek API 未配置");
            throw new InvalidOperationException("DeepSeek API 未配置，请在 [设置 > API 管理] 中保存 DeepSeek API Key");
        }

        // 4) 根据模式选择启动方式
        if (enableScalping)
        {
            lock (_sync)
            {
                _isScalpingMode = true;
                if (_scalpingController == null)
                {
                    _scalpingController = new ParallelScalpingController();
                }
            }

            try
            {
                await _scalpingController!.StartAsync(accountType).ConfigureAwait(false);
                LogService.Info("[AutoTrading] 已启动高频剥头皮模式");
                RaiseStarted();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "AutoTradingController: 启动高频剥头皮模式失败");
                throw;
            }
        }
        else
        {
            lock (_sync)
            {
                _isScalpingMode = false;
            }

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

            // subscribe automation events if available (AITradingAutomation currently does not expose OrderPlaced/OrderUpdated events)
            // if such events are added in future, subscribe here to forward to UI

            // 非阻塞启动自动化（在后台运行）
            string[] syms = (symbols is { Length: > 0 }) ? symbols : new[] { "BTCUSDT" };
            try
            {
                _ = Task.Run(() => _automation.StartAsync(syms, accountType), cancellationToken);
                LogService.Info("[AutoTrading] 已启动标准模式（后台任务）");
                RaiseStarted();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "AutoTradingController: 启动标准模式失败");
                throw;
            }
        }
    }

    /// <summary>
    /// 停止 AI 自动化交易
    /// </summary>
    public async Task StopAsync()
    {
        lock (_sync)
        {
            if (!IsRunning)
            {
                LogService.Warning("AutoTradingController: StopAsync called while not running");
                _isScalpingMode = false;
                return;
            }
        }

        if (_isScalpingMode && _scalpingController != null)
        {
            try
            {
                await _scalpingController.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "AutoTradingController: 停止剥头皮控制器失败");
            }
            finally
            {
                lock (_sync)
                {
                    _scalpingController = null;
                    _isScalpingMode = false;
                }
                RaiseStopped();
            }
        }
        else if (_automation != null && _automation.IsRunning)
        {
            try
            {
                await _automation.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "AutoTradingController: 停止自动化失败");
            }
            finally
            {
                lock (_sync)
                {
                    _automation = null;
                    _isScalpingMode = false;
                }
                RaiseStopped();
            }
        }
    }
}
