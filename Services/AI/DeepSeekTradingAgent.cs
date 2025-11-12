using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

public class DeepSeekTradingAgent
{
    private const string BaseUrl = "https://api.deepseek.com/v1/chat/completions";
    private const string ModelsUrl = "https://api.deepseek.com/v1/models"; // 🆕 for validation
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly double _temperature;

    public bool IsEnabled { get; }

    public DeepSeekTradingAgent(string apiKey, double temperature = 0.3, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? string.Empty;
        _temperature = temperature;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        IsEnabled = !string.IsNullOrWhiteSpace(_apiKey);
    }

    /// <summary>
    /// 分析市场情况并生成交易信号
    /// </summary>
    public async Task<AITradingSignal> AnalyzeMarketSituationAsync(
        MarketDataSnapshot marketData,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("DeepSeek AI agent 未启用或未配置 API Key");
        }

        string prompt = BuildTradingPrompt(marketData);

        var request = new
        {
            model = "deepseek-chat",
            messages = new[]
            {
                new { role = "system", content = GetSystemPrompt() },
                new { role = "user", content = prompt }
            },
            temperature = _temperature,
            max_tokens = 1000
        };

        string json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // 🔧 修复：使用 HttpRequestMessage 显式设置请求头
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        httpRequest.Content = content;

