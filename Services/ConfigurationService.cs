using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Serilog.Events;
using 币安量化机器人.Models.Configuration;

namespace 币安量化机器人.Services;

/// <summary>
/// 配置服务 - 从appsettings.json加载应用配置
/// </summary>
/// <remarks>
/// 使用Microsoft.Extensions.Configuration加载配置文件
/// 支持运行时修改配置文件后重启生效
/// </remarks>
public static class ConfigurationService
{
    private static IConfiguration? _configuration;
    private static bool _initialized = false;
    private static string? _configPath; // 🔧 保存配置文件路径

    /// <summary>
    /// 🔧 获取当前使用的配置文件路径
    /// </summary>
    public static string ConfigPath => _configPath ?? "未初始化";

    /// <summary>
    /// 初始化配置服务
    /// </summary>
    /// <param name="configFilePath">配置文件路径 (默认: appsettings.json)</param>
    /// <param name="forceReload">强制重新加载配置</param>
    public static void Initialize(string? configFilePath = null, bool forceReload = false)
    {
        if (_initialized && !forceReload)
        {
            return;
        }

        // 🔧 使用与 ApiManagerView 一致的配置文件查找逻辑
        if (configFilePath == null)
        {
            configFilePath = FindConfigFile();
        }

        // 确保配置文件存在
        if (!File.Exists(configFilePath))
        {
            throw new FileNotFoundException($"配置文件不存在: {configFilePath}");
        }

        _configPath = configFilePath;

        _configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(configFilePath) ?? AppContext.BaseDirectory)
            .AddJsonFile(Path.GetFileName(configFilePath), optional: false, reloadOnChange: true)
            .Build();

        _initialized = true;

