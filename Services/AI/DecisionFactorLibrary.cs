using System;
using System.Collections.Generic;
using System.Linq;

namespace 甯佸畨閲忓寲鏈哄櫒浜?Services.AI;

/// <summary>
/// 鍐崇瓥鍥犲瓙搴?
/// </summary>
/// <remarks>
/// 鍖呭惈50+涓喅绛栧洜瀛愶紝鍒嗕负浠ヤ笅绫诲埆锛?
/// 1. 鎶€鏈寚鏍囧洜瀛?(20+)
/// 2. 鍩烘湰闈㈠洜瀛?(10+)
/// 3. 甯傚満鎯呯华鍥犲瓙 (10+)
/// 4. 瀹忚鍥犵礌鍥犲瓙 (10+)
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
    /// 鍒濆鍖栨墍鏈夊洜瀛?
    /// </summary>
    private void InitializeFactors()
    {
        // ============================================
        // 鎶€鏈寚鏍囧洜瀛?(20+)
        // ============================================

        #region 瓒嬪娍鍥犲瓙

        RegisterFactor(new DecisionFactor
        {
            Code = "MA_TREND",
            Name = "绉诲姩骞冲潎瓒嬪娍",
            Category = FactorCategory.Technical,
            SubCategory = "瓒嬪娍",
            Weight = 0.15m,
            Calculator = (data) =>
            {
                // 璁＄畻鐭湡MA涓庨暱鏈烳A鐨勫叧绯?  = CalculateMA(data.ClosePrices, 5);  = CalculateMA(data.ClosePrices, 20);  = CalculateMA(data.ClosePrices, 60);

                if (ma5 > ma20 && ma20 > ma60)
                {
                    return 1.0m; // 寮哄娍涓婃定瓒嬪娍
                }

                if (ma5 < ma20 && ma20 < ma60)
                {
                    return -1.0m; // 寮哄娍涓嬭穼瓒嬪娍
                }

                return 0.0m; // 闇囪崱
            },
            Description = "鍩轰簬澶氬懆鏈烳A鍒ゆ柇瓒嬪娍寮哄害"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "MACD_SIGNAL",
            Name = "MACD淇″彿",
            Category = FactorCategory.Technical,
            SubCategory = "瓒嬪娍",
            Weight = 0.12m,
            Calculator = (data) =>
            {
                // 绠€鍖栫殑MACD璁＄畻  = CalculateEMA(data.ClosePrices, 12);  = CalculateEMA(data.ClosePrices, 26);  = ema12 - ema26;

                // 褰掍竴鍖栧埌[-1, 1]  = data.ClosePrices.Last();
                return Math.Clamp(dif / price * 100, -1, 1);
            },
            Description = "MACD閲戝弶姝诲弶淇″彿"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "ADX_STRENGTH",
            Name = "ADX瓒嬪娍寮哄害",
            Category = FactorCategory.Technical,
            SubCategory = "瓒嬪娍",
            Weight = 0.10m,
            Calculator = (data) =>
            {
                // ADX > 25 琛ㄧず瓒嬪娍鏄庢樉
                // 绠€鍖栬绠楋細浣跨敤娉㈠姩鐜囦唬鏇?  = CalculateVolatility(data.ClosePrices, 14);
                return Math.Clamp(volatility * 10, 0, 1);
            },
            Description = "瓒嬪娍寮哄害鎸囨爣"
        });

        #endregion

        #region 鍔ㄩ噺鍥犲瓙

        RegisterFactor(new DecisionFactor
        {
            Code = "RSI_MOMENTUM",
            Name = "RSI鍔ㄩ噺",
            Category = FactorCategory.Technical,
            SubCategory = "鍔ㄩ噺",
            Weight = 0.12m,
            Calculator = (data) =>
            {  = CalculateRSI(data.ClosePrices, 14);

                if (rsi > 70)
                {
                    return -0.5m; // 瓒呬拱
                }

                if (rsi < 30)
                {
                    return 0.5m; // 瓒呭崠
                }

                // 褰掍竴鍖栧埌[-1, 1]
                return (rsi - 50) / 50;
            },
            Description = "鐩稿寮哄急鎸囨爣"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "STOCH_OSCILLATOR",
            Name = "闅忔満鎸崱鍣?,
            Category = FactorCategory.Technical,
            SubCategory = "鍔ㄩ噺",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 绠€鍖栫殑Stochastic璁＄畻  = 14;  = data.ClosePrices.Last();  = data.HighPrices.TakeLast(period).Max();  = data.LowPrices.TakeLast(period).Min();  = ((currentPrice - lowestLow) / (highestHigh - lowestLow)) * 100;

                if (k > 80)
                {
                    return -0.5m; // 瓒呬拱
                }

                if (k < 20)
                {
                    return 0.5m; // 瓒呭崠
                }

                return (k - 50) / 50;
            },
            Description = "闅忔満鎸囨爣KD鍊?
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "CCI_INDICATOR",
            Name = "CCI鎸囨爣",
            Category = FactorCategory.Technical,
            SubCategory = "鍔ㄩ噺",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 绠€鍖栫殑CCI璁＄畻  = 20;
                var typicalPrices = new List<decimal>();
                for (var  = Math.Max(0, data.ClosePrices.Count - period); i < data.ClosePrices.Count; i++)
                {
                    typicalPrices.Add((data.HighPrices[i] + data.LowPrices[i] + data.ClosePrices[i]) / 3);
                }  = typicalPrices.Average();  = typicalPrices.Average(tp => Math.Abs(tp - sma));  = (typicalPrices.Last() - sma) / (0.015m * meanDeviation);

                return Math.Clamp(cci / 200, -1, 1);
            },
            Description = "椤哄娍鎸囨爣"
        });

        #endregion

        #region 娉㈠姩鎬у洜瀛?

        RegisterFactor(new DecisionFactor
        {
            Code = "BOLLINGER_POSITION",
            Name = "甯冩灄甯︿綅缃?,
            Category = FactorCategory.Technical,
            SubCategory = "娉㈠姩鎬?,
            Weight = 0.10m,
            Calculator = (data) =>
            {  = 20;  = CalculateMA(data.ClosePrices, period);  = CalculateStd(data.ClosePrices, period);  = ma + 2 * std;  = ma - 2 * std;  = data.ClosePrices.Last();

                // 浠锋牸鍦ㄥ竷鏋楀甫涓殑鐩稿浣嶇疆
                if (std == 0)
                {
                    return 0;
                }  = (currentPrice - lowerBand) / (upperBand - lowerBand);
                return (position - 0.5m) * 2; // 褰掍竴鍖栧埌[-1, 1]
            },
            Description = "浠锋牸鍦ㄥ竷鏋楀甫涓殑浣嶇疆"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "ATR_VOLATILITY",
            Name = "ATR娉㈠姩鐜?,
            Category = FactorCategory.Technical,
            SubCategory = "娉㈠姩鎬?,
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 绠€鍖栫殑ATR璁＄畻  = CalculateATR(data.HighPrices, data.LowPrices, data.ClosePrices, 14);  = data.ClosePrices.Last();

                // ATR鐧惧垎姣?  = (atr / price) * 100;

                // 褰掍竴鍖栵細娉㈠姩鐜囪秺楂橈紝鍊艰秺澶?
                return Math.Clamp(atrPercent / 5, 0, 1);
            },
            Description = "骞冲潎鐪熷疄娉㈠姩骞呭害"
        });

        #endregion

        #region 鎴愪氦閲忓洜瀛?

        RegisterFactor(new DecisionFactor
        {
            Code = "OBV_TREND",
            Name = "OBV瓒嬪娍",
            Category = FactorCategory.Technical,
            SubCategory = "鎴愪氦閲?,
            Weight = 0.10m,
            Calculator = (data) =>
            {
                // 绠€鍖栫殑OBV璁＄畻
                var obv = new List<decimal> { 0 };
                for (var  = 1; i < data.ClosePrices.Count; i++)
                {  = data.Volumes[i];
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

                // OBV鐨勭煭鏈熶笌闀挎湡瓒嬪娍  = obv.TakeLast(5).Average();  = obv.TakeLast(20).Average();

                if (obvLong == 0)
                {
                    return 0;
                }

                return Math.Clamp((obvShort - obvLong) / Math.Abs(obvLong), -1, 1);
            },
            Description = "鑳介噺娼寚鏍?
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "VOLUME_PRICE_TREND",
            Name = "閲忎环瓒嬪娍",
            Category = FactorCategory.Technical,
            SubCategory = "鎴愪氦閲?,
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 鎴愪氦閲忎笌浠锋牸鍙樺寲鐨勫叧绯?  = (data.ClosePrices.Last() - data.ClosePrices[^5]) / data.ClosePrices[^5];  = (data.Volumes.Last() - data.Volumes.TakeLast(5).Average()) /
                                      data.Volumes.TakeLast(5).Average();

                // 閲忎环閰嶅悎锛氫环娑ㄩ噺澧炰负姝ｏ紝浠疯穼閲忓涓鸿礋
                if (priceChange > 0 && volumeChange > 0)
                {
                    return 0.7m; // 鍋ュ悍涓婃定
                }

                if (priceChange < 0 && volumeChange > 0)
                {
                    return -0.7m; // 鏀鹃噺涓嬭穼
                }

                if (priceChange > 0 && volumeChange < 0)
                {
                    return 0.3m; // 缂╅噺涓婃定锛堣鎯曪級
                }

                return 0.0m;
            },
            Description = "閲忎环閰嶅悎鎯呭喌"
        });

        #endregion

        // ============================================
        // 鍩烘湰闈㈠洜瀛?(10+)
        // ============================================

        #region 鍩烘湰闈㈠洜瀛?

        RegisterFactor(new DecisionFactor
        {
            Code = "MARKET_CAP",
            Name = "甯傚€艰妯?,
            Category = FactorCategory.Fundamental,
            SubCategory = "瑙勬ā",
            Weight = 0.05m,
            Calculator = (data) =>
            {
                // 甯傚€艰秺澶э紝绋冲畾鎬ц秺楂橈紙姝ｇ浉鍏筹級  = data.MarketCap;

                if (marketCap > 100_000_000_000) // > 1000浜?
                {
                    return 0.8m;
                }

                if (marketCap > 10_000_000_000) // > 100浜?
                {
                    return 0.5m;
                }

                if (marketCap > 1_000_000_000) // > 10浜?
                {
                    return 0.2m;
                }

                return -0.2m; // 灏忓競鍊奸闄╅珮
            },
            Description = "甯傚€艰妯″洜瀛?
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "LIQUIDITY",
            Name = "娴佸姩鎬?,
            Category = FactorCategory.Fundamental,
            SubCategory = "娴佸姩鎬?,
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 鎴愪氦棰?/ 甯傚€?  = data.Volumes.TakeLast(20).Average();  = data.ClosePrices.TakeLast(20).Average();  = avgVolume * avgPrice;  = turnover / data.MarketCap;

                // 娴佸姩鎬ф瘮鐜囪秺楂樿秺濂?
                return Math.Clamp(liquidityRatio * 100, 0, 1);
            },
            Description = "甯傚満娴佸姩鎬?
        });

        #endregion

        // ============================================
        // 甯傚満鎯呯华鍥犲瓙 (10+)
        // ============================================

        #region 鎯呯华鍥犲瓙

        RegisterFactor(new DecisionFactor
        {
            Code = "FEAR_GREED_INDEX",
            Name = "鎭愭厡璐┆鎸囨暟",
            Category = FactorCategory.Sentiment,
            SubCategory = "鎯呯华",
            Weight = 0.12m,
            Calculator = (data) =>
            {
                // 绠€鍖栫増锛氬熀浜庢尝鍔ㄧ巼鍜孯SI  = CalculateVolatility(data.ClosePrices, 14);  = CalculateRSI(data.ClosePrices, 14);

                // 楂樻尝鍔?楂楻SI = 璐┆锛屼綆娉㈠姩+浣嶳SI = 鎭愭厡  = (rsi - 50) / 50 - (volatility - 0.3m) * 2;

                return Math.Clamp(fear_greed, -1, 1);
            },
            Description = "甯傚満鎭愭厡璐┆鎯呯华"
        });

        RegisterFactor(new DecisionFactor
        {
            Code = "LONG_SHORT_RATIO",
            Name = "澶氱┖姣?,
            Category = FactorCategory.Sentiment,
            SubCategory = "鎸佷粨",
            Weight = 0.08m,
            Calculator = (data) =>
            {
                // 鍋囪鏁版嵁涓湁澶氱┖姣斾俊鎭?  = data.LongShortRatio;

                // 澶氱┖姣?> 2 琛ㄧず澶氬ご寮哄娍锛? 0.5 琛ㄧず绌哄ご寮哄娍
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
            Description = "甯傚満澶氱┖鎸佷粨姣?
        });

        #endregion

        // ============================================
        // 瀹忚鍥犵礌鍥犲瓙 (10+)
        // ============================================

        #region 瀹忚鍥犲瓙

        RegisterFactor(new DecisionFactor
        {
            Code = "INTEREST_RATE",
            Name = "鍒╃巼鐜",
            Category = FactorCategory.Macro,
            SubCategory = "璐у竵鏀跨瓥",
            Weight = 0.10m,
            Calculator = (data) =>
            {
                // 鍒╃巼涓嬮檷鍒╁ソ椋庨櫓璧勪骇  = data.InterestRate;  = data.InterestRateChange;

                if (interestRateChange < -0.005m) // 闄嶆伅
                {
                    return 0.7m;
                }

                if (interestRateChange > 0.005m) // 鍔犳伅
                {
                    return -0.7m;
                }

                // 浣庡埄鐜囩幆澧?
                if (interestRate < 0.02m)
                {
                    return 0.5m;
                }

                return 0.0m;
            },
            Description = "鍒╃巼姘村钩鍜屽彉鍖?
        });

        #endregion

        LogService.Info("鉁?[DecisionFactorLibrary] 宸叉敞鍐?{Count} 涓喅绛栧洜瀛?, _factors.Count);
    }

    /// <summary>
    /// 娉ㄥ唽鍥犲瓙
    /// </summary>
    private void RegisterFactor(DecisionFactor factor)
    {
        _factors[factor.Code] = factor;
    }

    /// <summary>
    /// 璁＄畻鎵€鏈夊洜瀛愬緱鍒?
    /// </summary>
    public Dictionary<string, decimal> CalculateAllFactors(MarketData data)
    {
        var scores = new Dictionary<string, decimal>();

        foreach (var  in _factors)
        {
            try
            {  = kvp.Value.Calculator(data);
                scores[kvp.Key] = score;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[DecisionFactorLibrary] 璁＄畻鍥犲瓙 {Code} 澶辫触", kvp.Key);
                scores[kvp.Key] = 0;
            }
        }

        return scores;
    }

    /// <summary>
    /// 璁＄畻鍔犳潈鎬诲垎
    /// </summary>
    public decimal CalculateWeightedScore(Dictionary<string, decimal> factorScores)
    {  = 0;  = 0;

        foreach (var  in factorScores)
        {
            if (_factors.TryGetValue(, out var ))
            {
                totalScore += kvp.Value * factor.Weight;
                totalWeight += factor.Weight;
            }
        }

        return totalWeight > 0 ? totalScore / totalWeight : 0;
    }

    /// <summary>
    /// 鑾峰彇鍥犲瓙鍒楄〃
    /// </summary>
    public List<DecisionFactor> GetFactors(FactorCategory? category = null)
    {
        if (category == null)
        {
            return _factors.Values.ToList();
        }

        return _factors.Values.Where(f => f.Category == category).ToList();
    }

    /// <summary>
    /// 馃啎 Phase 3: 鑾峰彇鎵€鏈夊洜瀛愭潈閲?
    /// </summary>
    public Dictionary<string, decimal> GetFactorWeights()
    {
        return _factors.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Weight
        );
    }

    /// <summary>
    /// 馃啎 Phase 3: 鏇存柊鍥犲瓙鏉冮噸
    /// </summary>
    public void UpdateFactorWeights(Dictionary<string, decimal> newWeights)
    {  = 0;
        foreach (var (factorCode, newWeight) in newWeights)
        {
            if (_factors.TryGetValue(factorCode, out DecisionFactor? factor))
            {
                // 鍙湁褰撴潈閲嶆湁鏄庢樉鍙樺寲鏃舵墠鏇存柊
                if (Math.Abs(factor.Weight - newWeight) > 0.001m)
                {
                    factor.Weight = newWeight;
                    updatedCount++;
                }
            }
        }

        if (updatedCount > 0)
        {
            LogService.Info("[DecisionFactorLibrary] 宸叉洿鏂?{Count} 涓洜瀛愭潈閲?, updatedCount);
        }
    }

    /// <summary>
    /// 馃啎 Phase 3: 閲嶇疆鎵€鏈夊洜瀛愭潈閲嶄负榛樿鍊?
    /// </summary>
    public void ResetFactorWeights()
    {
        // 閲嶆柊鍒濆鍖栧洜瀛愶紙鎭㈠榛樿鏉冮噸锛?
        _factors.Clear();
        InitializeFactors();
        LogService.Info("[DecisionFactorLibrary] 鍥犲瓙鏉冮噸宸查噸缃负榛樿鍊?);
    }

    #region 杈呭姪璁＄畻鏂规硶

    private static decimal CalculateMA(List<decimal> prices, int period)
    {
        return prices.TakeLast(period).Average();
    }

    private static decimal CalculateEMA(List<decimal> prices, int period)
    {  = 2.0m / (period + 1);  = prices.Take(period).Average();

        for (var  = period; i < prices.Count; i++)
        {
            ema = (prices[i] - ema) * multiplier + ema;
        }

        return ema;
    }

    private static decimal CalculateRSI(List<decimal> prices, int period)
    {  = 0;  = 0;

        for (var  = 1; i <= period && i < prices.Count; i++)
        {  = prices[i] - prices[i - 1];
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
        }  = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private static decimal CalculateVolatility(List<decimal> prices, int period)
    {
        return CalculateStd(prices, period) / prices.TakeLast(period).Average();
    }

    private static decimal CalculateStd(List<decimal> prices, int period)
    {
        var recentPrices = prices.TakeLast(period).ToList();  = recentPrices.Average();  = recentPrices.Sum(p => (p - mean) * (p - mean));
        return (decimal)Math.Sqrt((double)(sumSquaredDiff / period));
    }

    private static decimal CalculateATR(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
    {
        var trueRanges = new List<decimal>();

        for (var  = 1; i < closes.Count; i++)
        {  = Math.Max(
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

#region 鏁版嵁妯″瀷

/// <summary>
/// 鍐崇瓥鍥犲瓙
/// </summary>
public class DecisionFactor
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FactorCategory Category { get; set; }
    public string SubCategory { get; set; } = string.Empty;
    public decimal Weight { get; set; }  // 馃敡 鏀逛负鍙慨鏀?
    public Func<MarketData, decimal> Calculator { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 鍥犲瓙鍒嗙被
/// </summary>
public enum FactorCategory
{
    Technical,      // 鎶€鏈寚鏍?
    Fundamental,    // 鍩烘湰闈?
    Sentiment,      // 甯傚満鎯呯华
    Macro          // 瀹忚鍥犵礌
}

/// <summary>
/// 甯傚満鏁版嵁
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
    /// 鏁版嵁璐ㄩ噺璇勫垎 (0-1)
    /// 1.0 = 瀹屾暣瀹炴椂鏁版嵁
    /// 0.5 = 缂撳瓨鍘嗗彶鏁版嵁
    /// 0.1 = 榛樿闄嶇骇鏁版嵁
    /// </summary>
    public decimal DataQuality { get; set; } = 1.0m;
}

#endregion
