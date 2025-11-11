using System;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Resilience;

/// <summary>
/// 弹性服务集成器
/// </summary>
/// <remarks>
/// 统一管理所有弹性组件:
/// 1. 异常检测系统
/// 2. 自动恢复管理器
/// 
/// 提供统一的弹性接口
/// </remarks>
public class ResilienceService : IDisposable
{
    private readonly AnomalyDetectionSystem _anomalyDetection;
    private readonly AutoRecoveryManager _recoveryManager;
    
    private bool _isInitialized;

    public ResilienceService()
    {
        // 初始化异常检测
        _anomalyDetection = new AnomalyDetectionSystem();

        // 初始化自动恢复
        _recoveryManager = new AutoRecoveryManager();

        // 订阅事件
        _anomalyDetection.AnomalyDetected += OnAnomalyDetected;
        _recoveryManager.RecoverySucceeded += OnRecoverySucceeded;
        _recoveryManager.RecoveryFailed += OnRecoveryFailed;

        _isInitialized = true;

        LogService.Info("🛡️ [ResilienceService] 弹性服务已启动");
    }

    #region 异常检测

    /// <summary>
    /// 检测API异常
    /// </summary>
    public void DetectApiAnomaly(string endpoint, Exception exception, TimeSpan responseTime)
    {
        _anomalyDetection.DetectApiAnomaly(endpoint, exception, responseTime);
    }

    /// <summary>
    /// 检测交易异常
    /// </summary>
    public void DetectTradingAnomaly(string symbol, string reason, System.Collections.Generic.Dictionary<string, object>? details = null)
    {
        _anomalyDetection.DetectTradingAnomaly(symbol, reason, details);
    }

    /// <summary>
    /// 检测性能异常
    /// </summary>
    public void DetectPerformanceAnomaly(string metric, double value, double threshold)
    {
        _anomalyDetection.DetectPerformanceAnomaly(metric, value, threshold);
    }

    /// <summary>
    /// 检测数据异常
    /// </summary>
    public void DetectDataAnomaly(string dataType, string reason, object? value = null)
    {
        _anomalyDetection.DetectDataAnomaly(dataType, reason, value);
    }

    /// <summary>
    /// 检测系统异常
    /// </summary>
    public void DetectSystemAnomaly(string component, Exception exception)
    {
        _anomalyDetection.DetectSystemAnomaly(component, exception);
    }

    /// <summary>
    /// 获取异常统计
    /// </summary>
    public AnomalyStatistics GetAnomalyStatistics()
    {
        return _anomalyDetection.GetStatistics();
    }

    /// <summary>
    /// 获取最近异常
    /// </summary>
    public System.Collections.Generic.List<AnomalyEvent> GetRecentAnomalies(int count = 50)
    {
        return _anomalyDetection.GetRecentAnomalies(count);
    }

    /// <summary>
    /// 获取异常模式
    /// </summary>
    public System.Collections.Generic.List<AnomalyPattern> GetAnomalyPatterns(int topN = 20)
    {
        return _anomalyDetection.GetAnomalyPatterns(topN);
    }

    #endregion

    #region 自动恢复

    /// <summary>
    /// 报告组件故障
    /// </summary>
    public async Task<bool> ReportFailureAsync(
        string component,
        Exception exception,
        CancellationToken ct = default)
    {
        // 先检测异常
        DetectSystemAnomaly(component, exception);
        
        // 尝试恢复
        return await _recoveryManager.ReportFailureAsync(component, exception, ct);
    }

    /// <summary>
    /// 报告组件恢复
    /// </summary>
    public void ReportRecovery(string component)
    {
        _recoveryManager.ReportRecovery(component);
    }

    /// <summary>
    /// 注册恢复策略
    /// </summary>
    public void RegisterRecoveryPolicy(string component, RecoveryPolicy policy)
    {
        _recoveryManager.RegisterPolicy(component, policy);
    }

    /// <summary>
    /// 获取组件健康状态
    /// </summary>
    public ComponentHealth? GetComponentHealth(string component)
    {
        return _recoveryManager.GetHealth(component);
    }

    /// <summary>
    /// 获取所有组件健康状态
    /// </summary>
    public System.Collections.Generic.List<ComponentHealth> GetAllComponentHealth()
    {
        return _recoveryManager.GetAllHealth();
    }

    /// <summary>
    /// 获取恢复统计
    /// </summary>
    public RecoveryStatistics GetRecoveryStatistics()
    {
        return _recoveryManager.GetStatistics();
    }

    /// <summary>
    /// 获取恢复历史
    /// </summary>
    public System.Collections.Generic.List<RecoveryAttempt> GetRecoveryHistory(int count = 50)
    {
        return _recoveryManager.GetRecoveryHistory(count);
    }