        // 🔧 确保 API Key 只包含 ASCII 字符
        string cleanApiKey = ValidateAndCleanApiKey(_apiKey);
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cleanApiKey}");
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync(ct);
        return ParseTradingSignal(responseJson, marketData.Symbol);
    }

    /// <summary>
    /// 🔧 对话模式：直接回答用户问题
    /// </summary>
    public async Task<string> ChatAsync(
        string userMessage,
        string? context = null,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("DeepSeek AI agent 未启用或未配置 API Key");
        }

        try
        {
            string systemPrompt = """
            你是一个专业的加密货币交易助手，精通技术分析、风险管理和交易策略。
            
            你的职责：
            1. 回答用户关于交易的问题
            2. 提供市场分析和建议
            3. 解释技术指标和交易策略
            4. 评估风险和收益
            
            回答要求：
            - 简洁明了，重点突出
            - 使用中文回答
            - 必要时使用 Emoji 增强表达
            - 给出可操作的建议
            """;

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            if (!string.IsNullOrEmpty(context))
            {
                messages.Add(new { role = "assistant", content = $"当前上下文:\n{context}" });
            }

            messages.Add(new { role = "user", content = userMessage });

            var request = new
            {
                model = "deepseek-chat",
                messages = messages.ToArray(),
                temperature = _temperature,
                max_tokens = 2000
            };

            string json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 🔧 修复：使用 HttpRequestMessage 显式设置请求头
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            httpRequest.Content = content;

            // 🔧 确保 API Key 只包含 ASCII 字符
            string cleanApiKey = ValidateAndCleanApiKey(_apiKey);
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cleanApiKey}");
            httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

            LogService.Info("[DeepSeekTradingAgent] 发送请求到 DeepSeek API, 消息: {Message}", userMessage.Substring(0, Math.Min(50, userMessage.Length)));

            HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync(ct);
                LogService.Error("[DeepSeekTradingAgent] API 请求失败: {StatusCode}, 错误内容: {ErrorContent}",
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"DeepSeek API 返回错误: {response.StatusCode} - {errorContent}");
            }

            string responseJson = await response.Content.ReadAsStringAsync(ct);

            LogService.Info("[DeepSeekTradingAgent] 收到 DeepSeek API 响应，长度: {Length}", responseJson.Length);

            var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "AI 未返回响应";
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[DeepSeekTradingAgent] ChatAsync 失败");
            throw;
        }
    }

    /// <summary>
    /// 🔧 验证并清理 API Key（确保只包含 ASCII 字符）
    /// </summary>
    private static string ValidateAndCleanApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException("API Key 不能为空", nameof(apiKey));
        }

        // 检查是否包含非 ASCII 字符
        if (apiKey.Any(c => c > 127))
        {
            LogService.Warning("[DeepSeekTradingAgent] API Key 包含非 ASCII 字符，已自动过滤");
            // 过滤非 ASCII 字符
            apiKey = new string(apiKey.Where(c => c <= 127).ToArray());
        }

        // 移除前后空格
        apiKey = apiKey.Trim();

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException("API Key 清理后为空，可能包含无效字符", nameof(apiKey));
        }

        return apiKey;
    }

    /// <summary>
    /// 构建交易分析提示词
    /// </summary>
    private static string BuildTradingPrompt(MarketDataSnapshot data)
    {
        return $"""
        作为量化交易AI，请分析以下市场数据并给出交易建议：
        
        【价格数据】
        - 交易对: {data.Symbol}
        - 最新价: {data.CurrentPrice:F4} USDT
        - 24h涨跌: {data.PriceChangePercent:+0.00;-0.00}%
        - 24h最高: {data.HighPrice:F4}
        - 24h最低: {data.LowPrice:F4}
        - 24h成交量: {data.Volume:F2}
        
        【技术指标】
        - RSI(14): {data.RSI:F2}
        - MACD: {data.MACD:F4}
        - 信号线: {data.MACDSignal:F4}
        - 布林带上轨: {data.BBUpper:F4}
        - 布林带中轨: {data.BBMiddle:F4}
        - 布林带下轨: {data.BBLower:F4}
        - 成交量MA(20): {data.VolumeMA:F2}
        
        【市场情绪】
        - 资金费率: {data.FundingRate:P4} (8h)
        - 持仓量: {data.OpenInterest:F2}
        - 多空比: {data.LongShortRatio:F2}
        
        请严格按照以下格式回复（不要添加其他内容）:
        
        SIGNAL: BUY/SELL/HOLD
        CONFIDENCE: 0.70
        REASON: 详细分析理由（限200字）
        ENTRY_PRICE: {data.CurrentPrice:F4}
        TARGET_PRICE: 目标价格
        STOP_LOSS: 止损价格
        POSITION_SIZE: 0.01-0.10 (建议仓位占比)
        TIMEFRAME: SHORT/MEDIUM/LONG (预期持仓时间)
        RISK_LEVEL: LOW/MEDIUM/HIGH
        """;
    }

    /// <summary>
    /// 系统提示词 - 定义AI角色
    /// </summary>
    private static string GetSystemPrompt()
    {
        return """
        你是一个专业的量化交易分析师和AI交易系统。你的职责是：
        
        1. **技术分析**: 精通K线形态、技术指标（RSI、MACD、布林带等）
        2. **风险控制**: 严格控制风险，设置合理的止损和止盈
        3. **市场情绪**: 理解资金费率、持仓量、多空比等市场情绪指标
        4. **趋势判断**: 准确识别趋势反转和延续信号
        5. **资金管理**: 根据市场波动性调整仓位大小
        
        **交易原则**:
        - 只在高确信度(>0.70)时给出BUY/SELL信号
        - 确信度<0.70时建议HOLD
        - 止损设置不超过3%
        - 盈亏比至少1:2
        - 考虑市场流动性和滑点
        
        **信号含义**:
        - BUY: 看涨，建议开多或加仓
        - SELL: 看跌，建议开空或减仓
        - HOLD: 观望，不建议操作
        
        请务必按照指定格式回复，不要添加额外解释。
        """;
    }

    /// <summary>
    /// 解析AI回复为交易信号
    /// </summary>
    private AITradingSignal ParseTradingSignal(string responseJson, string symbol)
    {
        var doc = JsonDocument.Parse(responseJson);
        string content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        string[] lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in lines)
        {
            string[] parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                data[parts[0]] = parts[1];
            }
        }

        return new AITradingSignal
        {
            Symbol = symbol,
            Action = ParseAction(data.GetValueOrDefault("SIGNAL", "HOLD")),
            Confidence = ParseDouble(data.GetValueOrDefault("CONFIDENCE", "0")),
            Reason = data.GetValueOrDefault("REASON", "未提供理由"),
            EntryPrice = ParseDouble(data.GetValueOrDefault("ENTRY_PRICE", "0")),
            TargetPrice = ParseDouble(data.GetValueOrDefault("TARGET_PRICE", "0")),
            StopLoss = ParseDouble(data.GetValueOrDefault("STOP_LOSS", "0")),
            PositionSize = ParseDouble(data.GetValueOrDefault("POSITION_SIZE", "0.01")),
            Timeframe = data.GetValueOrDefault("TIMEFRAME", "MEDIUM"),
            RiskLevel = data.GetValueOrDefault("RISK_LEVEL", "MEDIUM"),
            Timestamp = DateTime.UtcNow,
            RawAnalysis = content
        };
    }

    private static SignalAction ParseAction(string text)
    {
        return text.ToUpperInvariant() switch
        {
            "BUY" => SignalAction.Buy,
            "SELL" => SignalAction.Sell,
            _ => SignalAction.Hold
        };
    }

    private static double ParseDouble(string text)
    {
        // 移除百分号和其他符号
        text = text.Replace("%", "").Replace(",", "").Trim();
        return double.TryParse(text, out double result) ? result : 0;
    }

    /// <summary>
    /// 🆕 预检访问权限与额度：尝试访问 /v1/models，捕获 401/402 等错误
    /// </summary>
    public async Task ValidateAccessAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("DeepSeek AI 未启用或未配置 API Key");
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, ModelsUrl);
        string cleanApiKey = ValidateAndCleanApiKey(_apiKey);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cleanApiKey}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await _httpClient.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[DeepSeekTradingAgent] ValidateAccessAsync 请求失败");
            throw;
        }

        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            string message = $"DeepSeek 访问失败: {(int)resp.StatusCode} {resp.StatusCode} - {body}";
            throw new HttpRequestException(message);
        }
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
/// AI交易信号
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

/// <summary>
/// 信号动作
/// </summary>
public enum SignalAction
{
    Hold,
    Buy,
    Sell
}
