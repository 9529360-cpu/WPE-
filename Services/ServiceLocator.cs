using System;
using System.Threading.Tasks;

namespace 币安量化机器人.Services;

public static class ServiceLocator
{
    private static DataCacheService? _cacheInstance;
    private static readonly object _cacheLock = new();

    static ServiceLocator()
    {
        AppSettingsService.Load();
    }

    public static async Task InitializeAsync()
    {
        if (_cacheInstance != null) return;

        lock (_cacheLock)
        {
            if (_cacheInstance != null) return;
            _cacheInstance = new DataCacheService();
        }

        await _cacheInstance.InitializeAsync().ConfigureAwait(false);
    }

    private static readonly Lazy<BinanceApiClient> ApiFactory = new(() => new BinanceApiClient());
    private static readonly Lazy<AiForecastService> AiFactory = new(() => new AiForecastService(Api, Cache));
    private static readonly Lazy<RiskEngine> RiskFactory = new(() => new RiskEngine(Cache));
    private static readonly Lazy<BinanceStreamClient> StreamFactory = new(() => new BinanceStreamClient());
    private static readonly Lazy<NotificationService> NotificationFactory = new(() => new NotificationService());
    private static readonly Lazy<AppSettings> SettingsFactory = new(() => AppSettingsService.Current);
    private static readonly Lazy<AutoTradeEngine> AutoTradeFactory = new(() =>
        new AutoTradeEngine(Api, Ai, Risk, Notification, Settings));

    public static DataCacheService Cache => _cacheInstance ?? throw new InvalidOperationException("ServiceLocator has not been initialized. Call InitializeAsync() first.");
    public static BinanceApiClient Api => ApiFactory.Value;
    public static AiForecastService Ai => AiFactory.Value;
    public static RiskEngine Risk => RiskFactory.Value;
    public static BinanceStreamClient Stream => StreamFactory.Value;
    public static NotificationService Notification => NotificationFactory.Value;
    public static AppSettings Settings => SettingsFactory.Value;
    public static AutoTradeEngine AutoTrade => AutoTradeFactory.Value;

    public static async ValueTask DisposeAsync()
    {
        if (AutoTradeFactory.IsValueCreated)
            await AutoTradeFactory.Value.DisposeAsync();
        if (StreamFactory.IsValueCreated)
            await StreamFactory.Value.DisposeAsync();
        if (ApiFactory.IsValueCreated)
            ApiFactory.Value.Dispose();
    }
}
