using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 本地替代的 DeepSeekTradingAgent 实现。
/// 该实现不依赖外部网络，内部委托给 LocalAIAgentAdapter 执行规则引擎分析并提供对话模拟。
/// 保持与原类型签名兼容以减少对代码其余部分的修改。
/// </summary>
public class DeepSeekTradingAgent : ITradingAgent
{
    private readonly LocalAIAgentAdapter _local;
    public bool IsEnabled { get; }

    public DeepSeekTradingAgent(string apiKey = "", double temperature = 0.3, System.Net.Http.HttpClient? httpClient = null)
    {
        // 忽略外部 API key，始终使用本地适配器
        _local = new LocalAIAgentAdapter(temperature);
        IsEnabled = true; // 标记为可用（本地模式）
    }

    public Task<AITradingSignal> AnalyzeMarketSituationAsync(MarketDataSnapshot marketData, CancellationToken ct = default)
    {
        return _local.AnalyzeMarketSituationAsync(marketData, ct);
    }

    public Task<string> ChatAsync(string userMessage, string? context = null, CancellationToken ct = default)
    {
        // 简单的本地对话模拟：基于关键字返回简要建议
        try
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return Task.FromResult("请提供问题或指令。");
            }

            string lower = userMessage.Trim().ToLowerInvariant();
            if (lower.Contains("买") || lower.Contains("long") || lower.Contains("buy"))
            {
                return Task.FromResult("建议：如果信心度高，分批建仓；否则观望。💡");
            }

            if (lower.Contains("卖") || lower.Contains("short") || lower.Contains("sell"))
            {
                return Task.FromResult("建议：考虑减仓或设定止盈止损；关注关键支撑/阻力。📉");
            }

            if (lower.Contains("rsi") || lower.Contains("macd") || lower.Contains("指标"))
            {
                return Task.FromResult("技术指标说明：RSI<30超卖，>70超买；MACD柱上涨表明动量加强。📊");
            }

            return Task.FromResult("本地AI：已收到指令，当前处于本地模式。请使用“分析市场”或具体交易对查询。🤖");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[DeepSeekTradingAgent] 本地 ChatAsync 失败");
            return Task.FromResult("本地AI对话出错");
        }
    }

    public Task ValidateAccessAsync(CancellationToken ct = default)
    {
        // 本地模式无需外部访问，始终通过
        return Task.CompletedTask;
    }
}

/// <summary>
/// 市场数据快照
/// </summary>
public class MarketDataSnapshot
{
    public required string Symbol { get; init; }
    public double CurrentPrice { get; init; }
    public double PriceChangePercent { get; init; }
    public double HighPrice { get; init; }
    public double LowPrice { get; init; }
    public double Volume { get; init; }
    public double RSI { get; init; }
    public double MACD { get; init; }
    public double MACDSignal { get; init; }
    public double BBUpper { get; init; }
    public double BBMiddle { get; init; }
    public double BBLower { get; init; }
    public double VolumeMA { get; init; }
    public double FundingRate { get; init; }
    public double OpenInterest { get; init; }
    public double LongShortRatio { get; init; }
}

/// <summary>
/// AI 交易信号
/// </summary>
public class AITradingSignal
{
    public required string Symbol { get; init; }
    public SignalAction Action { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double EntryPrice { get; init; }
    public double TargetPrice { get; init; }
    public double StopLoss { get; init; }
    public double PositionSize { get; init; }
    public string Timeframe { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string RawAnalysis { get; init; } = string.Empty;
}

public enum SignalAction
{
    Hold,
    Buy,
    Sell
}
