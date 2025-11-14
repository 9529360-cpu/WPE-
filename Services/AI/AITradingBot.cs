using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Services;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// AI交易机器人 - 集成AI分析、风控、执行的完整系统
/// 已修改为使用可插拔的 ITradingAgent（本地适配器或远端代理）
/// </summary>
public class AITradingBot
{
    private readonly ITradingAgent _aiAgent;
    private readonly MarketDataPreprocessor _dataProcessor;
    private readonly AIRiskManager _riskManager;
    private readonly AIPerformanceTracker _performanceTracker;
    private readonly BinanceApiClient _apiClient;

    private bool _isRunning;
    private CancellationTokenSource? _cts;

    // 新构造：注入 ITradingAgent
    public AITradingBot(
        ITradingAgent tradingAgent,
        BinanceApiClient apiClient,
        DataCacheService cacheService)
    {
        _aiAgent = tradingAgent ?? throw new ArgumentNullException(nameof(tradingAgent));
        _dataProcessor = new MarketDataPreprocessor(apiClient, cacheService);
        _riskManager = new AIRiskManager();
        _performanceTracker = new AIPerformanceTracker();
        _apiClient = apiClient;
    }

    // 兼容旧构造函数：接受 API Key，但仍使用本地适配器（防止外部依赖）
    public AITradingBot(string deepSeekApiKey, BinanceApiClient apiClient, DataCacheService cacheService)
        : this(new LocalAIAgentAdapter(ConfigurationService.GetAIConfig().Temperature), apiClient, cacheService)
    {
        // do nothing else - keep compatibility
    }

    public bool IsRunning => _isRunning;
    public AIPerformanceTracker PerformanceTracker => _performanceTracker;

    /// <summary>
    /// 启动交易机器人
    /// </summary>
    public async Task StartAsync(string symbol, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) { throw new ArgumentException("symbol is required", nameof(symbol)); }

        lock (this)
        {
            if (_isRunning)
            {
                LogService.Warning("AITradingBot.StartAsync called while already running");
                return;
            }
            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        CancellationToken ct = _cts.Token;

        StartupDiagnostics.Log($"AITradingBot: 启动交易机器人 {symbol}, 间隔 {interval.TotalMinutes} 分钟");

        try
        {
            await RunTradingCycleAsync(symbol, interval, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StartupDiagnostics.Log("AITradingBot: 交易机器人已停止");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"AITradingBot Error: {ex.GetBaseException().Message}");
            throw;
        }
        finally
        {
            lock (this)
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    /// <summary>
    /// 停止交易机器人
    /// </summary>
    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"AITradingBot.Stop error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>
    /// 单次分析(不执行交易)
    /// </summary>
    public async Task<AITradingSignal> AnalyzeOnceAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) { throw new ArgumentException("symbol is required", nameof(symbol)); }

        // 1. 收集市场数据
        MarketDataSnapshot marketData = await _dataProcessor.CollectMarketDataAsync(symbol, ct).ConfigureAwait(false);
        if (marketData == null) { throw new InvalidOperationException("无法收集市场数据"); }

        // 2. AI分析
        AITradingSignal signal = await _aiAgent.AnalyzeMarketSituationAsync(marketData, ct).ConfigureAwait(false);

        StartupDiagnostics.Log($"AITradingBot: {symbol} 分析完成 - {signal.Action} (信心度: {signal.Confidence:P0})");

        return signal;
    }

