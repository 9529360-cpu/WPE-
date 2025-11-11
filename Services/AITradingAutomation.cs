using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

public class AITradingAutomation
{
    private readonly BinanceStreamClient _streamClient;
    private readonly MarketDataPreprocessor _dataProcessor;
    private readonly DeepSeekTradingAgent _aiAgent;
    private readonly AIOrderExecutionEngine _executionEngine;
    private readonly TradingAccountManager _accountManager;
    private readonly PositionManager _positionManager;
    private readonly SignalBroadcaster _signalBroadcaster;

    private bool _isRunning;
    private CancellationTokenSource? _cts;

    // 由中央协调器注入
    private EventBus? _eventBus;
    public void SetEventBus(EventBus bus) => _eventBus = bus;

    public AITradingAutomation(
        BinanceStreamClient streamClient,
        MarketDataPreprocessor dataProcessor,
        DeepSeekTradingAgent aiAgent,
        AIOrderExecutionEngine executionEngine,
        TradingAccountManager accountManager,
        PositionManager positionManager)
    {
        _streamClient = streamClient;
        _dataProcessor = dataProcessor;
        _aiAgent = aiAgent;
        _executionEngine = executionEngine;
        _accountManager = accountManager;
        _positionManager = positionManager;
        _signalBroadcaster = SignalBroadcaster.Instance;
    }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 启动AI自动交易 (WebSocket驱动)
    /// </summary>
    /// <param name="symbols">监控的交易对</param>
    /// <param name="accountType">账户类型 (模拟/真实)</param>
    public async Task StartAsync(string[] symbols, AccountType accountType = AccountType.Simulated)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("AI自动交易已在运行");
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();

        // 切换到指定账户
        _accountManager.SwitchAccount(accountType);

        LogService.Info("🤖 AI自动交易启动: 账户={Account}, 交易对={Symbols}",
            accountType, string.Join(",", symbols ?? Array.Empty<string>()));

        try
        {
            // 启动仓位监控
            _ = _positionManager.StartMonitoringAsync(_cts.Token);

            // 订阅WebSocket行情 (MiniTicker)
            _streamClient.MiniTickerReceived += OnMiniTickerReceived;
            await _streamClient.ConnectMiniTickerAsync(symbols ?? Array.Empty<string>(), _cts.Token);

            LogService.Info("✅ WebSocket订阅完成,等待行情数据...");

            // 保持运行
            await Task.Delay(Timeout.Infinite, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            LogService.Info("AI自动交易已停止");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "AI自动交易异常");
            throw;
        }
        finally
        {
            _streamClient.MiniTickerReceived -= OnMiniTickerReceived;
            _positionManager.StopMonitoring();
            _isRunning = false;
        }
    }

    /// <summary>
    /// 停止AI自动交易
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        await _streamClient.StopAsync();
        _isRunning = false;
        LogService.Info("🛑 AI自动交易已停止");
    }

    /// <summary>
    /// WebSocket MiniTicker回调 - 定时触发AI分析
    /// </summary>
    private DateTime _lastAnalysisTime = DateTime.MinValue;
    private const int AnalysisIntervalSeconds = 60; // 每60秒分析一次

    private async void OnMiniTickerReceived(MiniTickerUpdate ticker)
    {
        try
        {
            // 限制分析频率 (避免太频繁)
            if ((DateTime.UtcNow - _lastAnalysisTime).TotalSeconds < AnalysisIntervalSeconds)
            {
                return;
            }

            _lastAnalysisTime = DateTime.UtcNow;

            LogService.Debug("📊 价格更新: {Symbol} {LastPrice}", ticker.Symbol, ticker.LastPrice);

            // 1. 收集市场数据
            MarketDataSnapshot marketData = await _dataProcessor.CollectMarketDataAsync(ticker.Symbol);

            // 2. AI分析生成信号
            AITradingSignal signal = await _aiAgent.AnalyzeMarketSituationAsync(marketData);

            LogService.Info("🧠 AI信号: {Symbol} {Action} 信心度={Confidence:P0} 理由={Reason}",
                signal.Symbol, signal.Action, signal.Confidence, signal.Reason ?? string.Empty);

            // 广播到UI
            _signalBroadcaster.BroadcastSignal(signal, "AITradingAutomation");

            if (_eventBus != null)
            {
                await _eventBus.PublishAsync(new AITradingSignalGeneratedEvent
                {
                    Signal = signal,
                    Source = "AITradingAutomation",
                    Timestamp = DateTime.UtcNow
                });
            }

            // 4. 执行交易 (如果不是Hold)
            OrderExecutionResult result;
            if (signal.Action != SignalAction.Hold)
            {
                result = await _executionEngine.ExecuteSignalAsync(signal);
            }
            else
            {
                result = OrderExecutionResult.Rejected("Hold");
            }

            if (_eventBus != null)
            {
                await _eventBus.PublishAsync(new AITradingSignalExecutedEvent
                {
                    Signal = signal,
                    Result = result,
                    Timestamp = DateTime.UtcNow
                });
            }

            if (result.IsSuccess)
            {
                LogService.Info("✅ 订单执行成功: {OrderId} @ {Price}",
                    result.OrderId ?? string.Empty, result.ExecutedPrice);
            }
            else if (!result.IsSuccess && string.Equals(result.Error, "Hold", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Debug("⏸️ 信号被忽略(Hold)");
            }
            else if (!result.IsSuccess && string.Equals(result.Error, "风控拒绝", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Debug("⏸️ 信号被风控拒绝");
            }
            else if (!result.IsSuccess)
            {
                LogService.Warning("❌ 订单执行失败: {Reason}", result.Error ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "AI自动交易异常: {Symbol}", ticker.Symbol);
        }
    }

    /// <summary>
    /// 获取运行状态
    /// </summary>
    public string GetStatus()
    {
        if (!_isRunning)
        {
            return "已停止";
        }

        TradingAccount? account = _accountManager.ActiveAccount;
        if (account == null)
        {
            return "无激活账户";
        }

        return $"""
            运行中 ({account.Type})
            ├─ 账户: {account.Name}
            ├─ 净值: {account.NetValue:F2} USDT
            ├─ 持仓: {account.OpenPositionCount}个
            ├─ 今日盈亏: {account.TodayPnL:F2} ({account.TodayReturnPercent:P2})
            └─ 总盈亏: {account.TotalPnL:F2} ({account.TotalReturnPercent:P2})
            """;
    }
}
