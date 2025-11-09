using System;
using System.Threading.Tasks;

namespace 币安量化机器人.Services;

public static class ServiceLocator
{
    static ServiceLocator()
    {
        AppSettingsService.Load();
    }

    private static readonly Lazy<DataCacheService> CacheFactory = new(() =>
    {
        var cache = new DataCacheService();
        cache.InitializeAsync().GetAwaiter().GetResult();
        return cache;
    });

    private static readonly Lazy<BinanceApiClient> ApiFactory = new(() => new BinanceApiClient());
    private static readonly Lazy<AiForecastService> AiFactory = new(() => new AiForecastService(Api, Cache));
    private static readonly Lazy<RiskEngine> RiskFactory = new(() => new RiskEngine(Cache));
    private static readonly Lazy<BinanceStreamClient> StreamFactory = new(() => new BinanceStreamClient());
    private static readonly Lazy<NotificationService> NotificationFactory = new(() => new NotificationService());
    private static readonly Lazy<AppSettings> SettingsFactory = new(() => AppSettingsService.Current);
    private static readonly Lazy<AutoTradeEngine> AutoTradeFactory = new(() =>
        new AutoTradeEngine(Api, Ai, Risk, Notification, Settings));

    public static DataCacheService Cache => CacheFactory.Value;
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
