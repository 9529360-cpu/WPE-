using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 回测引擎 - 历史数据模拟交易
    /// </summary>
    public interface IBacktestingEngine
    {
        Task<BacktestResult> RunBacktestAsync(BacktestConfig config, CancellationToken cancellationToken = default);
        Task<List<Kline>> LoadHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate, KlineInterval interval);
        Task<PerformanceMetrics> CalculatePerformanceMetricsAsync(List<TradeHistory> trades, decimal initialCapital);
    }

    public class BacktestingEngine : IBacktestingEngine
    {
        private readonly ILogger _logger;
        private readonly IBinanceApiService _apiService;

        public BacktestingEngine(IBinanceApiService apiService, ILogger logger = null)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _logger = logger ?? LoggerFactory.CreateLogger<BacktestingEngine>();
        }

        /// <summary>
        /// 运行回测
        /// </summary>
        public async Task<BacktestResult> RunBacktestAsync(BacktestConfig config, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.Info($"开始回测: {config.Symbol} from {config.StartDate:yyyy-MM-dd} to {config.EndDate:yyyy-MM-dd}");

                // 1. 加载历史数据
                var klines = await LoadHistoricalDataAsync(
                    config.Symbol,
                    config.StartDate,
                    config.EndDate,
                    config.Interval
                );

                if (klines.Count == 0)
                {
                    _logger.Warning("没有历史数据");
                    return new BacktestResult
                    {
                        Success = false,
                        ErrorMessage = "无历史数据"
                    };
                }

                _logger.Info($"加载了 {klines.Count} 根K线");

                // 2. 初始化模拟账户
                var account = new SimulatedAccount
                {
                    InitialCapital = config.InitialCapital,
                    CurrentCapital = config.InitialCapital,
                    Positions = new Dictionary<string, decimal>()
                };

                var trades = new List<TradeHistory>();

                // 3. 遍历历史数据，生成交易信号并模拟执行
                for (int i = config.WarmupPeriod; i < klines.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.Info("回测被取消");
                        break;
                    }

                    var currentKline = klines[i];
                    var historicalData = klines.Take(i + 1).ToList();

                    // 生成交易信号（这里需要策略逻辑）
                    var signal = await GenerateSignalAsync(config, historicalData, account);

                    if (signal != null)
                    {
                        // 模拟执行交易
                        var trade = ExecuteSimulatedTrade(signal, currentKline, account);
                        if (trade != null)
                        {
                            trades.Add(trade);
                            _logger.Debug($"回测交易: {trade.Symbol} {trade.Direction} {trade.Quantity} @ {trade.Price}");
                        }
                    }

                    // 更新持仓市值
                    UpdateAccountValue(account, currentKline.Close);
                }

                // 4. 计算性能指标
                var performance = await CalculatePerformanceMetricsAsync(trades, config.InitialCapital);

                _logger.Info($"回测完成: 总收益率={performance.TotalReturnPercent:F2}%, 胜率={performance.WinRate:F2}%");

                return new BacktestResult
                {
                    Success = true,
                    Trades = trades,
                    Performance = performance,
                    FinalCapital = account.CurrentCapital,
                    DataPoints = klines.Count,
                    StartDate = config.StartDate,
                    EndDate = config.EndDate
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"回测失败: {ex.Message}", ex);
                return new BacktestResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 加载历史数据
        /// </summary>
        public async Task<List<Kline>> LoadHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate, KlineInterval interval)
        {
            try
            {
                _logger.Debug($"加载历史数据: {symbol} {startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}");

                // TODO: 实现从Binance API获取历史K线数据
                // 需要分批获取（每次最多1000根）并合并
                /*
                var allKlines = new List<Kline>();
                var currentStart = startDate;
                
                while (currentStart < endDate)
                {
                    var klines = await _apiService.GetKlinesAsync(symbol, interval, 1000);
                    allKlines.AddRange(klines.Where(k => k.OpenTime >= startDate && k.CloseTime <= endDate));
                    
                    if (klines.Count < 1000)
                    {
                        break; // 没有更多数据
                    }
                    
                    currentStart = klines.Last().CloseTime;
                }
                
                return allKlines;
                */

                // 临时返回空列表
                return await Task.FromResult(new List<Kline>());
            }
            catch (Exception ex)
            {
                _logger.Error($"加载历史数据失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 计算性能指标
        /// </summary>
        public async Task<PerformanceMetrics> CalculatePerformanceMetricsAsync(List<TradeHistory> trades, decimal initialCapital)
        {
            try
            {
                if (trades.Count == 0)
                {
                    return new PerformanceMetrics
                    {
                        TotalTrades = 0,
                        TotalReturnPercent = 0
                    };
                }

                // 计算总收益
                decimal totalPnl = trades.Sum(t => t.Pnl);
                decimal finalCapital = initialCapital + totalPnl;

                // 胜率
                int winCount = trades.Count(t => t.Pnl > 0);
                decimal winRate = ((decimal)winCount / trades.Count) * 100;

                // 最大回撤
                var (maxDrawdown, maxDrawdownPercent) = CalculateMaxDrawdown(trades, initialCapital);

                // 夏普比率
                decimal sharpeRatio = CalculateSharpeRatio(trades);

                // 盈亏比
                var winTrades = trades.Where(t => t.Pnl > 0).ToList();
                var lossTrades = trades.Where(t => t.Pnl < 0).ToList();
                
                decimal avgWin = winTrades.Any() ? winTrades.Average(t => t.Pnl) : 0;
                decimal avgLoss = lossTrades.Any() ? Math.Abs(lossTrades.Average(t => t.Pnl)) : 0;
                decimal profitFactor = avgLoss > 0 ? avgWin / avgLoss : 0;

                return await Task.FromResult(new PerformanceMetrics
                {
                    TotalTrades = trades.Count,
                    WinningTrades = winCount,
                    LosingTrades = trades.Count - winCount,
                    WinRate = winRate,
                    TotalPnl = totalPnl,
                    TotalReturnPercent = (totalPnl / initialCapital) * 100,
                    MaxDrawdown = maxDrawdown,
                    MaxDrawdownPercent = maxDrawdownPercent,
                    SharpeRatio = sharpeRatio,
                    ProfitFactor = profitFactor,
                    AverageWin = avgWin,
                    AverageLoss = avgLoss,
                    LargestWin = winTrades.Any() ? winTrades.Max(t => t.Pnl) : 0,
                    LargestLoss = lossTrades.Any() ? lossTrades.Min(t => t.Pnl) : 0
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"计算性能指标失败: {ex.Message}", ex);
                throw;
            }
        }

        #region 私有方法

        private async Task<TradeSignal> GenerateSignalAsync(BacktestConfig config, List<Kline> historicalData, SimulatedAccount account)
        {
            // TODO: 实现策略逻辑，生成交易信号
            // 这里需要根据配置的策略类型和参数来生成信号
            // 例如：双均线策略、布林带策略等
            
            // 示例：简单的双均线策略
            /*
            if (historicalData.Count < config.StrategyParams["LongPeriod"])
            {
                return null;
            }

            int shortPeriod = config.StrategyParams["ShortPeriod"];
            int longPeriod = config.StrategyParams["LongPeriod"];

            decimal shortMA = historicalData.TakeLast(shortPeriod).Average(k => k.Close);
            decimal longMA = historicalData.TakeLast(longPeriod).Average(k => k.Close);

            // 金叉买入
            if (shortMA > longMA && !account.Positions.ContainsKey(config.Symbol))
            {
                return new TradeSignal
                {
                    Symbol = config.Symbol,
                    Direction = TradeDirection.Buy,
                    Quantity = (account.CurrentCapital * 0.95m) / historicalData.Last().Close,
                    Price = historicalData.Last().Close,
                    OrderType = SignalOrderType.Market,
                    StrategyName = config.StrategyName
                };
            }
            // 死叉卖出
            else if (shortMA < longMA && account.Positions.ContainsKey(config.Symbol))
            {
                return new TradeSignal
                {
                    Symbol = config.Symbol,
                    Direction = TradeDirection.Sell,
                    Quantity = account.Positions[config.Symbol],
                    Price = historicalData.Last().Close,
                    OrderType = SignalOrderType.Market,
                    StrategyName = config.StrategyName
                };
            }
            */

            return await Task.FromResult<TradeSignal>(null);
        }

        private TradeHistory ExecuteSimulatedTrade(TradeSignal signal, Kline currentKline, SimulatedAccount account)
        {
            try
            {
                decimal executionPrice = signal.OrderType == SignalOrderType.Market 
                    ? currentKline.Close 
                    : signal.Price;

                decimal fee = signal.Quantity * executionPrice * 0.001m; // 0.1% 手续费

                if (signal.Direction == TradeDirection.Buy)
                {
                    decimal cost = signal.Quantity * executionPrice + fee;
                    if (cost > account.CurrentCapital)
                    {
                        return null; // 资金不足
                    }

                    account.CurrentCapital -= cost;
                    account.Positions[signal.Symbol] = account.Positions.GetValueOrDefault(signal.Symbol, 0) + signal.Quantity;
                }
                else // Sell
                {
                    if (!account.Positions.ContainsKey(signal.Symbol) || account.Positions[signal.Symbol] < signal.Quantity)
                    {
                        return null; // 持仓不足
                    }

                    decimal proceeds = signal.Quantity * executionPrice - fee;
                    account.CurrentCapital += proceeds;
                    account.Positions[signal.Symbol] -= signal.Quantity;
                    
                    if (account.Positions[signal.Symbol] == 0)
                    {
                        account.Positions.Remove(signal.Symbol);
                    }
                }

                // 计算盈亏（简化处理）
                decimal pnl = signal.Direction == TradeDirection.Sell 
                    ? signal.Quantity * executionPrice - fee 
                    : -(signal.Quantity * executionPrice + fee);

                return new TradeHistory
                {
                    Timestamp = currentKline.OpenTime,
                    Symbol = signal.Symbol,
                    Direction = signal.Direction,
                    Quantity = signal.Quantity,
                    Price = executionPrice,
                    StrategyName = signal.StrategyName,
                    Fee = fee,
                    Pnl = pnl
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"模拟交易执行失败: {ex.Message}", ex);
                return null;
            }
        }

        private void UpdateAccountValue(SimulatedAccount account, decimal currentPrice)
        {
            // 更新账户总价值（现金 + 持仓市值）
            decimal positionValue = account.Positions.Sum(kvp => kvp.Value * currentPrice);
            account.TotalValue = account.CurrentCapital + positionValue;
        }

        private (decimal Amount, decimal Percent) CalculateMaxDrawdown(List<TradeHistory> trades, decimal initialCapital)
        {
            decimal peak = initialCapital;
            decimal maxDrawdown = 0;
            decimal runningBalance = initialCapital;

            foreach (var trade in trades.OrderBy(t => t.Timestamp))
            {
                runningBalance += trade.Pnl;

                if (runningBalance > peak)
                {
                    peak = runningBalance;
                }

                decimal currentDrawdown = peak - runningBalance;
                if (currentDrawdown > maxDrawdown)
                {
                    maxDrawdown = currentDrawdown;
                }
            }

            decimal drawdownPercent = peak > 0 ? (maxDrawdown / peak) * 100 : 0;
            return (maxDrawdown, drawdownPercent);
        }

        private decimal CalculateSharpeRatio(List<TradeHistory> trades)
        {
            if (trades.Count < 2) return 0;

            var returns = trades.Select(t => t.Pnl).ToList();
            decimal avgReturn = returns.Average();
            decimal stdDev = CalculateStandardDeviation(returns);

            return stdDev > 0 ? avgReturn / stdDev : 0;
        }

        private decimal CalculateStandardDeviation(List<decimal> values)
        {
            if (values.Count < 2) return 0;

            decimal avg = values.Average();
            decimal sumSquares = values.Sum(v => (v - avg) * (v - avg));
            return (decimal)Math.Sqrt((double)(sumSquares / (values.Count - 1)));
        }

        #endregion
    }

    #region 数据模型

    public class BacktestConfig
    {
        public string Symbol { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public KlineInterval Interval { get; set; } = KlineInterval.OneHour;
        public decimal InitialCapital { get; set; } = 10000m;
        public int WarmupPeriod { get; set; } = 20; // 预热期（计算指标所需最小数据点）
        public string StrategyName { get; set; }
        public Dictionary<string, int> StrategyParams { get; set; } = new Dictionary<string, int>();
    }

    public class BacktestResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<TradeHistory> Trades { get; set; } = new List<TradeHistory>();
        public PerformanceMetrics Performance { get; set; }
        public decimal FinalCapital { get; set; }
        public int DataPoints { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class PerformanceMetrics
    {
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public decimal WinRate { get; set; }
        public decimal TotalPnl { get; set; }
        public decimal TotalReturnPercent { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal MaxDrawdownPercent { get; set; }
        public decimal SharpeRatio { get; set; }
        public decimal ProfitFactor { get; set; }
        public decimal AverageWin { get; set; }
        public decimal AverageLoss { get; set; }
        public decimal LargestWin { get; set; }
        public decimal LargestLoss { get; set; }
    }

    public class SimulatedAccount
    {
        public decimal InitialCapital { get; set; }
        public decimal CurrentCapital { get; set; }
        public decimal TotalValue { get; set; }
        public Dictionary<string, decimal> Positions { get; set; } = new Dictionary<string, decimal>();
    }

    #endregion
}
