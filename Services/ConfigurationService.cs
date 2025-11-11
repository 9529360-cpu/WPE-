using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using 币安量化机器人.Models.Configuration;
using Serilog.Events;

namespace 币安量化机器人.Services;

/// <summary>
/// 配置服务 - 统一管理应用配置
/// </summary>
public static class ConfigurationService
{
    private static IConfiguration? _configuration;
    private static readonly object _lock = new();

    /// <summary>
    /// 初始化配置（支持环境变量）
    /// </summary>
    public static void Initialize(string? basePath = null)
    {
        lock (_lock)
        {
            basePath ??= AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                _configuration = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
                    .Build();

                LogService.Info("配置服务初始化成功");
                
                // 🔧 检查环境变量配置
                CheckEnvironmentVariables();
            }
            catch (Exception ex)
            {
                // 如果配置文件不存在，创建一个空的配置
                LogService.Error(ex, "配置文件加载失败，使用默认配置");
                _configuration = new ConfigurationBuilder().Build();
            }
        }
    }

    /// <summary>
    /// 🔧 检查环境变量配置
    /// </summary>
    private static void CheckEnvironmentVariables()
    {
        string? deepSeekKeyFromEnv = Environment.GetEnvironmentVariable("TRADING_DEEPSEEK_API_KEY");
        string? binanceKeyFromEnv = Environment.GetEnvironmentVariable("TRADING_BINANCE_API_KEY");
        string? binanceSecretFromEnv = Environment.GetEnvironmentVariable("TRADING_BINANCE_SECRET_KEY");

        if (!string.IsNullOrEmpty(deepSeekKeyFromEnv))
        {
            LogService.Info("[ConfigService] ✅ 从环境变量加载 DeepSeek API Key");
        }

        if (!string.IsNullOrEmpty(binanceKeyFromEnv))
        {
            LogService.Info("[ConfigService] ✅ 从环境变量加载 Binance API Key");
        }

        if (!string.IsNullOrEmpty(binanceSecretFromEnv))
        {
            LogService.Info("[ConfigService] ✅ 从环境变量加载 Binance Secret Key");
        }
    }

    /// <summary>
    /// 🔧 获取DeepSeek API Key（优先环境变量）
    /// </summary>
    public static string GetDeepSeekApiKey()
    {
        EnsureInitialized();

        // 1. 优先从环境变量读取
        string? envKey = Environment.GetEnvironmentVariable("TRADING_DEEPSEEK_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            LogService.Debug("[ConfigService] 使用环境变量的 DeepSeek API Key");
            return envKey;
        }

        // 2. 从配置文件读取
        string? configKey = _configuration!["AI:DeepSeek:ApiKey"];
        if (!string.IsNullOrEmpty(configKey))
        {
            // 移除可能的隐藏字符 (•)
            configKey = configKey.Replace("•", "");
            
            if (configKey.StartsWith("sk-") && configKey.Length > 10)
            {
                LogService.Debug("[ConfigService] 使用配置文件的 DeepSeek API Key");
                return configKey;
            }
        }

        LogService.Warning("[ConfigService] ⚠️ 未配置 DeepSeek API Key");
        return string.Empty;
    }

    /// <summary>
    /// 🔧 获取Binance API凭证（优先环境变量）
    /// </summary>
    public static (string apiKey, string secretKey) GetBinanceCredentials()
    {
        EnsureInitialized();

        // 从环境变量读取
        string? envApiKey = Environment.GetEnvironmentVariable("TRADING_BINANCE_API_KEY");
        string? envSecretKey = Environment.GetEnvironmentVariable("TRADING_BINANCE_SECRET_KEY");

        if (!string.IsNullOrEmpty(envApiKey) && !string.IsNullOrEmpty(envSecretKey))
        {
            LogService.Debug("[ConfigService] 使用环境变量的 Binance 凭证");
            return (envApiKey, envSecretKey);
        }

        // 从配置文件读取
        string? configApiKey = _configuration!["Api:Binance:ApiKey"];
        string? configSecretKey = _configuration!["Api:Binance:SecretKey"];

        if (!string.IsNullOrEmpty(configApiKey) && !string.IsNullOrEmpty(configSecretKey))
        {
            // 移除可能的隐藏字符
            configApiKey = configApiKey.Replace("•", "");
            configSecretKey = configSecretKey.Replace("•", "");
            
            LogService.Debug("[ConfigService] 使用配置文件的 Binance 凭证");
            return (configApiKey, configSecretKey);
        }

        LogService.Warning("[ConfigService] ⚠️ 未配置 Binance API 凭证");
        return (string.Empty, string.Empty);
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

        try
        {
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
        catch (Exception ex)
        {
            LogService.Error(ex, "[ConfigService] 获取AI配置失败，返回默认值");
            
            // 返回默认配置
            return new AIConfig
            {
                DeepSeekApiKey = string.Empty,
                Model = "deepseek-chat",
                Temperature = 0.3,
                MaxTokens = 2000,
                EnableAITrading = false
            };
        }
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
        if (_configuration == null)
        {
            // 🔧 自动初始化
            LogService.Warning("[ConfigService] 配置未初始化，正在自动初始化...");
            Initialize();
            
            if (_configuration == null)
            {
                throw new InvalidOperationException("配置服务自动初始化失败，请检查 appsettings.json 文件是否存在");
            }
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
