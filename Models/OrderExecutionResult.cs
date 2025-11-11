using System;

namespace 币安量化机器人.Models;

/// <summary>
/// 订单执行结果
/// </summary>
/// <remarks>
/// 统一的订单执行结果,支持模拟账户和真实账户
/// </remarks>
public class OrderExecutionResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 订单ID
    /// </summary>
    public string OrderId { get; init; } = string.Empty;

    /// <summary>
    /// 成交价格
    /// </summary>
    public double ExecutedPrice { get; init; }

    /// <summary>
    /// 成交数量
    /// </summary>
    public double ExecutedQuantity { get; init; }

    /// <summary>
    /// 手续费
    /// </summary>
    public double Commission { get; init; }

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 执行时间
    /// </summary>
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static OrderExecutionResult Success(string orderId, double executedPrice, double executedQuantity, double commission = 0)
    {
        return new OrderExecutionResult
        {
            IsSuccess = true,
            OrderId = orderId,
            ExecutedPrice = executedPrice,
            ExecutedQuantity = executedQuantity,
            Commission = commission,
            Message = "订单执行成功"
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static OrderExecutionResult Failed(string error, string message = "订单执行失败")
    {
        return new OrderExecutionResult
        {
            IsSuccess = false,
            Message = message,
            Error = error
        };
    }

    /// <summary>
    /// 创建拒绝结果
    /// </summary>
    public static OrderExecutionResult Rejected(string reason)
    {
        return new OrderExecutionResult
        {
            IsSuccess = false,
            Message = "订单被拒绝",
            Error = reason
        };
    }
}
