using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace 币安量化机器人.Services.Observability;

/// <summary>
/// 追踪管理器（基于System.Diagnostics.Activity）
/// </summary>
public class TraceManager
{
    private static readonly ActivitySource _activitySource = new("币安量化机器人", "1.0.0");

    /// <summary>
    /// 开始追踪
    /// </summary>
    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return _activitySource.StartActivity(name, kind);
    }

    /// <summary>
    /// 添加标签
    /// </summary>
    public static void AddTag(string key, object? value)
    {
        Activity.Current?.AddTag(key, value);
    }

    /// <summary>
    /// 添加事件
    /// </summary>
    public static void AddEvent(string name)
    {
        Activity.Current?.AddEvent(new ActivityEvent(name));
    }

    /// <summary>
    /// 设置状态
    /// </summary>
    public static void SetStatus(ActivityStatusCode statusCode, string? description = null)
    {
        if (Activity.Current != null)
        {
            Activity.Current.SetStatus(statusCode, description);
        }
    }

    /// <summary>
    /// 获取当前TraceId
    /// </summary>
    public static string GetCurrentTraceId()
    {
        return Activity.Current?.TraceId.ToString() ?? string.Empty;
    }
}
