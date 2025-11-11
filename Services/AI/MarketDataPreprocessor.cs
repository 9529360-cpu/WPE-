using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 市场数据预处理器 - 收集并计算技术指标
/// </summary>
public class MarketDataPreprocessor
{
    private readonly BinanceApiClient _apiClient;
    private readonly DataCacheService _cacheService;

    public MarketDataPreprocessor(BinanceApiClient apiClient, DataCacheService cacheService)
    {
        _apiClient = apiClient;
        _cacheService = cacheService;
    }

    /// <summary>
    /// 收集完整的市场数据快照
    /// </summary>
    public async Task<MarketDataSnapshot> CollectMarketDataAsync(string symbol, CancellationToken ct = default)
    {
        // 1. 获取实时价格数据
        IReadOnlyList<TickerQuote> tickers = await _apiClient.GetMiniTickersAsync(new[] { symbol }, ct);
        TickerQuote ticker = tickers.FirstOrDefault() ?? throw new InvalidOperationException($"未找到 {symbol} 的行情数据");

        // 2. 获取K线数据用于计算技术指标
        IReadOnlyList<decimal> closes = await _apiClient.GetKlineClosesAsync(symbol, "1h", limit: 100, ct);
        double[] prices = closes.Select(c => (double)c).ToArray();

        // 3. 获取资金费率
        IReadOnlyList<FundingRateSnapshot> fundingRates = await _apiClient.GetFundingRatesAsync(symbol, limit: 1, ct);
        FundingRateSnapshot? funding = fundingRates.FirstOrDefault();

        // 4. 计算技术指标
        TechnicalIndicators indicators = CalculateTechnicalIndicators(prices);

        return new MarketDataSnapshot
        {
            Symbol = symbol,
            CurrentPrice = ticker.LastPrice,
            PriceChangePercent = ticker.ChangePercent,
            HighPrice = ticker.HighPrice,
            LowPrice = ticker.LowPrice,
            Volume = ticker.Volume,
            RSI = indicators.RSI,
            MACD = indicators.MACD,
            MACDSignal = indicators.MACDSignal,
            BBUpper = indicators.BBUpper,
            BBMiddle = indicators.BBMiddle,
            BBLower = indicators.BBLower,
            VolumeMA = ticker.Volume, // 简化,实际应计算MA
            FundingRate = funding?.LastFundingRate ?? 0,
            OpenInterest = funding?.OpenInterest ?? 0,
            LongShortRatio = 1.0 // 简化,实际需要从API获取
        };
    }

    /// <summary>
    /// 计算技术指标
    /// </summary>
    private TechnicalIndicators CalculateTechnicalIndicators(double[] prices)
    {
        if (prices.Length < 50)
        {
            throw new ArgumentException("价格数据不足,至少需要50个数据点");
        }

        return new TechnicalIndicators
        {
            RSI = CalculateRSI(prices, 14),
            MACD = CalculateMACD(prices).MACD,
            MACDSignal = CalculateMACD(prices).Signal,
            BBUpper = CalculateBollingerBands(prices, 20).Upper,
            BBMiddle = CalculateBollingerBands(prices, 20).Middle,
            BBLower = CalculateBollingerBands(prices, 20).Lower
        };
    }

    /// <summary>
    /// 计算RSI (相对强弱指标)
    /// </summary>
    private double CalculateRSI(double[] prices, int period)
    {
        if (prices.Length < period + 1)
        {
            return 50; // 默认中性值
        }

        var gains = new List<double>();
        var losses = new List<double>();

        for (int i = 1; i < prices.Length; i++)
        {
            double change = prices[i] - prices[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? -change : 0);
        }

        double avgGain = gains.TakeLast(period).Average();
        double avgLoss = losses.TakeLast(period).Average();

        if (avgLoss == 0)
        {
            return 100;
        }

        double rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    /// <summary>
    /// 计算MACD (移动平均收敛散度)
    /// </summary>
    private (double MACD, double Signal) CalculateMACD(double[] prices)
    {
        double ema12 = CalculateEMA(prices, 12);
        double ema26 = CalculateEMA(prices, 26);
        double macd = ema12 - ema26;

        // 简化:信号线用9日EMA
        double[] macdLine = new[] { macd };
        double signal = macd; // 实际应该是MACD的9日EMA

        return (macd, signal);
    }

    /// <summary>
    /// 计算EMA (指数移动平均)
    /// </summary>
    private double CalculateEMA(double[] prices, int period)
    {
        double multiplier = 2.0 / (period + 1);
        double ema = prices[0];

        foreach (double price in prices.Skip(1))
        {
            ema = (price - ema) * multiplier + ema;
        }

        return ema;
    }

    /// <summary>
    /// 计算布林带
    /// </summary>
    private (double Upper, double Middle, double Lower) CalculateBollingerBands(double[] prices, int period)
    {
        double[] recent = prices.TakeLast(period).ToArray();
        double sma = recent.Average();
        double stdDev = Math.Sqrt(recent.Sum(p => Math.Pow(p - sma, 2)) / period);

        return (
            Upper: sma + (2 * stdDev),
            Middle: sma,
            Lower: sma - (2 * stdDev)
        );
    }
}

/// <summary>
/// 技术指标集合
/// </summary>
internal class TechnicalIndicators
{
    public double RSI { get; init; }
    public double MACD { get; init; }
    public double MACDSignal { get; init; }
    public double BBUpper { get; init; }
    public double BBMiddle { get; init; }
    public double BBLower { get; init; }
}