        LogService.Info("配置服务已{Action},配置文件: {ConfigFile}", forceReload ? "重新加载" : "初始化", configFilePath);
    }

    /// <summary>
    /// 🔧 查找配置文件（项目根目录优先）
    /// </summary>
    private static string FindConfigFile()
    {
        // 1. 尝试项目根目录（开发环境）
        string? projectRoot = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName;
        if (projectRoot != null)
        {
            string projectConfig = Path.Combine(projectRoot, "appsettings.json");
            if (File.Exists(projectConfig))
            {
                LogService.Info("[ConfigurationService] 使用项目根目录配置: {Path}", projectConfig);
                return projectConfig;
            }
        }

        // 2. 回退到运行目录
        string runtimeConfig = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        LogService.Info("[ConfigurationService] 使用运行目录配置: {Path}", runtimeConfig);
        return runtimeConfig;
    }

    /// <summary>
    /// 获取应用基本信息
    /// </summary>
    public static AppInfo GetAppInfo()
    {
        EnsureInitialized();

        return new AppInfo
        {
            Name = _configuration!["App:Name"] ?? "币安量化机器人",
            Version = _configuration["App:Version"] ?? "1.0.0",
            Environment = _configuration["App:Environment"] ?? "Production"
        };
    }

    /// <summary>
    /// 获取日志配置
    /// </summary>
    public static LoggingConfig GetLoggingConfig()
    {
        EnsureInitialized();

        string minimumLevel = _configuration!["Logging:MinimumLevel"] ?? "Information";

        return new LoggingConfig
        {
            MinimumLevel = Enum.TryParse<LogEventLevel>(minimumLevel, out LogEventLevel level)
                ? level
                : LogEventLevel.Information,
            FilePath = _configuration["Logging:FilePath"] ?? "Logs/app-.log",
            RetainDays = int.TryParse(_configuration["Logging:RetainDays"], out int days)
                ? days
                : 30
        };
    }

    /// <summary>
    /// 获取交易配置
    /// </summary>
    public static TradingConfig GetTradingConfig()
    {
        EnsureInitialized();

        IConfigurationSection section = _configuration!.GetSection("Trading");

        return new TradingConfig
        {
            MinConfidence = GetDouble(section, "MinConfidenceThreshold", 0.70),
            MaxPositionSize = GetDouble(section, "MaxPositionSizePercent", 0.10),
            MaxDailyLoss = GetDouble(section, "MaxDailyLossPercent", 0.05),
            StopLossLimit = GetDouble(section, "DefaultStopLossPercent", 0.02),
            DefaultQuantity = GetDouble(section, "DefaultQuantity", 0.01),
            EnableSlippageProtection = GetBool(section, "EnableSlippageProtection", true),
            MaxAcceptableSlippage = GetDouble(section, "MaxAcceptableSlippage", 0.005)
        };
    }

    /// <summary>
    /// 获取API配置
    /// </summary>
    public static ApiConfig GetApiConfig()
    {
        EnsureInitialized();

        IConfigurationSection section = _configuration!.GetSection("Api:Binance");
        IConfigurationSection breakerSection = _configuration.GetSection("Api:CircuitBreaker");

        // 🔧 优先从环境变量读取 API Key 和 Secret Key
        string apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY")
                    ?? section["ApiKey"] ?? "";
        string secretKey = Environment.GetEnvironmentVariable("BINANCE_SECRET_KEY")
                       ?? section["SecretKey"] ?? "";

        return new ApiConfig
        {
            RestEndpoint = section["RestEndpoint"] ?? "https://fapi.binance.com",
            StreamEndpoint = section["WebSocketEndpoint"] ?? "wss://fstream.binance.com/stream",
            TimeoutSeconds = GetInt(section, "Timeout", 10),
            MaxRetries = GetInt(section, "MaxRetries", 3),
            RestApiRateLimit = GetInt(section.GetSection("RateLimit"), "RestApiCapacity", 1200),
            OrderApiRateLimit = GetInt(section.GetSection("RateLimit"), "OrderApiCapacity", 300),
            EnableCircuitBreaker = GetBool(breakerSection, "Enabled", true),
            CircuitBreakerFailureThreshold = GetInt(breakerSection, "FailureThreshold", 5),
            CircuitBreakerCooldownMinutes = GetInt(breakerSection, "CooldownMinutes", 1)
        };
    }

    /// <summary>
    /// 获取风控配置
    /// </summary>
    public static RiskConfig GetRiskConfig()
    {
        EnsureInitialized();

        IConfigurationSection section = _configuration!.GetSection("Risk");

        return new RiskConfig
        {
            EnableDynamicStopLoss = GetBool(section, "EnableDynamicStopLoss", true),
            AtrMultiplier = GetDouble(section, "StopLossMultiplier", 2.0),
            EnableTimeBasedExit = GetBool(section, "EnableTimeBasedExit", true),
            TimeBasedExitHours = GetDouble(section, "MaxPositionHoldingHours", 24.0),
            EnableDailyLossLimit = GetBool(section, "EnableDailyLossLimit", true),
            EnableTrailingStopLoss = GetBool(section, "EnableTrailingStopLoss", true),
            TrailingStopTrigger = GetDouble(section, "TrailingStopActivationPercent", 0.02),
            TrailingStopDistance = GetDouble(section, "TrailingStopPercent", 0.01),
            EnableMaxDrawdownProtection = GetBool(section, "EnableMaxDrawdownProtection", true),
            MaxDrawdownThreshold = GetDouble(section, "MaxDrawdown", 0.10),
            EnableMaxPositionLimit = GetBool(section, "EnableMaxPositionLimit", true),
            MaxTotalPositionSize = GetDouble(section, "MaxPositionSize", 0.50),
            EnableBlacklist = GetBool(section, "EnableBlacklist", true)
        };
    }

    /// <summary>
    /// 获取回测配置
    /// </summary>
    public static BacktestConfig GetBacktestConfig()
    {
        EnsureInitialized();

        IConfigurationSection section = _configuration!.GetSection("Backtest");

        return new BacktestConfig
        {
            InitialCapital = GetDouble(section, "InitialCapital", 10000.0),
            MakerFeeRate = GetDouble(section, "MakerFeeRate", 0.0002),
            TakerFeeRate = GetDouble(section, "TakerFeeRate", 0.0004),
            AvgFundingRate = GetDouble(section, "AvgFundingRate", 0.0001),
            BaseSlippage = GetDouble(section, "BaseSlippage", 0.0001),
            ImpactFactor = GetDouble(section, "ImpactFactor", 0.00001),
            SpreadMultiplier = GetDouble(section, "SpreadMultiplier", 0.5),
            UseVipFeeRates = GetBool(section, "UseVipFeeRates", false),
            VipLevel = GetInt(section, "VipLevel", 0),
            EnableStressTest = GetBool(section, "EnableStressTest", false),
            MinTradeIntervalSeconds = GetInt(section, "MinTradeIntervalSeconds", 1)
        };
    }

    /// <summary>
    /// 获取AI配置
    /// </summary>
    public static AIConfig GetAIConfig()
    {
        EnsureInitialized();

        IConfigurationSection section = _configuration!.GetSection("AI:DeepSeek");

        // 🔧 优先从环境变量读取 DeepSeek API Key
        string apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
                    ?? section["ApiKey"] ?? "";

        return new AIConfig
        {
            DeepSeekApiKey = apiKey,
            Model = section["Model"] ?? "deepseek-chat",
            Temperature = GetDouble(section, "Temperature", 0.3),
            MaxTokens = GetInt(section, "MaxTokens", 2000),
            EnableAITrading = GetBool(_configuration.GetSection("AI"), "EnableAITrading", false)
        };
    }

    /// <summary>
    /// 获取数据库配置
    /// </summary>
    public static DatabaseConfig GetDatabaseConfig()
    {
        EnsureInitialized();

        IConfigurationSection section = _configuration!.GetSection("Database");

        return new DatabaseConfig
        {
            Provider = section["Provider"] ?? "SQLite",
            ConnectionString = section["ConnectionString"] ?? "Data Source=Data/trading.sqlite",
            EnableAutoMigration = GetBool(section, "EnableAutoMigration", true)
        };
    }

    /// <summary>
    /// 获取原始配置值
    /// </summary>
    /// <param name="key">配置键 (支持冒号分隔,如 "Api:Binance:RestEndpoint")</param>
    /// <param name="defaultValue">默认值</param>
    public static string GetValue(string key, string defaultValue = "")
    {
        EnsureInitialized();
        return _configuration![key] ?? defaultValue;
    }

    // 辅助方法
    private static void EnsureInitialized()
    {
        if (!_initialized || _configuration == null)
        {
            throw new InvalidOperationException("配置服务未初始化,请先调用 ConfigurationService.Initialize()");
        }
    }

    private static int GetInt(IConfigurationSection section, string key, int defaultValue)
    {
        return int.TryParse(section[key], out int value) ? value : defaultValue;
    }

    private static double GetDouble(IConfigurationSection section, string key, double defaultValue)
    {
        return double.TryParse(section[key], out double value) ? value : defaultValue;
    }

    private static bool GetBool(IConfigurationSection section, string key, bool defaultValue)
    {
        return bool.TryParse(section[key], out bool value) ? value : defaultValue;
    }
}

/// <summary>
/// 应用基本信息
/// </summary>
public record AppInfo
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
}

/// <summary>
/// 日志配置
/// </summary>
public record LoggingConfig
{
    public LogEventLevel MinimumLevel { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public int RetainDays { get; init; }
}

/// <summary>
/// AI配置
/// </summary>
public record AIConfig
{
    public string DeepSeekApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public double Temperature { get; init; }
    public int MaxTokens { get; init; }
    public bool EnableAITrading { get; init; }
}

/// <summary>
/// 数据库配置
/// </summary>
public record DatabaseConfig
{
    public string Provider { get; init; } = string.Empty;
    public string ConnectionString { get; init; } = string.Empty;
    public bool EnableAutoMigration { get; init; }
}