    /// <summary>
    /// 交易循环
    /// </summary>
    private async Task RunTradingCycleAsync(string symbol, TimeSpan interval, CancellationToken ct)
    {
        const int ERROR_COOLDOWN_SECONDS = 30; // 错误后等待时间

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. 收集数据
                MarketDataSnapshot marketData = await _dataProcessor.CollectMarketDataAsync(symbol, ct).ConfigureAwait(false);
                if (marketData == null)
                {
                    StartupDiagnostics.Log($"AITradingBot: 未能获取市场数据, 跳过本轮 {symbol}");
                    await Task.Delay(TimeSpan.FromSeconds(ERROR_COOLDOWN_SECONDS), ct).ConfigureAwait(false);
                    continue;
                }

                // 2. AI分析
                AITradingSignal signal = await _aiAgent.AnalyzeMarketSituationAsync(marketData, ct).ConfigureAwait(false);
                if (signal == null)
                {
                    StartupDiagnostics.Log($"AITradingBot: AI未返回信号, 跳过本轮 {symbol}");
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    continue;
                }

                // 3. 风控检查
                if (_riskManager.ApproveSignal(signal))
                {
                    // 4. 执行交易
                    TradeExecutionResult result = await ExecuteTradeAsync(signal, ct).ConfigureAwait(false);

                    // 5. 记录绩效
                    _performanceTracker.RecordSignal(signal, result);

                    StartupDiagnostics.Log($"AITradingBot: 交易执行 {signal.Action} @ {signal.EntryPrice:F4}, 结果: {result.Status}");
                }
                else
                {
                    StartupDiagnostics.Log($"AITradingBot: 信号被风控拒绝 - {signal.Action} (信心度: {signal.Confidence:P0})");
                }

                // 6. 等待下一个周期
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log($"AITradingBot Cycle Error: {ex.GetBaseException().Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(ERROR_COOLDOWN_SECONDS), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 执行交易
    /// </summary>
    private async Task<TradeExecutionResult> ExecuteTradeAsync(AITradingSignal signal, CancellationToken ct)
    {
        try
        {
            if (signal.Action == SignalAction.Hold)
            {
                return new TradeExecutionResult
                {
                    Status = "SKIPPED",
                    Message = "Hold信号,不执行交易",
                    ExecutedPrice = 0,
                    ExecutedQuantity = 0
                };
            }

            // 这里集成实际的下单逻辑
            // var order = await _apiClient.PlaceOrderAsync(...);

            return new TradeExecutionResult
            {
                Status = "SUCCESS",
                Message = $"{signal.Action} executed at {signal.EntryPrice:F4}",
                ExecutedPrice = signal.EntryPrice,
                ExecutedQuantity = signal.PositionSize
            };
        }
        catch (Exception ex)
        {
            return new TradeExecutionResult
            {
                Status = "FAILED",
                Message = ex.GetBaseException().Message,
                ExecutedPrice = 0,
                ExecutedQuantity = 0
            };
        }
    }
}

/// <summary>
/// AI风险管理器 - 基于规则的交易信号审批
/// </summary>
/// <remarks>
/// 对AI生成的交易信号进行多层风控检查，确保信号符合风险承受能力
/// </remarks>
public class AIRiskManager
{
    /// <summary>
    /// 风控配置常量
    /// </summary>
    private static class RiskConstants
    {
        /// <summary>
        /// 最低信心度阈值 (70%)
        /// </summary>
        /// <remarks>
        /// AI信号低于此阈值将被拒绝
        /// </remarks>
        public const double MIN_CONFIDENCE_THRESHOLD = 0.70;

        /// <summary>
        /// 最大单次仓位占比 (10%)
        /// </summary>
        /// <remarks>
        /// 单笔交易最多使用总资金的百分比
        /// </remarks>
        public const double MAX_POSITION_SIZE = 0.10;

        /// <summary>
        /// 每日最大亏损限制 (5%)
        /// </summary>
        /// <remarks>
        /// 达到此亏损比例后当日停止交易
        /// </remarks>
        public const double MAX_DAILY_LOSS = 0.05;

        /// <summary>
        /// 最大止损百分比 (3%)
        /// </summary>
        /// <remarks>
        /// 单笔交易的最大止损幅度
        /// </remarks>
        public const double MAX_STOP_LOSS_PERCENT = 0.03;

        /// <summary>
        /// 异常后等待时间 (秒)
        /// </summary>
        public const int ERROR_COOLDOWN_SECONDS = 30;
    }

    private readonly double _minConfidence = RiskConstants.MIN_CONFIDENCE_THRESHOLD;
    private readonly double _maxPositionSize = RiskConstants.MAX_POSITION_SIZE;
    private readonly double _maxDailyLoss = RiskConstants.MAX_DAILY_LOSS;
    private double _dailyPnL = 0;

    /// <summary>
    /// 审批交易信号
    /// </summary>
    /// <param name="signal">AI生成的交易信号</param>
    /// <returns>true表示通过，false表示拒绝</returns>
    public bool ApproveSignal(AITradingSignal signal)
    {
        if (signal == null) { return false; }

        // 1. 信心度检查
        if (double.IsNaN(signal.Confidence) || signal.Confidence < _minConfidence)
        {
            StartupDiagnostics.Log($"RiskCheck: 信心度不足 {signal.Confidence:P0} < {_minConfidence:P0}");
            return false;
        }

        // 2. 仓位检查
        if (double.IsNaN(signal.PositionSize) || signal.PositionSize <= 0 || signal.PositionSize > _maxPositionSize)
        {
            StartupDiagnostics.Log($"RiskCheck: 仓位异常 {signal.PositionSize:P0} > {_maxPositionSize:P0}");
            return false;
        }

        // 3. 每日亏损检查
        if (_dailyPnL < -_maxDailyLoss)
        {
            StartupDiagnostics.Log($"RiskCheck: 达到每日最大亏损 {_dailyPnL:P2}");
            return false;
        }

        // 4. 止损合理性检查
        if (signal.EntryPrice <= 0 || signal.StopLoss <= 0)
        {
            StartupDiagnostics.Log($"RiskCheck: 价格数据无效 Entry={signal.EntryPrice}, StopLoss={signal.StopLoss}");
            return false;
        }

        double riskPercent = Math.Abs(signal.EntryPrice - signal.StopLoss) / signal.EntryPrice;
        if (riskPercent > RiskConstants.MAX_STOP_LOSS_PERCENT)
        {
            StartupDiagnostics.Log($"RiskCheck: 止损过大 {riskPercent:P2} > {RiskConstants.MAX_STOP_LOSS_PERCENT:P2}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 更新每日盈亏
    /// </summary>
    /// <param name="pnl">本次交易盈亏</param>
    public void UpdateDailyPnL(double pnl)
    {
        _dailyPnL += pnl;
    }

    /// <summary>
    /// 重置每日盈亏(每日UTC 00:00调用)
    /// </summary>
    public void ResetDailyPnL()
    {
        _dailyPnL = 0;
    }
}

/// <summary>
/// AI绩效追踪器
/// </summary>
public class AIPerformanceTracker
{
    private readonly List<SignalRecord> _signalHistory = new();

    public int TotalSignals => _signalHistory.Count;
    public int ExecutedTrades => _signalHistory.Count(s => s.Result.Status == "SUCCESS");
    public double WinRate => CalculateWinRate();
    public double AverageConfidence => _signalHistory.Any() ? _signalHistory.Average(s => s.Signal.Confidence) : 0;

    public void RecordSignal(AITradingSignal signal, TradeExecutionResult result)
    {
        if (signal == null || result == null) { return; }

        _signalHistory.Add(new SignalRecord
        {
            Signal = signal,
            Result = result,
            Timestamp = DateTime.UtcNow
        });

        // 只保留最近1000条
        if (_signalHistory.Count > 1000)
        {
            _signalHistory.RemoveAt(0);
        }
    }

    public IReadOnlyList<SignalRecord> GetRecentSignals(int count = 10)
    {
        return _signalHistory.TakeLast(count).ToList();
    }

    private double CalculateWinRate()
    {
        var executed = _signalHistory.Where(s => s.Result.Status == "SUCCESS").ToList();
        if (executed.Count == 0)
        {
            return 0;
        }

        // 简化:实际应根据真实盈亏计算
        int wins = executed.Count(s => s.Signal.Confidence > 0.80);
        return (double)wins / executed.Count;
    }
}

/// <summary>
/// 交易执行结果
/// </summary>
public class TradeExecutionResult
{
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double ExecutedPrice { get; init; }
    public double ExecutedQuantity { get; init; }
}

/// <summary>
/// 信号记录
/// </summary>
public class SignalRecord
{
    public AITradingSignal Signal { get; init; } = null!;
    public TradeExecutionResult Result { get; init; } = null!;
    public DateTime Timestamp { get; init; }
}
