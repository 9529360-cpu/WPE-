using System;
using System.Collections.Generic;
using System.Linq;

namespace 币安量化机器人.Indicators;

/// <summary>
/// 技术指标计算库
/// </summary>
public static class TechnicalIndicators
{
    /// <summary>
    /// 简单移动平均线 (SMA)
    /// </summary>
    public static double[] SMA(double[] prices, int period)
    {
        double[] result = new double[prices.Length];
        for (int i = period - 1; i < prices.Length; i++)
        {
            result[i] = prices.Skip(i - period + 1).Take(period).Average();
        }
        return result;
    }

    /// <summary>
    /// 指数移动平均线 (EMA)
    /// </summary>
    public static double[] EMA(double[] prices, int period)
    {
        double[] result = new double[prices.Length];
        double multiplier = 2.0 / (period + 1);
        result[0] = prices[0];

        for (int i = 1; i < prices.Length; i++)
        {
            result[i] = (prices[i] - result[i - 1]) * multiplier + result[i - 1];
        }
        return result;
    }

    /// <summary>
    /// 相对强弱指标 (RSI)
    /// </summary>
    public static double[] RSI(double[] prices, int period = 14)
    {
        double[] result = new double[prices.Length];
        var gains = new List<double>();
        var losses = new List<double>();

        for (int i = 1; i < prices.Length; i++)
        {
            double change = prices[i] - prices[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);

            if (i >= period)
            {
                double avgGain = gains.Skip(i - period).Average();
                double avgLoss = losses.Skip(i - period).Average();
                double rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
                result[i] = 100 - (100 / (1 + rs));
            }
        }
        return result;
    }

    /// <summary>
    /// MACD (移动平均收敛发散指标)
    /// </summary>
    public static (double[] macd, double[] signal, double[] histogram) MACD(
        double[] prices,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        double[] fastEMA = EMA(prices, fastPeriod);
        double[] slowEMA = EMA(prices, slowPeriod);
        double[] macd = fastEMA.Zip(slowEMA, (f, s) => f - s).ToArray();
        double[] signal = EMA(macd, signalPeriod);
        double[] histogram = macd.Zip(signal, (m, s) => m - s).ToArray();

        return (macd, signal, histogram);
    }

    /// <summary>
    /// 布林带 (Bollinger Bands)
    /// </summary>
    public static (double[] upper, double[] middle, double[] lower) BollingerBands(
        double[] prices,
        int period = 20,
        double stdDev = 2.0)
    {
        double[] middle = SMA(prices, period);
        double[] upper = new double[prices.Length];
        double[] lower = new double[prices.Length];

        for (int i = period - 1; i < prices.Length; i++)
        {
            double[] slice = prices.Skip(i - period + 1).Take(period).ToArray();
            double std = StandardDeviation(slice);
            upper[i] = middle[i] + stdDev * std;
            lower[i] = middle[i] - stdDev * std;
        }

        return (upper, middle, lower);
    }

    /// <summary>
    /// ATR (平均真实波幅)
    /// </summary>
    public static double[] ATR(double[] highs, double[] lows, double[] closes, int period = 14)
    {
        double[] result = new double[closes.Length];
        double[] trueRanges = new double[closes.Length];

        for (int i = 1; i < closes.Length; i++)
        {
            double hl = highs[i] - lows[i];
            double hc = Math.Abs(highs[i] - closes[i - 1]);
            double lc = Math.Abs(lows[i] - closes[i - 1]);
            trueRanges[i] = Math.Max(hl, Math.Max(hc, lc));
        }

        for (int i = period; i < closes.Length; i++)
        {
            result[i] = trueRanges.Skip(i - period + 1).Take(period).Average();
        }

        return result;
    }

    /// <summary>
    /// 标准差
    /// </summary>
    private static double StandardDeviation(double[] values)
    {
        double avg = values.Average();
        double sum = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sum / values.Length);
    }
}
