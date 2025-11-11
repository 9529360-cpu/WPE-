namespace 币安量化机器人.Models.Configuration;

/// <summary>
/// API配置 - Binance API连接参数
/// </summary>
/// <remarks>
/// 配置Binance REST和WebSocket端点、超时、重试等参数
/// </remarks>
public class ApiConfig
{
    /// <summary>
    /// Binance合约REST端点
    /// </summary>
    /// <remarks>
    /// 生产环境: https://fapi.binance.com
    /// 测试网: https://testnet.binancefuture.com
    /// </remarks>
    public string RestEndpoint { get; init; } = "https://fapi.binance.com";

    /// <summary>
    /// Binance合约WebSocket端点
    /// </summary>
    /// <remarks>
    /// 生产环境: wss://fstream.binance.com/stream
    /// 测试网: wss://stream.binancefuture.com/stream
    /// </remarks>
    public string StreamEndpoint { get; init; } = "wss://fstream.binance.com/stream";

    /// <summary>
    /// HTTP请求超时时间(秒)
    /// </summary>
    /// <remarks>
    /// 推荐范围: 5-30秒，过短可能导致请求失败，过长影响响应性
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    /// <remarks>
    /// 遇到429或5xx错误时的最大重试次数
    /// </remarks>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// REST API速率限制 (请求/分钟)
    /// </summary>
    /// <remarks>
    /// Binance官方限制: 1200 weight/分钟
    /// 建议保留20%缓冲: 960
    /// </remarks>
    public int RestApiRateLimit { get; init; } = 1200;

    /// <summary>
    /// 订单API速率限制 (请求/10秒)
    /// </summary>
    /// <remarks>
    /// Binance官方限制: 300 orders/10秒
    /// </remarks>
    public int OrderApiRateLimit { get; init; } = 300;

    /// <summary>
    /// 启用API熔断器
    /// </summary>
    public bool EnableCircuitBreaker { get; init; } = true;

    /// <summary>
    /// 熔断器失败阈值
    /// </summary>
    /// <remarks>
    /// 连续失败此次数后触发熔断
    /// </remarks>
    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    /// <summary>
    /// 熔断器冷却时间(分钟)
    /// </summary>
    public int CircuitBreakerCooldownMinutes { get; init; } = 1;
}
