using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace 币安量化机器人.Services;

/// <summary>
/// 配置验证服务
/// Configuration validation service
/// </summary>
public class ConfigurationValidator
{
    private readonly ILogger _logger = LoggerFactory.CreateLogger<ConfigurationValidator>();
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Errors => _errors.AsReadOnly();
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// 验证策略配置
    /// Validate strategy configuration
    /// </summary>
    public bool ValidateStrategyConfig(
        string strategyName,
        string p1Name,
        double p1Min,
        double p1Max,
        double p1Default,
        string p2Name,
        double p2Min,
        double p2Max,
        double p2Default)
    {
        _errors.Clear();
        _warnings.Clear();

        _logger.Debug($"验证策略配置: {strategyName}");

        // 验证策略名称
        if (string.IsNullOrWhiteSpace(strategyName))
        {
            _errors.Add("策略名称不能为空");
        }
        else if (strategyName.Length < 2)
        {
            _warnings.Add("策略名称过短，建议至少2个字符");
        }
        else if (strategyName.Length > 50)
        {
            _errors.Add("策略名称过长，最多50个字符");
        }

        // 验证非法字符
        var invalidChars = Path.GetInvalidFileNameChars();
        if (strategyName.Any(c => invalidChars.Contains(c)))
        {
            _errors.Add($"策略名称包含非法字符：{string.Join(", ", invalidChars.Where(strategyName.Contains))}");
        }

        // 验证参数1
        ValidateParameter("参数1", p1Name, p1Min, p1Max, p1Default);

        // 验证参数2
        ValidateParameter("参数2", p2Name, p2Min, p2Max, p2Default);

        // 记录验证结果
        if (_errors.Count > 0)
        {
            _logger.Warning($"策略配置验证失败: {_errors.Count} 个错误, {_warnings.Count} 个警告");
            foreach (var error in _errors)
            {
                _logger.Warning($"  错误: {error}");
            }
        }
        else if (_warnings.Count > 0)
        {
            _logger.Info($"策略配置验证通过，但有 {_warnings.Count} 个警告");
        }
        else
        {
            _logger.Info($"策略配置验证通过: {strategyName}");
        }

        return IsValid;
    }

    private void ValidateParameter(string label, string name, double min, double max, double defaultValue)
    {
        // 验证参数名称
        if (string.IsNullOrWhiteSpace(name))
        {
            _errors.Add($"{label}名称不能为空");
            return;
        }

        // 验证范围
        if (min >= max)
        {
            _errors.Add($"{label}（{name}）的最小值必须小于最大值（当前：min={min}, max={max}）");
        }

        // 验证默认值
        if (defaultValue < min || defaultValue > max)
        {
            _errors.Add($"{label}（{name}）的默认值必须在最小值和最大值之间（当前：default={defaultValue}, range=[{min}, {max}]）");
        }

        // 检查范围是否太小
        var range = max - min;
        if (range < 0.001)
        {
            _warnings.Add($"{label}（{name}）的取值范围过小：{range:F6}");
        }

        // 检查范围是否太大
        if (range > 1000000)
        {
            _warnings.Add($"{label}（{name}）的取值范围过大：{range:F2}，可能导致性能问题");
        }
    }

    /// <summary>
    /// 验证WFO优化参数
    /// Validate WFO optimization parameters
    /// </summary>
    public bool ValidateWfoParameters(
        int isLength,
        int oosLength,
        double p1Min,
        double p1Max,
        double p1Step,
        double p2Min,
        double p2Max,
        double p2Step,
        int maxCombinations = 10000)
    {
        _errors.Clear();
        _warnings.Clear();

        _logger.Debug($"验证WFO参数: IS={isLength}, OOS={oosLength}");

        // 验证时间窗口长度
        if (isLength <= 0)
        {
            _errors.Add("样本内周期必须大于0");
        }
        else if (isLength < 30)
        {
            _warnings.Add($"样本内周期过短（{isLength}天），建议至少30天");
        }

        if (oosLength <= 0)
        {
            _errors.Add("样本外周期必须大于0");
        }
        else if (oosLength < 7)
        {
            _warnings.Add($"样本外周期过短（{oosLength}天），建议至少7天");
        }

        // 验证参数1
        ValidateOptimizationParameter("参数1", p1Min, p1Max, p1Step);

        // 验证参数2
        ValidateOptimizationParameter("参数2", p2Min, p2Max, p2Step);

        // 验证搜索空间大小
        if (p1Step > 0 && p2Step > 0)
        {
            var p1Count = (int)Math.Ceiling((p1Max - p1Min) / p1Step) + 1;
            var p2Count = (int)Math.Ceiling((p2Max - p2Min) / p2Step) + 1;
            var totalCombinations = p1Count * p2Count;

            if (totalCombinations > maxCombinations)
            {
                _errors.Add($"搜索空间过大：{totalCombinations} 组合（限制：{maxCombinations}）。" +
                           $"建议增大步长或缩小范围。");
            }
            else if (totalCombinations > maxCombinations / 2)
            {
                _warnings.Add($"搜索空间较大：{totalCombinations} 组合，优化可能需要较长时间");
            }
        }

        // 记录验证结果
        if (_errors.Count > 0)
        {
            _logger.Warning($"WFO参数验证失败: {_errors.Count} 个错误, {_warnings.Count} 个警告");
        }
        else if (_warnings.Count > 0)
        {
            _logger.Info($"WFO参数验证通过，但有 {_warnings.Count} 个警告");
        }
        else
        {
            _logger.Info("WFO参数验证通过");
        }

        return IsValid;
    }

    private void ValidateOptimizationParameter(string label, double min, double max, double step)
    {
        if (min >= max)
        {
            _errors.Add($"{label}的最小值必须小于最大值（min={min}, max={max}）");
        }

        if (step <= 0)
        {
            _errors.Add($"{label}的步长必须大于0（当前：{step}）");
        }

        if (step > (max - min))
        {
            _warnings.Add($"{label}的步长（{step}）大于取值范围（{max - min}），将只有1个值被测试");
        }
    }

    /// <summary>
    /// 获取所有验证消息（错误+警告）
    /// Get all validation messages (errors + warnings)
    /// </summary>
    public string GetAllMessages()
    {
        var messages = new List<string>();

        if (_errors.Count > 0)
        {
            messages.Add("【错误】");
            messages.AddRange(_errors.Select(e => $"  • {e}"));
        }

        if (_warnings.Count > 0)
        {
            if (messages.Count > 0)
                messages.Add("");
            messages.Add("【警告】");
            messages.AddRange(_warnings.Select(w => $"  • {w}"));
        }

        return string.Join(Environment.NewLine, messages);
    }

    /// <summary>
    /// 获取错误消息
    /// Get error messages
    /// </summary>
    public string GetErrorMessages()
    {
        return string.Join(Environment.NewLine, _errors.Select(e => $"• {e}"));
    }

    /// <summary>
    /// 获取警告消息
    /// Get warning messages
    /// </summary>
    public string GetWarningMessages()
    {
        return string.Join(Environment.NewLine, _warnings.Select(w => $"• {w}"));
    }
}
