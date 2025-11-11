using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

/// <summary>
/// 交易信号广播服务 - 负责将AI生成的信号实时广播到所有订阅者
/// 采用发布-订阅模式,实现模块间解耦
/// </summary>
public sealed class SignalBroadcaster
{
    private static SignalBroadcaster? _instance;
    private static readonly object _lock = new();

    private readonly ConcurrentDictionary<string, List<Action<TradingSignalEvent>>> _subscribers = new();
    private readonly ConcurrentQueue<TradingSignalEvent> _signalHistory = new();
    private const int MaxHistorySize = 1000;

    private SignalBroadcaster() { }

    /// <summary>
    /// 单例实例
    /// </summary>
    public static SignalBroadcaster Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new SignalBroadcaster();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 订阅交易信号
    /// </summary>
    /// <param name="subscriberId">订阅者ID (通常是View的名称)</param>
    /// <param name="callback">信号回调</param>
    public void Subscribe(string subscriberId, Action<TradingSignalEvent> callback)
    {
        _subscribers.AddOrUpdate(
            subscriberId,
            _ => new List<Action<TradingSignalEvent>> { callback },
            (_, list) =>
            {
                list.Add(callback);
                return list;
            });

        LogService.Info($"[SignalBroadcaster] 订阅者 {subscriberId} 已注册");
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public void Unsubscribe(string subscriberId)
    {
        _subscribers.TryRemove(subscriberId, out _);
        LogService.Info($"[SignalBroadcaster] 订阅者 {subscriberId} 已取消订阅");
    }

    /// <summary>
    /// 广播交易信号到所有订阅者
    /// </summary>
    public void BroadcastSignal(AITradingSignal signal, string source = "AI")
    {
        var signalEvent = new TradingSignalEvent
        {
            SignalId = Guid.NewGuid().ToString(),
            Timestamp = signal.Timestamp,
            Source = source,
            Symbol = signal.Symbol,
            Action = signal.Action.ToString(),
            Confidence = signal.Confidence,
            EntryPrice = signal.EntryPrice,
            TargetPrice = signal.TargetPrice,
            StopLoss = signal.StopLoss,
            PositionSize = signal.PositionSize,
            Timeframe = signal.Timeframe,
            RiskLevel = signal.RiskLevel,
            Reason = signal.Reason,
            TechnicalIndicators = new Dictionary<string, double>() // 🔧 暂时设为空字典,后续扩展
        };

        // 保存到历史记录
        _signalHistory.Enqueue(signalEvent);
        while (_signalHistory.Count > MaxHistorySize)
        {
            _signalHistory.TryDequeue(out _);
        }

        // 广播到所有订阅者
        int subscriberCount = 0;
        foreach (var (subscriberId, callbacks) in _subscribers)
        {
            foreach (var callback in callbacks)
            {
                try
                {
                    callback(signalEvent);
                    subscriberCount++;
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, $"[SignalBroadcaster] 广播信号到 {subscriberId} 失败");
                }
            }
        }

        LogService.Info($"[SignalBroadcaster] 信号已广播: {signal.Symbol} {signal.Action} (置信度: {signal.Confidence:P0}) -> {subscriberCount} 个订阅者");
    }

    /// <summary>
    /// 获取最近的信号历史
    /// </summary>
    public IReadOnlyList<TradingSignalEvent> GetRecentSignals(int count = 50)
    {
        return _signalHistory.Reverse().Take(count).ToList();
    }

    /// <summary>
    /// 获取特定交易对的信号历史
    /// </summary>
    public IReadOnlyList<TradingSignalEvent> GetSignalsForSymbol(string symbol, int count = 50)
    {
        return _signalHistory
            .Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// 清空历史记录
    /// </summary>
    public void ClearHistory()
    {
        while (_signalHistory.TryDequeue(out _))
        { }
        LogService.Info("[SignalBroadcaster] 历史记录已清空");
    }

    /// <summary>
    /// 获取订阅者数量
    /// </summary>
    public int GetSubscriberCount() => _subscribers.Count;
}

/// <summary>
/// 交易信号事件
/// </summary>
public class TradingSignalEvent
{
    public string SignalId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public double EntryPrice { get; set; }
    public double TargetPrice { get; set; }
    public double StopLoss { get; set; }
    public double PositionSize { get; set; }
    public string Timeframe { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Dictionary<string, double> TechnicalIndicators { get; set; } = new();

    /// <summary>
    /// 潜在收益比
    /// </summary>
    public double ProfitPotential => TargetPrice > 0 && EntryPrice > 0
        ? (TargetPrice - EntryPrice) / EntryPrice
        : 0;

    /// <summary>
    /// 风险回报比
    /// </summary>
    public double RiskRewardRatio
    {
        get
        {
            if (EntryPrice == 0 || StopLoss == 0)
            {
                return 0;
            }

            double risk = Math.Abs(EntryPrice - StopLoss);
            double reward = Math.Abs(TargetPrice - EntryPrice);
            return risk > 0 ? reward / risk : 0;
        }
    }

    /// <summary>
    /// 信号年龄(秒)
    /// </summary>
    public double AgeInSeconds => (DateTime.UtcNow - Timestamp).TotalSeconds;

    /// <summary>
    /// 是否是新信号(5分钟内)
    /// </summary>
    public bool IsNew => AgeInSeconds < 300;
}
