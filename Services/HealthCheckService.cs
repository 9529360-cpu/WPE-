using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace 币安量化机器人.Services;

/// <summary>
/// 系统健康检查服务 (System Health Check Service)
/// </summary>
public class HealthCheckService
{
    private readonly DataCacheService _cache;
    private readonly BinanceApiClient _apiClient;
    private readonly DateTime _startTime;

    public HealthCheckService(DataCacheService cache, BinanceApiClient apiClient)
    {
        _cache = cache;
        _apiClient = apiClient;
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 执行全面健康检查 (Perform comprehensive health check)
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync()
    {
        var checks = new List<ComponentHealth>();

        // 检查数据库连接 (Check database connection)
        checks.Add(await CheckDatabaseAsync());

        // 检查API连接 (Check API connection)
        checks.Add(await CheckApiConnectionAsync());

        // 检查磁盘空间 (Check disk space)
        checks.Add(CheckDiskSpace());

        // 检查内存使用 (Check memory usage)
        checks.Add(CheckMemoryUsage());

        // 检查系统运行时间 (Check system uptime)
        checks.Add(CheckUptime());

        var overallStatus = checks.Any(c => c.Status == HealthStatus.Unhealthy)
            ? HealthStatus.Unhealthy
            : checks.Any(c => c.Status == HealthStatus.Degraded)
                ? HealthStatus.Degraded
                : HealthStatus.Healthy;

        return new HealthCheckResult
        {
            Status = overallStatus,
            CheckedAt = DateTime.UtcNow,
            Components = checks,
            Uptime = DateTime.UtcNow - _startTime
        };
    }

    private async Task<ComponentHealth> CheckDatabaseAsync()
    {
        try
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, "Data", "trading.sqlite");
            if (!File.Exists(dbPath))
            {
                return new ComponentHealth
                {
                    Name = "Database",
                    Status = HealthStatus.Unhealthy,
                    Message = "Database file not found",
                    Details = new Dictionary<string, object>
                    {
                        ["path"] = dbPath
                    }
                };
            }

            // 简单检查：尝试访问缓存（间接检查数据库）
            await _cache.GetLatestPriceAsync("BTCUSDT");

            var fileInfo = new FileInfo(dbPath);
            return new ComponentHealth
            {
                Name = "Database",
                Status = HealthStatus.Healthy,
                Message = "Database accessible",
                Details = new Dictionary<string, object>
                {
                    ["size_mb"] = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2),
                    ["last_modified"] = fileInfo.LastWriteTime
                }
            };
        }
        catch (Exception ex)
        {
            return new ComponentHealth
            {
                Name = "Database",
                Status = HealthStatus.Unhealthy,
                Message = $"Database error: {ex.Message}",
                Exception = ex
            };
        }
    }

    private async Task<ComponentHealth> CheckApiConnectionAsync()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var serverTime = await _apiClient.GetServerTimeAsync();
            sw.Stop();

            var latency = sw.ElapsedMilliseconds;
            var status = latency < 200 ? HealthStatus.Healthy
                        : latency < 500 ? HealthStatus.Degraded
                        : HealthStatus.Unhealthy;

            return new ComponentHealth
            {
                Name = "Binance API",
                Status = status,
                Message = status == HealthStatus.Healthy 
                    ? "API connection healthy" 
                    : $"High latency: {latency}ms",
                Details = new Dictionary<string, object>
                {
                    ["latency_ms"] = latency,
                    ["server_time"] = serverTime
                }
            };
        }
        catch (Exception ex)
        {
            return new ComponentHealth
            {
                Name = "Binance API",
                Status = HealthStatus.Unhealthy,
                Message = $"API connection failed: {ex.Message}",
                Exception = ex
            };
        }
    }

    private ComponentHealth CheckDiskSpace()
    {
        try
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "Data");
            var drive = new DriveInfo(Path.GetPathRoot(dataPath)!);

            var freeSpaceGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            var totalSpaceGB = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
            var usedPercent = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;

            var status = usedPercent < 80 ? HealthStatus.Healthy
                        : usedPercent < 90 ? HealthStatus.Degraded
                        : HealthStatus.Unhealthy;

            return new ComponentHealth
            {
                Name = "Disk Space",
                Status = status,
                Message = status == HealthStatus.Healthy 
                    ? "Sufficient disk space" 
                    : $"Low disk space: {usedPercent:F1}% used",
                Details = new Dictionary<string, object>
                {
                    ["free_gb"] = Math.Round(freeSpaceGB, 2),
                    ["total_gb"] = Math.Round(totalSpaceGB, 2),
                    ["used_percent"] = Math.Round(usedPercent, 1)
                }
            };
        }
        catch (Exception ex)
        {
            return new ComponentHealth
            {
                Name = "Disk Space",
                Status = HealthStatus.Degraded,
                Message = $"Unable to check disk space: {ex.Message}",
                Exception = ex
            };
        }
    }

    private ComponentHealth CheckMemoryUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / 1024.0 / 1024.0;

            var status = memoryMB < 500 ? HealthStatus.Healthy
                        : memoryMB < 1000 ? HealthStatus.Degraded
                        : HealthStatus.Unhealthy;

            return new ComponentHealth
            {
                Name = "Memory",
                Status = status,
                Message = status == HealthStatus.Healthy 
                    ? "Memory usage normal" 
                    : $"High memory usage: {memoryMB:F0} MB",
                Details = new Dictionary<string, object>
                {
                    ["working_set_mb"] = Math.Round(memoryMB, 2),
                    ["private_memory_mb"] = Math.Round(process.PrivateMemorySize64 / 1024.0 / 1024.0, 2)
                }
            };
        }
        catch (Exception ex)
        {
            return new ComponentHealth
            {
                Name = "Memory",
                Status = HealthStatus.Degraded,
                Message = $"Unable to check memory: {ex.Message}",
                Exception = ex
            };
        }
    }

    private ComponentHealth CheckUptime()
    {
        var uptime = DateTime.UtcNow - _startTime;
        
        return new ComponentHealth
        {
            Name = "Application",
            Status = HealthStatus.Healthy,
            Message = $"Running for {FormatUptime(uptime)}",
            Details = new Dictionary<string, object>
            {
                ["uptime_seconds"] = uptime.TotalSeconds,
                ["started_at"] = _startTime
            }
        };
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays} days, {uptime.Hours} hours";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours} hours, {uptime.Minutes} minutes";
        return $"{(int)uptime.TotalMinutes} minutes";
    }
}

/// <summary>
/// 健康检查结果 (Health check result)
/// </summary>
public class HealthCheckResult
{
    public HealthStatus Status { get; set; }
    public DateTime CheckedAt { get; set; }
    public TimeSpan Uptime { get; set; }
    public List<ComponentHealth> Components { get; set; } = new();
}

/// <summary>
/// 组件健康状态 (Component health status)
/// </summary>
public class ComponentHealth
{
    public string Name { get; set; } = string.Empty;
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Details { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// 健康状态枚举 (Health status enumeration)
/// </summary>
public enum HealthStatus
{
    Healthy,    // 健康
    Degraded,   // 降级
    Unhealthy   // 不健康
}
