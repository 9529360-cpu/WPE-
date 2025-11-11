using System;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services;

/// <summary>
/// 系统就绪检测服务：集中维护 Binance/DeepSeek/Trading 就绪状态，向 UI 广播变化。
/// </summary>
public sealed class SystemReadyService
{
    private readonly object _lock = new();

    public bool BinanceReady { get; private set; }
    public bool DeepSeekReady { get; private set; }
    public bool TradingReady { get; private set; }

    public event Action? ReadyStateChanged;

    /// <summary>
    /// 从配置刷新就绪状态（只检查密钥存在性）。
    /// </summary>
    public void RefreshFromConfig()
    {
        lock (_lock)
        {
            var (api, secret) = ConfigurationService.GetBinanceCredentials();
            BinanceReady = !string.IsNullOrWhiteSpace(api) && !string.IsNullOrWhiteSpace(secret);
            var ds = ConfigurationService.GetDeepSeekApiKey();
            DeepSeekReady = !string.IsNullOrWhiteSpace(ds);
            TradingReady = BinanceReady && DeepSeekReady;
        }
        ReadyStateChanged?.Invoke();
    }

    /// <summary>
    /// 异步验证 DeepSeek 可用性（401/402 明确），验证结果将更新 DeepSeekReady 与 TradingReady。
    /// </summary>
    public async Task ValidateDeepSeekAsync(CancellationToken ct = default)
    {
        try
        {
            string key = ConfigurationService.GetDeepSeekApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                UpdateDeepSeek(false);
                return;
            }
            var agent = new Services.AI.DeepSeekTradingAgent(key);
            await agent.ValidateAccessAsync(ct);
            UpdateDeepSeek(true);
        }
        catch
        {
            UpdateDeepSeek(false);
        }
    }

    private void UpdateDeepSeek(bool ok)
    {
        lock (_lock)
        {
            DeepSeekReady = ok;
            var (api, secret) = ConfigurationService.GetBinanceCredentials();
            TradingReady = ok && !string.IsNullOrWhiteSpace(api) && !string.IsNullOrWhiteSpace(secret);
        }
        ReadyStateChanged?.Invoke();
    }
}
