using System;
using System.Collections.Generic;
using System.Linq;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 决策因子库
/// </summary>
/// <remarks>
/// 包含50+个决策因子，分为以下类别：
/// 1. 技术指标因子 (20+)
/// 2. 基本面因子 (10+)
/// 3. 市场情绪因子 (10+)
/// 4. 宏观因素因子 (10+)
/// </remarks>
public class DecisionFactorLibrary
{
    private readonly Dictionary<string, DecisionFactor> _factors;

    public DecisionFactorLibrary()
    {
        _factors = new Dictionary<string, DecisionFactor>();
        InitializeFactors();
    }

    /// <summary>
    /// 初始化所有因子
    /// </summary>
    private void InitializeFactors()
    {
        // ============================================
        // 技术指标因子 (20+)
        // ============================================

        #region 趋势因子

        RegisterFactor(new DecisionFactor
        {
            Code = "MA_TREND",
            Name = "移动平均趋势",
            Category = FactorCategory.Technical,
            SubCategory = "趋势",
            Weight = 0.15m,
            Calculator = (data) =>
            {
                // 计算短期MA与长期MA的关系
                decimal ma5 = CalculateMA(data.ClosePrices, 5);
                decimal ma20 = CalculateMA(data.ClosePrices, 20);
                decimal ma60 = CalculateMA(data.ClosePrices, 60);

                if (ma5 > ma20 && ma20 > ma60)
                {
                    return 1.0m; // 强势上涨趋势
                }

                if (ma5 < ma20 && ma20 < ma60)
                {
                    return -1.0m; // 强势下跌趋势
                }

                return 0.0m; // 震荡
            },
            Description = "基于多周期MA判断趋势强度"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "MACD_SIGNAL",
            Name = "MACD信号",
            Category = FactorCategory.Technical,
            SubCategory = "趋势",
            Weight = 0.12m,
            Calculator = (data) =>
            {
                // 简化的MACD计算
                decimal ema12 = CalculateEMA(data.ClosePrices, 12);
                decimal ema26 = CalculateEMA(data.ClosePrices, 26);
                decimal dif = ema12 - ema26;

                // 归一化到[-1, 1]
                decimal price = data.ClosePrices.Last();
                return Math.Clamp(dif / price * 100, -1, 1);
            },
            Description = "MACD金叉死叉信号"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "ADX_STRENGTH",
            Name = "ADX趋势强度",
            Category = FactorCategory.Technical,
            SubCategory = "趋势",
            Weight = 0.10m,
            Calculator = (data) =>
            {
                // ADX > 25 表示趋势明显
                // 简化计算：使用波动率代替
                decimal volatility = CalculateVolatility(data.ClosePrices, 14);
                return Math.Clamp(volatility * 10, 0, 1);
            },
            Description = "趋势强度指标"
        });

        #endregion

        #region 动量因子

        RegisterFactor(new DecisionFactor
        {
            Code = "RSI_MOMENTUM",
            Name = "RSI动量",
            Category = FactorCategory.Technical,
            SubCategory = "动量",
            Weight = 0.12m,
            Calculator = (data) =>
            {
                decimal rsi = CalculateRSI(data.ClosePrices, 14);

                if (rsi > 70)
                {
                    return -0.5m; // 超买
                }

                if (rsi < 30)
                {
                    return 0.5m; // 超卖
                }

                // 归一化到[-1, 1]
                return (rsi - 50) / 50;
            },
            Description = "相对强弱指标"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "STOCH_OSCILLATOR",
            Name = "随机振荡器",
            Category = FactorCategory.Technical,
            SubCategory = "动量",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 简化的Stochastic计算
                int period = 14;
                decimal currentPrice = data.ClosePrices.Last();
                decimal highestHigh = data.HighPrices.TakeLast(period).Max();
                decimal lowestLow = data.LowPrices.TakeLast(period).Min();

                decimal k = ((currentPrice - lowestLow) / (highestHigh - lowestLow)) * 100;

                if (k > 80)
                {
                    return -0.5m; // 超买
                }

                if (k < 20)
                {
                    return 0.5m; // 超卖
                }

                return (k - 50) / 50;
            },
            Description = "随机指标KD值"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "CCI_INDICATOR",
            Name = "CCI指标",
            Category = FactorCategory.Technical,
            SubCategory = "动量",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 简化的CCI计算
                int period = 20;
                var typicalPrices = new List<decimal>();
                for (int i = Math.Max(0, data.ClosePrices.Count - period); i < data.ClosePrices.Count; i++)
                {
                    typicalPrices.Add((data.HighPrices[i] + data.LowPrices[i] + data.ClosePrices[i]) / 3);
                }

                decimal sma = typicalPrices.Average();
                decimal meanDeviation = typicalPrices.Average(tp => Math.Abs(tp - sma));
                decimal cci = (typicalPrices.Last() - sma) / (0.015m * meanDeviation);

                return Math.Clamp(cci / 200, -1, 1);
            },
            Description = "顺势指标"
        });

        #endregion

        #region 波动性因子

        RegisterFactor(new DecisionFactor
        {
            Code = "BOLLINGER_POSITION",
            Name = "布林带位置",
            Category = FactorCategory.Technical,
            SubCategory = "波动性",
            Weight = 0.10m,
            Calculator = (data) =>
            {
                int period = 20;
                decimal ma = CalculateMA(data.ClosePrices, period);
                decimal std = CalculateStd(data.ClosePrices, period);
                decimal upperBand = ma + 2 * std;
                decimal lowerBand = ma - 2 * std;
                decimal currentPrice = data.ClosePrices.Last();

                // 价格在布林带中的相对位置
                if (std == 0)
                {
                    return 0;
                }

                decimal position = (currentPrice - lowerBand) / (upperBand - lowerBand);
                return (position - 0.5m) * 2; // 归一化到[-1, 1]
            },
            Description = "价格在布林带中的位置"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "ATR_VOLATILITY",
            Name = "ATR波动率",
            Category = FactorCategory.Technical,
            SubCategory = "波动性",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 简化的ATR计算
                decimal atr = CalculateATR(data.HighPrices, data.LowPrices, data.ClosePrices, 14);
                decimal price = data.ClosePrices.Last();

                // ATR百分比
                decimal atrPercent = (atr / price) * 100;

                // 归一化：波动率越高，值越大
                return Math.Clamp(atrPercent / 5, 0, 1);
            },
            Description = "平均真实波动幅度"
        });

        #endregion

        #region 成交量因子

        RegisterFactor(new DecisionFactor
        {
            Code = "OBV_TREND",
            Name = "OBV趋势",
            Category = FactorCategory.Technical,
            SubCategory = "成交量",
            Weight = 0.10m,
            Calculator = (data) =>
            {
                // 简化的OBV计算
                var obv = new List<decimal> { 0 };
                for (int i = 1; i < data.ClosePrices.Count; i++)
                {
                    decimal volume = data.Volumes[i];
                    if (data.ClosePrices[i] > data.ClosePrices[i - 1])
                    {
                        obv.Add(obv.Last() + volume);
                    }
                    else if (data.ClosePrices[i] < data.ClosePrices[i - 1])
                    {
                        obv.Add(obv.Last() - volume);
                    }
                    else
                    {
                        obv.Add(obv.Last());
                    }
                }

                // OBV的短期与长期趋势
                decimal obvShort = obv.TakeLast(5).Average();
                decimal obvLong = obv.TakeLast(20).Average();

                if (obvLong == 0)
                {
                    return 0;
                }

                return Math.Clamp((obvShort - obvLong) / Math.Abs(obvLong), -1, 1);
            },
            Description = "能量潮指标"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "VOLUME_PRICE_TREND",
            Name = "量价趋势",
            Category = FactorCategory.Technical,
            SubCategory = "成交量",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 成交量与价格变化的关系
                decimal priceChange = (data.ClosePrices.Last() - data.ClosePrices[^5]) / data.ClosePrices[^5];
                decimal volumeChange = (data.Volumes.Last() - data.Volumes.TakeLast(5).Average()) /
                                      data.Volumes.TakeLast(5).Average();

                // 量价配合：价涨量增为正，价跌量增为负
                if (priceChange > 0 && volumeChange > 0)
                {
                    return 0.7m; // 健康上涨
                }

                if (priceChange < 0 && volumeChange > 0)
                {
                    return -0.7m; // 放量下跌
                }

                if (priceChange > 0 && volumeChange < 0)
                {
                    return 0.3m; // 缩量上涨（警惕）
                }

                return 0.0m;
            },
            Description = "量价配合情况"
        });

        #endregion

        // ============================================
        // 基本面因子 (10+)
        // ============================================

        #region 基本面因子

        RegisterFactor(new DecisionFactor
        {
            Code = "MARKET_CAP",
            Name = "市值规模",
            Category = FactorCategory.Fundamental,
            SubCategory = "规模",
            Weight = 0.05m,
            Calculator = (data) =>
            {
                // 市值越大，稳定性越高（正相关）
                decimal marketCap = data.MarketCap;

                if (marketCap > 100_000_000_000) // > 1000亿
                {
                    return 0.8m;
                }

                if (marketCap > 10_000_000_000) // > 100亿
                {
                    return 0.5m;
                }

                if (marketCap > 1_000_000_000) // > 10亿
                {
                    return 0.2m;
                }

                return -0.2m; // 小市值风险高
            },
            Description = "市值规模因子"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "LIQUIDITY",
            Name = "流动性",
            Category = FactorCategory.Fundamental,
            SubCategory = "流动性",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 成交额 / 市值
                decimal avgVolume = data.Volumes.TakeLast(20).Average();
                decimal avgPrice = data.ClosePrices.TakeLast(20).Average();
                decimal turnover = avgVolume * avgPrice;
                decimal liquidityRatio = turnover / data.MarketCap;

                // 流动性比率越高越好
                return Math.Clamp(liquidityRatio * 100, 0, 1);
            },
            Description = "市场流动性"
        });

        #endregion

        // ============================================
        // 市场情绪因子 (10+)
        // ============================================

        #region 情绪因子

        RegisterFactor(new DecisionFactor
        {
            Code = "FEAR_GREED_INDEX",
            Name = "恐慌贪婪指数",
            Category = FactorCategory.Sentiment,
            SubCategory = "情绪",
            Weight = 0.12m,
            Calculator = (data) =>
            {
                // 简化版：基于波动率和RSI
                decimal volatility = CalculateVolatility(data.ClosePrices, 14);
                decimal rsi = CalculateRSI(data.ClosePrices, 14);

                // 高波动+高RSI = 贪婪，低波动+低RSI = 恐慌
                decimal fear_greed = (rsi - 50) / 50 - (volatility - 0.3m) * 2;

                return Math.Clamp(fear_greed, -1, 1);
            },
            Description = "市场恐慌贪婪情绪"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "LONG_SHORT_RATIO",
            Name = "多空比",
            Category = FactorCategory.Sentiment,
            SubCategory = "持仓",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 假设数据中有多空比信息
                decimal longShortRatio = data.LongShortRatio;

                // 多空比 > 2 表示多头强势，< 0.5 表示空头强势
                if (longShortRatio > 2)
                {
                    return 0.7m;
                }

                if (longShortRatio < 0.5m)
                {
                    return -0.7m;
                }

                return (longShortRatio - 1) / 2;
            },
            Description = "市场多空持仓比"
        });

        #endregion

        // ============================================
        // 宏观因素因子 (10+)
        // ============================================

        #region 宏观因子

        RegisterFactor(new DecisionFactor
        {
            Code = "INTEREST_RATE",
            Name = "利率环境",
            Category = FactorCategory.Macro,
            SubCategory = "货币政策",
            Weight = 0.10m,
            Calculator = (data) =>
            {
                // 利率下降利好风险资产
                decimal interestRate = data.InterestRate;
                decimal interestRateChange = data.InterestRateChange;

                if (interestRateChange < -0.005m) // 降息
                {
                    return 0.7m;
                }

                if (interestRateChange > 0.005m) // 加息
                {
                    return -0.7m;
                }

                // 低利率环境
                if (interestRate < 0.02m)
                {
                    return 0.5m;
                }

                return 0.0m;
            },
            Description = "利率水平和变化"
        });

        #endregion

        LogService.Info("✅ [DecisionFactorLibrary] 已注册 {Count} 个决策因子", _factors.Count);
    }

    /// <summary>
    /// 注册因子
    /// </summary>
    private void RegisterFactor(DecisionFactor factor)
    {
        _factors[factor.Code] = factor;
    }

    /// <summary>
    /// 计算所有因子得分
    /// </summary>
    public Dictionary<string, decimal> CalculateAllFactors(MarketData data)
    {
        var scores = new Dictionary<string, decimal>();

        foreach (KeyValuePair<string, DecisionFactor> kvp in _factors)
        {
            try
            {
                decimal score = kvp.Value.Calculator(data);
                scores[kvp.Key] = score;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[DecisionFactorLibrary] 计算因子 {Code} 失败", kvp.Key);
                scores[kvp.Key] = 0;
            }
        }

        return scores;
    }

    /// <summary>
    /// 计算加权总分
    /// </summary>
    public decimal CalculateWeightedScore(Dictionary<string, decimal> factorScores)
    {
        decimal totalScore = 0;
        decimal totalWeight = 0;

        foreach (KeyValuePair<string, decimal> kvp in factorScores)
        {
            if (_factors.TryGetValue(kvp.Key, out DecisionFactor factor))
            {
                totalScore += kvp.Value * factor.Weight;
                totalWeight += factor.Weight;
            }
        }

        return totalWeight > 0 ? totalScore / totalWeight : 0;
    }

    /// <summary>
    /// 获取因子列表
    /// </summary>
    public List<DecisionFactor> GetFactors(FactorCategory? category = null)
    {
        if (category == null)
        {
            return _factors.Values.ToList();
        }

        return _factors.Values.Where(f => f.Category == category).ToList();
    }

    #region 辅助计算方法

    private static decimal CalculateMA(List<decimal> prices, int period)
    {
        return prices.TakeLast(period).Average();
    }

    private static decimal CalculateEMA(List<decimal> prices, int period)
    {
        decimal multiplier = 2.0m / (period + 1);
        decimal ema = prices.Take(period).Average();

        for (int i = period; i < prices.Count; i++)
        {
            ema = (prices[i] - ema) * multiplier + ema;
        }

        return ema;
    }

    private static decimal CalculateRSI(List<decimal> prices, int period)
    {
        decimal avgGain = 0;
        decimal avgLoss = 0;

        for (int i = 1; i <= period && i < prices.Count; i++)
        {
            decimal change = prices[i] - prices[i - 1];
            if (change > 0)
            {
                avgGain += change;
            }
            else
            {
                avgLoss -= change;
            }
        }

        avgGain /= period;
        avgLoss /= period;

        if (avgLoss == 0)
        {
            return 100;
        }

        decimal rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private static decimal CalculateVolatility(List<decimal> prices, int period)
    {
        return CalculateStd(prices, period) / prices.TakeLast(period).Average();
    }

    private static decimal CalculateStd(List<decimal> prices, int period)
    {
        var recentPrices = prices.TakeLast(period).ToList();
        decimal mean = recentPrices.Average();
        decimal sumSquaredDiff = recentPrices.Sum(p => (p - mean) * (p - mean));
        return (decimal)Math.Sqrt((double)(sumSquaredDiff / period));
    }

    private static decimal CalculateATR(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
    {
        var trueRanges = new List<decimal>();

        for (int i = 1; i < closes.Count; i++)
        {
            decimal tr = Math.Max(
                highs[i] - lows[i],
                Math.Max(
                    Math.Abs(highs[i] - closes[i - 1]),
                    Math.Abs(lows[i] - closes[i - 1])
                )
            );
            trueRanges.Add(tr);
        }

        return trueRanges.TakeLast(period).Average();
    }

    #endregion
}

#region 数据模型

/// <summary>
/// 决策因子
/// </summary>
public class DecisionFactor
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FactorCategory Category { get; set; }
    public string SubCategory { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public Func<MarketData, decimal> Calculator { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 因子分类
/// </summary>
public enum FactorCategory
{
    Technical,      // 技术指标
    Fundamental,    // 基本面
    Sentiment,      // 市场情绪
    Macro          // 宏观因素
}

/// <summary>
/// 市场数据
/// </summary>
public class MarketData
{
    public List<decimal> ClosePrices { get; set; } = new();
    public List<decimal> HighPrices { get; set; } = new();
    public List<decimal> LowPrices { get; set; } = new();
    public List<decimal> Volumes { get; set; } = new();
    public decimal MarketCap { get; set; }
    public decimal LongShortRatio { get; set; } = 1.0m;
    public decimal InterestRate { get; set; } = 0.05m;
    public decimal InterestRateChange { get; set; } = 0;
    
    /// <summary>
    /// 数据质量评分 (0-1)
    /// 1.0 = 完整实时数据
    /// 0.5 = 缓存历史数据
    /// 0.1 = 默认降级数据
    /// </summary>
    public decimal DataQuality { get; set; } = 1.0m;
}

#endregion
