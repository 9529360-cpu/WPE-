using System;

namespace 币安量化机器人.Models;

/// <summary>
/// 账户升级规则 - 模拟账户达标后才能升级到真实账户
/// </summary>
/// <remarks>
/// 设计理念:
/// 1. 保护用户资金: 必须在模拟账户证明盈利能力
/// 2. 多维度考核: 交易次数、胜率、收益率、运行时长、回撤
/// 3. 严格标准: 宁可保守,不可激进
/// 4. 可配置: 用户可以根据风险偏好调整规则
/// </remarks>
public class AccountUpgradeRules
{
    #region 默认规则 (保守型)

    /// <summary>
    /// 最低交易次数
    /// </summary>
    /// <remarks>
    /// 目的: 确保样本量足够,避免偶然性
    /// 建议: 100笔以上才有统计意义
    /// </remarks>
    public int MinTrades { get; init; } = 100;

    /// <summary>
    /// 最低胜率
    /// </summary>
    /// <remarks>
    /// 目的: 确保策略有正向预期
    /// 建议: 55%以上算优秀策略
    /// </remarks>
    public double MinWinRate { get; init; } = 0.55;

    /// <summary>
    /// 最低总收益率
    /// </summary>
    /// <remarks>
    /// 目的: 确保策略有盈利能力
    /// 建议: 10%以上 (30天)
    /// </remarks>
    public double MinTotalReturnPercent { get; init; } = 0.10;

    /// <summary>
    /// 最小运行天数
    /// </summary>
    /// <remarks>
    /// 目的: 确保策略经过时间考验
    /// 建议: 至少30天,覆盖不同市场环境
    /// </remarks>
    public int MinRunningDays { get; init; } = 30;

    /// <summary>
    /// 最大回撤限制
    /// </summary>
    /// <remarks>
    /// 目的: 确保风控有效
    /// 建议: 不超过15%
    /// </remarks>
    public double MaxDrawdownPercent { get; init; } = 0.15;

    /// <summary>
    /// 最低盈亏比
    /// </summary>
    /// <remarks>
    /// 目的: 确保盈利交易能覆盖亏损
    /// 建议: 1.5以上 (盈利是亏损的1.5倍)
    /// </remarks>
    public double MinProfitFactor { get; init; } = 1.5;

    /// <summary>
    /// 最低夏普比率
    /// </summary>
    /// <remarks>
    /// 目的: 确保收益风险比合理
    /// 建议: 1.0以上算优秀
    /// </remarks>
    public double MinSharpeRatio { get; init; } = 0.8;

    #endregion

    #region 升级检查

    /// <summary>
    /// 检查账户是否可以升级到真实账户
    /// </summary>
    /// <param name="account">模拟账户</param>
    /// <returns>升级结果</returns>
    public AccountUpgradeResult CanUpgrade(TradingAccount account)
    {
        if (account.Type != AccountType.Simulated)
        {
            return AccountUpgradeResult.Failed("只有模拟账户才能升级");
        }

        var result = new AccountUpgradeResult();
        int runningDays = (DateTime.UtcNow - account.CreatedAt).Days;

        // 检查1: 交易次数
        result.AddCheck(
            "交易次数",
            account.TotalTrades >= MinTrades,
            $"{account.TotalTrades}/{MinTrades}",
            account.TotalTrades >= MinTrades
                ? $"✅ 交易次数充足 ({account.TotalTrades}笔)"
                : $"❌ 交易次数不足,还需要 {MinTrades - account.TotalTrades} 笔"
        );

        // 检查2: 胜率
        result.AddCheck(
            "胜率",
            account.WinRate >= MinWinRate,
            $"{account.WinRate:P0}/{MinWinRate:P0}",
            account.WinRate >= MinWinRate
                ? $"✅ 胜率达标 ({account.WinRate:P0})"
                : $"❌ 胜率不足,还需提升 {(MinWinRate - account.WinRate):P0}"
        );

        // 检查3: 收益率
        result.AddCheck(
            "总收益率",
            account.TotalReturnPercent >= MinTotalReturnPercent,
            $"{account.TotalReturnPercent:P0}/{MinTotalReturnPercent:P0}",
            account.TotalReturnPercent >= MinTotalReturnPercent
                ? $"✅ 收益率达标 ({account.TotalReturnPercent:P0})"
                : $"❌ 收益率不足,还需提升 {(MinTotalReturnPercent - account.TotalReturnPercent):P0}"
        );

        // 检查4: 运行天数
        result.AddCheck(
            "运行天数",
            runningDays >= MinRunningDays,
            $"{runningDays}/{MinRunningDays}",
            runningDays >= MinRunningDays
                ? $"✅ 运行时长达标 ({runningDays}天)"
                : $"❌ 运行时长不足,还需运行 {MinRunningDays - runningDays} 天"
        );

        // 检查5: 最大回撤
        result.AddCheck(
            "最大回撤",
            account.MaxDrawdown <= MaxDrawdownPercent,
            $"{account.MaxDrawdown:P0}/{MaxDrawdownPercent:P0}",
            account.MaxDrawdown <= MaxDrawdownPercent
                ? $"✅ 回撤控制良好 ({account.MaxDrawdown:P0})"
                : $"❌ 回撤过大 ({account.MaxDrawdown:P0}),需优化风控"
        );

        // 检查6: 盈亏比
        result.AddCheck(
            "盈亏比",
            account.ProfitFactor >= MinProfitFactor,
            $"{account.ProfitFactor:F2}/{MinProfitFactor:F2}",
            account.ProfitFactor >= MinProfitFactor
                ? $"✅ 盈亏比达标 ({account.ProfitFactor:F2})"
                : $"❌ 盈亏比不足,还需提升 {(MinProfitFactor - account.ProfitFactor):F2}"
        );

        // 检查7: 夏普比率
        result.AddCheck(
            "夏普比率",
            account.SharpeRatio >= MinSharpeRatio,
            $"{account.SharpeRatio:F2}/{MinSharpeRatio:F2}",
            account.SharpeRatio >= MinSharpeRatio
                ? $"✅ 夏普比率达标 ({account.SharpeRatio:F2})"
                : $"❌ 夏普比率不足,还需提升 {(MinSharpeRatio - account.SharpeRatio):F2}"
        );

        // 最终结果
        result.CanUpgrade = result.PassedChecks == result.TotalChecks;
        result.Message = result.CanUpgrade
            ? $"🎉 恭喜!您的模拟账户已达标,可以升级到真实账户!"
            : $"还需满足 {result.FailedChecks} 个条件才能升级";

        return result;
    }

