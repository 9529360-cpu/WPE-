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
    private readonly 币安量化机器人.Services.AI.ITradingAgent _aiAgent;
    private readonly AIOrderExecutionEngine _executionEngine;
    private readonly TradingAccountManager _accountManager;
    private readonly PositionManager _positionManager;
    private readonly SignalBroadcaster _signalBroadcaster;

    private bool _isRunning;
    private CancellationTokenSource? _cts;
    private Task? _positionMonitorTask;
    private readonly object _sync = new();

    // 由中央协调器注入
    private EventBus? _eventBus;
    public void SetEventBus(EventBus bus) => _eventBus = bus;

    public AITradingAutomation(
        BinanceStreamClient streamClient,
        MarketDataPreprocessor dataProcessor,
        币安量化机器人.Services.AI.ITradingAgent aiAgent,
        AIOrderExecutionEngine executionEngine,
        TradingAccountManager accountManager,
        PositionManager positionManager)
    {
        _streamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
        _dataProcessor = dataProcessor ?? throw new ArgumentNullException(nameof(dataProcessor));
        _aiAgent = aiAgent ?? throw new ArgumentNullException(nameof(aiAgent));
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
        _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        _positionManager = positionManager ?? throw new ArgumentNullException(nameof(positionManager));
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
        lock (_sync)
        {
            if (_isRunning)
            {
                LogService.Warning("AITradingAutomation.StartAsync called while already running");
                return; // idempotent
            }
            _isRunning = true;
            _cts = new CancellationTokenSource();
        }

        CancellationToken token = _cts.Token;

        // 切换到指定账户
        _accountManager.SwitchAccount(accountType);

        string[] defaultSymbols = new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "ADAUSDT", "XRPUSDT" };
        var subscribeSymbols = (symbols == null || symbols.Length == 0) ? defaultSymbols : symbols;

        LogService.Info("AI自动交易启动: 账户={Account}, 交易对={Symbols}",
            accountType, string.Join(',', subscribeSymbols));

        try
        {
            // 启动仓位监控并记录任务以观察异常
            _positionMonitorTask = _positionManager.StartMonitoringAsync(token);
            _positionMonitorTask.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    LogService.Error(t.Exception.GetBaseException(), "Position monitor task failed");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            // 订阅WebSocket行情 (MiniTicker)
            _streamClient.MiniTickerReceived += OnMiniTickerReceived;
            await _streamClient.ConnectMiniTickerAsync(subscribeSymbols, token).ConfigureAwait(false);

            if (_eventBus != null)
            {
                foreach (var s in subscribeSymbols)
                {
                    // publish without blocking start
                    _ = _eventBus.PublishAsync(new MarketStreamSubscriptionRequestEvent
                    {
                        Symbol = s,
                        SubscribeFundingRate = true,
                        SubscribeOpenInterest = true,
                        SubscribeLongShortRatio = true
                    });
                }
            }

            LogService.Info("WebSocket订阅完成,等待行情数据...");

            // 保持运行直到取消
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogService.Info("AI自动交易停止请求已被取消");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "AI自动交易异常启动或运行期间错误");
            throw;
        }
        finally
        {
            try
            {
                _streamClient.MiniTickerReceived -= OnMiniTickerReceived;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "解绑 MiniTickerReceived 事件时发生错误");
            }

            try
            {
                // 停止监控并等待完成（带超时）
                _positionManager.StopMonitoring();
                if (_positionMonitorTask != null)
                {
                    await Task.WhenAny(_positionMonitorTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "停止 PositionMonitor 时发生错误");
            }

            lock (_sync)
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }

            LogService.Info("AI自动交易已退出运行循环");
        }
    }

    /// <summary>
    /// 停止AI自动交易
    /// </summary>
    public async Task StopAsync()
    {
        lock (_sync)
        {
            if (!_isRunning)
            {
                LogService.Warning("AITradingAutomation.StopAsync called while not running");
                return; // idempotent
            }
        }

        try
        {
            _cts?.Cancel();

            // 请求流客户端停止
            try
            {
                await _streamClient.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "停止流客户端时出错");
            }

            // 停止仓位监控
            try
            {
                _positionManager.StopMonitoring();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "停止仓位监控时出错");
            }

            // 等待监控任务优雅完成
            if (_positionMonitorTask != null)
            {
                try
                {
                    await Task.WhenAny(_positionMonitorTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "等待 position monitor 完成时出错");
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }

            try
            {
                _streamClient.MiniTickerReceived -= OnMiniTickerReceived;
            }
            catch { }

            LogService.Info("AI自动交易已停止");
        }
    }

    /// <summary>
    /// WebSocket MiniTicker回调 - 定时触发AI分析
    /// </summary>
    private DateTime _lastAnalysisTime = DateTime.MinValue;
    private const int AnalysisIntervalSeconds = 60; // 每60秒分析一次

    private async void OnMiniTickerReceived(MiniTickerUpdate ticker)
    {
        // 防止并发或取消触发
        if (!_isRunning || _cts == null || _cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            using var act = Services.Observability.TraceManager.StartActivity("AITradingAutomation.OnTicker", System.Diagnostics.ActivityKind.Consumer);
            Services.Observability.TraceManager.AddTag("symbol", ticker.Symbol);

            // 限制分析频率 (避免太频繁)
            if ((DateTime.UtcNow - _lastAnalysisTime).TotalSeconds < AnalysisIntervalSeconds)
            {
                return;
            }

            _lastAnalysisTime = DateTime.UtcNow;

            LogService.Debug("价格更新: {Symbol} {LastPrice}", ticker.Symbol, ticker.LastPrice);

            // 1. 收集市场数据
            MarketDataSnapshot marketData = await _dataProcessor.CollectMarketDataAsync(ticker.Symbol).ConfigureAwait(false);

            if (marketData == null)
            {
                LogService.Warning("MarketDataSnapshot 为空，跳过分析: {Symbol}", ticker.Symbol);
                return;
            }

            // 2. AI分析生成信号
            AITradingSignal signal = null;
            try
            {
                signal = await _aiAgent.AnalyzeMarketSituationAsync(marketData).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "AI agent 分析失败: {Symbol}", ticker.Symbol);
                return;
            }

            if (signal == null)
            {
                LogService.Warning("AI 未返回信号: {Symbol}", ticker.Symbol);
                return;
            }

            // 基本校验
            if (signal.Confidence < 0 || signal.Confidence > 1)
            {
                LogService.Warning("信心度异常, 忽略信号: {Symbol} {Confidence}", signal.Symbol, signal.Confidence);
                return;
            }

            Services.Observability.TraceManager.AddTag("signal.action", signal.Action);
            Services.Observability.TraceManager.AddTag("signal.confidence", signal.Confidence);

            LogService.Info("AI信号: {Symbol} {Action} 信心度={Confidence:P0} 理由={Reason}",
                signal.Symbol, signal.Action, signal.Confidence, signal.Reason ?? string.Empty);

            // 广播到UI
            _signalBroadcaster.BroadcastSignal(signal, "AITradingAutomation");

            // Persist signal for learning
            try
            {
                _ = ServiceLocator.Cache.SaveSignalAsync(signal.Symbol, signal.Action.ToString(), signal.Confidence, signal.Reason, "AITradingAutomation", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                LogService.Warning("保存信号失败: {Message}", ex.Message);
            }

            if (_eventBus != null)
            {
                _ = _eventBus.PublishAsync(new AITradingSignalGeneratedEvent
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
                result = await _executionEngine.ExecuteSignalAsync(signal).ConfigureAwait(false);
            }
            else
            {
                result = OrderExecutionResult.Rejected("Hold");
            }

            if (_eventBus != null)
            {
                _ = _eventBus.PublishAsync(new AITradingSignalExecutedEvent
                {
                    Signal = signal,
                    Result = result,
                    Timestamp = DateTime.UtcNow
                });
            }

            if (result.IsSuccess)
            {
                LogService.Info("订单执行成功: {OrderId} @ {Price}",
                    result.OrderId ?? string.Empty, result.ExecutedPrice);

                // 保存订单到历史（fire-and-forget, catch errors）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var orderHistory = new OrderHistoryService(ServiceLocator.Cache);
                        long orderIdLong;
                        if (!long.TryParse(result.OrderId, out orderIdLong))
                        {
                            orderIdLong = Math.Abs(result.OrderId?.GetHashCode() ?? Guid.NewGuid().GetHashCode());
                        }

                        await orderHistory.RecordOrderPlacedAsync(new OrderResponse
                        {
                            OrderId = orderIdLong,
                            Symbol = signal.Symbol,
                            Status = "FILLED",
                            ExecutedQuantity = (decimal)result.ExecutedQuantity,
                            Price = (decimal)result.ExecutedPrice,
                            AvgPrice = (decimal)result.ExecutedPrice,
                            Time = DateTime.UtcNow
                        }, strategyName: "AutoTrader").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, "保存订单历史失败");
                    }
                });
            }
            else if (!result.IsSuccess && string.Equals(result.Error, "Hold", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Debug("信号被忽略(Hold)");
            }
            else if (!result.IsSuccess && string.Equals(result.Error, "风控拒绝", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Debug("信号被风控拒绝");
            }
            else if (!result.IsSuccess)
            {
                LogService.Warning("订单执行失败: {Reason}", result.Error ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            try
            {
                LogService.Error(ex, "AI自动交易异常: {Symbol}", ticker.Symbol);
            }
            catch { }
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

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"运行中({account.Type})");
        sb.AppendLine($"├─ 账户: {account.Name}");
        sb.AppendLine($"├─ 净值: {account.NetValue:F2} USDT");
        sb.AppendLine($"├─ 持仓: {account.OpenPositionCount} 个");
        sb.AppendLine($"├─ 今日盈亏: {account.TodayPnL:F2} ({account.TodayReturnPercent:P2})");
        sb.AppendLine($"└─ 总盈亏: {account.TotalPnL:F2} ({account.TotalReturnPercent:P2})");

        return sb.ToString();
    }
}