    #endregion

    #region 事件处理

    /// <summary>
    /// 异常检测事件处理
    /// </summary>
    private void OnAnomalyDetected(object? sender, AnomalyDetectedEventArgs e)
    {
        AnomalyEvent anomaly = e.Anomaly;
        
        // 严重异常自动触发恢复
        if (anomaly.Severity == AnomalySeverity.Critical)
        {
            LogService.Warning("[ResilienceService] 检测到严重异常，触发自动恢复: {Source}",
                anomaly.Source);
            
            // 异步触发恢复
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReportFailureAsync(
                        anomaly.Source,
                        new Exception(anomaly.Message),
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "[ResilienceService] 自动恢复触发失败");
                }
            });
        }
    }

    /// <summary>
    /// 恢复成功事件处理
    /// </summary>
    private void OnRecoverySucceeded(object? sender, RecoveryEventArgs e)
    {
        LogService.Info("✅ [ResilienceService] 组件恢复成功: {Component}, Attempt={Attempt}",
            e.Attempt.Component, e.Attempt.AttemptNumber);
    }

    /// <summary>
    /// 恢复失败事件处理
    /// </summary>
    private void OnRecoveryFailed(object? sender, RecoveryEventArgs e)
    {
        LogService.Warning("❌ [ResilienceService] 组件恢复失败: {Component}, Attempt={Attempt}",
            e.Attempt.Component, e.Attempt.AttemptNumber);
    }

    #endregion

    #region 系统健康

    /// <summary>
    /// 获取系统整体健康状态
    /// </summary>
    public SystemHealthReport GetSystemHealth()
    {
        AnomalyStatistics anomalyStats = _anomalyDetection.GetStatistics();
        RecoveryStatistics recoveryStats = _recoveryManager.GetStatistics();
        System.Collections.Generic.List<ComponentHealth> components = _recoveryManager.GetAllHealth();

        bool isHealthy = anomalyStats.RecentHourCount < 50 &&
                        anomalyStats.CriticalAnomalies == 0 &&
                        recoveryStats.UnhealthyComponents == 0;

        return new SystemHealthReport
        {
            IsHealthy = isHealthy,
            TotalComponents = components.Count,
            HealthyComponents = components.Count - recoveryStats.UnhealthyComponents,
            UnhealthyComponents = recoveryStats.UnhealthyComponents,
            TotalAnomalies = anomalyStats.TotalAnomalies,
            RecentAnomalies = anomalyStats.RecentHourCount,
            TotalRecoveries = recoveryStats.TotalRecoveries,
            RecoverySuccessRate = recoveryStats.SuccessRate,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 打印健康报告
    /// </summary>
    public void PrintHealthReport()
    {
        SystemHealthReport report = GetSystemHealth();
        
        LogService.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        LogService.Info("🛡️ [ResilienceService] 系统健康报告");
        LogService.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        
        string healthStatus = report.IsHealthy ? "✅ 健康" : "⚠️ 异常";
        LogService.Info("整体状态: {Status}", healthStatus);
        LogService.Info("组件健康: {Healthy}/{Total}", report.HealthyComponents, report.TotalComponents);
        LogService.Info("最近异常: {Recent} (总计: {Total})", report.RecentAnomalies, report.TotalAnomalies);
        LogService.Info("恢复成功率: {Rate:P2} (总计: {Total})", report.RecoverySuccessRate, report.TotalRecoveries);
        
        LogService.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    #endregion

    public void Dispose()
    {
        if (!_isInitialized)
        {
            return;
        }

        _anomalyDetection?.Dispose();
        _recoveryManager?.Dispose();

        _isInitialized = false;

        LogService.Info("[ResilienceService] 已释放资源");
    }
}

#region 数据模型

/// <summary>
/// 系统健康报告
/// </summary>
public class SystemHealthReport
{
    public bool IsHealthy { get; init; }
    public int TotalComponents { get; init; }
    public int HealthyComponents { get; init; }
    public int UnhealthyComponents { get; init; }
    public long TotalAnomalies { get; init; }
    public int RecentAnomalies { get; init; }
    public long TotalRecoveries { get; init; }
    public double RecoverySuccessRate { get; init; }
    public DateTime Timestamp { get; init; }

    public override string ToString()
    {
        string status = IsHealthy ? "✅ 健康" : "⚠️ 异常";
        return $"{status} - Components: {HealthyComponents}/{TotalComponents}, " +
               $"Anomalies: {RecentAnomalies}, Recoveries: {TotalRecoveries} ({RecoverySuccessRate:P0})";
    }
}

#endregion