    #endregion

    #region 预设规则

    /// <summary>
    /// 保守型规则 (默认)
    /// </summary>
    public static AccountUpgradeRules Conservative => new()
    {
        MinTrades = 100,
        MinWinRate = 0.55,
        MinTotalReturnPercent = 0.10,
        MinRunningDays = 30,
        MaxDrawdownPercent = 0.15,
        MinProfitFactor = 1.5,
        MinSharpeRatio = 0.8
    };

    /// <summary>
    /// 激进型规则 (降低门槛,适合有经验的交易者)
    /// </summary>
    public static AccountUpgradeRules Aggressive => new()
    {
        MinTrades = 50,
        MinWinRate = 0.50,
        MinTotalReturnPercent = 0.05,
        MinRunningDays = 14,
        MaxDrawdownPercent = 0.20,
        MinProfitFactor = 1.2,
        MinSharpeRatio = 0.5
    };

    /// <summary>
    /// 专业型规则 (更严格,追求稳定性)
    /// </summary>
    public static AccountUpgradeRules Professional => new()
    {
        MinTrades = 200,
        MinWinRate = 0.60,
        MinTotalReturnPercent = 0.15,
        MinRunningDays = 60,
        MaxDrawdownPercent = 0.10,
        MinProfitFactor = 2.0,
        MinSharpeRatio = 1.2
    };

    #endregion
}

/// <summary>
/// 账户升级检查结果
/// </summary>
public class AccountUpgradeResult
{
    /// <summary>
    /// 是否可以升级
    /// </summary>
    public bool CanUpgrade { get; set; }

    /// <summary>
    /// 总体消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 检查项列表
    /// </summary>
    public List<UpgradeCheckItem> CheckItems { get; } = new();

    /// <summary>
    /// 通过的检查数量
    /// </summary>
    public int PassedChecks => CheckItems.Count(c => c.Passed);

    /// <summary>
    /// 失败的检查数量
    /// </summary>
    public int FailedChecks => CheckItems.Count(c => !c.Passed);

    /// <summary>
    /// 总检查数量
    /// </summary>
    public int TotalChecks => CheckItems.Count;

    /// <summary>
    /// 完成度百分比
    /// </summary>
    public double CompletionPercent => TotalChecks == 0 ? 0 : (double)PassedChecks / TotalChecks;

    /// <summary>
    /// 添加检查项
    /// </summary>
    public void AddCheck(string name, bool passed, string value, string message)
    {
        CheckItems.Add(new UpgradeCheckItem
        {
            Name = name,
            Passed = passed,
            Value = value,
            Message = message
        });
    }

    /// <summary>
    /// 获取升级报告
    /// </summary>
    public string GetReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("账户升级检查报告");
        sb.AppendLine("==================");
        sb.AppendLine();
        sb.AppendLine($"总体结果: {(CanUpgrade ? "✅ 通过" : "❌ 未通过")}");
        sb.AppendLine($"完成度: {CompletionPercent:P0} ({PassedChecks}/{TotalChecks})");
        sb.AppendLine();
        sb.AppendLine("详细检查:");

        foreach (UpgradeCheckItem item in CheckItems)
        {
            sb.AppendLine($"{item.Name}: {item.Value}");
            sb.AppendLine($"  {item.Message}");
        }

        sb.AppendLine();
        sb.AppendLine(Message);

        return sb.ToString();
    }

    /// <summary>
    /// 生成失败的升级结果
    /// </summary>
    public static AccountUpgradeResult Failed(string message)
    {
        return new AccountUpgradeResult
        {
            CanUpgrade = false,
            Message = message
        };
    }
}

/// <summary>
/// 升级检查项
/// </summary>
public class UpgradeCheckItem
{
    /// <summary>
    /// 检查项名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 是否通过
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// 当前值/目标值
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// 详细消息
    /// </summary>
    public required string Message { get; init; }
}
