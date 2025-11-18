using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 交易引擎 - 负责订单执行、仓位管理、投资组合跟踪
    /// </summary>
    public interface ITradingEngine
    {
        Task<bool> InitializeAsync(IBinanceApiService apiService, IRiskManagementService riskService);
        Task<ExecutionResult> ExecuteOrderAsync(TradeSignal signal);
        Task<List<Position>> GetCurrentPositionsAsync();
        Task<PortfolioSummary> GetPortfolioSummaryAsync();
        Task<bool> ClosePositionAsync(string symbol, decimal percentage = 100m);
        Task<bool> CloseAllPositionsAsync();
        Task SyncPositionsWithExchangeAsync();
        Task<List<TradeHistory>> GetTradeHistoryAsync(DateTime? startDate = null, DateTime? endDate = null);
    }

    public class TradingEngine : ITradingEngine
    {
        private readonly ILogger _logger;
        private IBinanceApiService _apiService;
        private IRiskManagementService _riskService;
        private readonly List<TradeHistory> _tradeHistory = new List<TradeHistory>();
        private bool _isInitialized = false;

        public TradingEngine(ILogger logger = null)
        {
            _logger = logger ?? LoggerFactory.CreateLogger<TradingEngine>();
        }

        /// <summary>
        /// 初始化交易引擎
        /// </summary>
        public async Task<bool> InitializeAsync(IBinanceApiService apiService, IRiskManagementService riskService)
        {
            try
            {
                _logger.Info("初始化交易引擎");
                
                _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
                _riskService = riskService ?? throw new ArgumentNullException(nameof(riskService));

                // 同步当前持仓
                await SyncPositionsWithExchangeAsync();

                _isInitialized = true;
                _logger.Info("交易引擎初始化成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"初始化交易引擎失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 执行交易信号
        /// </summary>
        public async Task<ExecutionResult> ExecuteOrderAsync(TradeSignal signal)
        {
            EnsureInitialized();

            try
            {
                _logger.Info($"执行交易信号: {signal.Symbol} {signal.Direction} {signal.Quantity}");

                // 1. 风险检查
                var riskCheck = await _riskService.EvaluateTradeRiskAsync(signal);
                if (!riskCheck.Approved)
                {
                    _logger.Warning($"交易被风控拒绝: {riskCheck.Reason}");
                    return new ExecutionResult
                    {
                        Success = false,
                        ErrorMessage = $"风控拒绝: {riskCheck.Reason}"
                    };
                }

                // 2. 计算实际下单数量（考虑风控调整）
                decimal adjustedQuantity = riskCheck.AdjustedQuantity > 0 
                    ? riskCheck.AdjustedQuantity 
                    : signal.Quantity;

                // 3. 执行订单
                OrderResult orderResult;
                if (signal.OrderType == SignalOrderType.Market)
                {
                    orderResult = await _apiService.PlaceMarketOrderAsync(
                        signal.Symbol,
                        signal.Direction == TradeDirection.Buy ? OrderSide.Buy : OrderSide.Sell,
                        adjustedQuantity
                    );
                }
                else
                {
                    orderResult = await _apiService.PlaceLimitOrderAsync(
                        signal.Symbol,
                        signal.Direction == TradeDirection.Buy ? OrderSide.Buy : OrderSide.Sell,
                        adjustedQuantity,
                        signal.Price
                    );
                }

                // 4. 记录交易历史
                if (orderResult.Success)
                {
                    var trade = new TradeHistory
                    {
                        Timestamp = DateTime.Now,
                        Symbol = signal.Symbol,
                        Direction = signal.Direction,
                        Quantity = orderResult.ExecutedQuantity,
                        Price = orderResult.AveragePrice,
                        OrderId = orderResult.OrderId,
                        StrategyName = signal.StrategyName,
                        Fee = CalculateFee(orderResult.ExecutedQuantity, orderResult.AveragePrice)
                    };
                    
                    _tradeHistory.Add(trade);
                    _logger.Info($"交易成功: OrderId={trade.OrderId}, 成交{trade.Quantity} @ {trade.Price}");

                    // 5. 更新风控统计
                    await _riskService.RecordTradeAsync(trade);
                }

                return new ExecutionResult
                {
                    Success = orderResult.Success,
                    OrderId = orderResult.OrderId,
                    ExecutedQuantity = orderResult.ExecutedQuantity,
                    AveragePrice = orderResult.AveragePrice,
                    ErrorMessage = orderResult.ErrorMessage
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"执行交易失败: {ex.Message}", ex);
                return new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 获取当前持仓
        /// </summary>
        public async Task<List<Position>> GetCurrentPositionsAsync()
        {
            EnsureInitialized();

            try
            {
                return await _apiService.GetPositionsAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"获取持仓失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 获取投资组合摘要
        /// </summary>
        public async Task<PortfolioSummary> GetPortfolioSummaryAsync()
        {
            EnsureInitialized();

            try
            {
                _logger.Debug("计算投资组合摘要");

                var balances = await _apiService.GetAccountBalancesAsync();
                var positions = await GetCurrentPositionsAsync();

                // 计算总价值（转换为USDT）
                decimal totalValue = 0;
                foreach (var balance in balances)
                {
                    if (balance.Key == "USDT")
                    {
                        totalValue += balance.Value;
                    }
                    else
                    {
                        // 获取当前价格并转换
                        try
                        {
                            var price = await _apiService.GetCurrentPriceAsync($"{balance.Key}USDT");
                            totalValue += balance.Value * price;
                        }
                        catch
                        {
                            // 无法获取价格的资产跳过
                        }
                    }
                }

                // 计算今日盈亏（需要历史数据）
                var todayTrades = _tradeHistory.Where(t => t.Timestamp.Date == DateTime.Today).ToList();
                decimal todayPnl = todayTrades.Sum(t => 
                    t.Direction == TradeDirection.Sell ? t.Quantity * t.Price : -t.Quantity * t.Price
                );

                return new PortfolioSummary
                {
                    TotalValue = totalValue,
                    AvailableBalance = balances.ContainsKey("USDT") ? balances["USDT"] : 0,
                    PositionCount = positions.Count,
                    TodayPnl = todayPnl,
                    TodayPnlPercent = totalValue > 0 ? (todayPnl / totalValue) * 100 : 0,
                    TotalTrades = _tradeHistory.Count,
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"计算投资组合摘要失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 平仓指定持仓
        /// </summary>
        public async Task<bool> ClosePositionAsync(string symbol, decimal percentage = 100m)
        {
            EnsureInitialized();

            try
            {
                _logger.Info($"平仓: {symbol} {percentage}%");

                var positions = await GetCurrentPositionsAsync();
                var position = positions.FirstOrDefault(p => p.Symbol == symbol || p.Symbol == $"{symbol}USDT");

                if (position == null)
                {
                    _logger.Warning($"未找到持仓: {symbol}");
                    return false;
                }

                decimal quantityToClose = position.Quantity * (percentage / 100m);
                
                var signal = new TradeSignal
                {
                    Symbol = position.Symbol,
                    Direction = TradeDirection.Sell, // 平多仓
                    Quantity = quantityToClose,
                    OrderType = SignalOrderType.Market,
                    StrategyName = "Manual Close"
                };

                var result = await ExecuteOrderAsync(signal);
                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.Error($"平仓失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 平掉所有持仓
        /// </summary>
        public async Task<bool> CloseAllPositionsAsync()
        {
            EnsureInitialized();

            try
            {
                _logger.Info("平掉所有持仓");

                var positions = await GetCurrentPositionsAsync();
                bool allSuccess = true;

                foreach (var position in positions)
                {
                    if (position.Symbol == "USDT") continue; // 跳过USDT

                    var success = await ClosePositionAsync(position.Symbol);
                    if (!success)
                    {
                        allSuccess = false;
                        _logger.Warning($"平仓失败: {position.Symbol}");
                    }
                }

                return allSuccess;
            }
            catch (Exception ex)
            {
                _logger.Error($"平掉所有持仓失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 同步持仓（从交易所获取最新数据）
        /// </summary>
        public async Task SyncPositionsWithExchangeAsync()
        {
            try
            {
                _logger.Debug("同步持仓数据");
                
                var positions = await _apiService.GetPositionsAsync();
                _logger.Info($"同步完成，当前持仓: {positions.Count}个");
            }
            catch (Exception ex)
            {
                _logger.Error($"同步持仓失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取交易历史
        /// </summary>
        public async Task<List<TradeHistory>> GetTradeHistoryAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            EnsureInitialized();

            try
            {
                var query = _tradeHistory.AsEnumerable();

                if (startDate.HasValue)
                {
                    query = query.Where(t => t.Timestamp >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(t => t.Timestamp <= endDate.Value);
                }

                return await Task.FromResult(query.OrderByDescending(t => t.Timestamp).ToList());
            }
            catch (Exception ex)
            {
                _logger.Error($"获取交易历史失败: {ex.Message}", ex);
                throw;
            }
        }

        private decimal CalculateFee(decimal quantity, decimal price)
        {
            // 币安现货手续费默认0.1%
            const decimal feeRate = 0.001m;
            return quantity * price * feeRate;
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("TradingEngine未初始化，请先调用InitializeAsync");
            }
        }
    }

    #region 数据模型

    public enum TradeDirection
    {
        Buy,
        Sell
    }

    public enum SignalOrderType
    {
        Market,
        Limit
    }

    public class TradeSignal
    {
        public string Symbol { get; set; }
        public TradeDirection Direction { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public SignalOrderType OrderType { get; set; }
        public string StrategyName { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class ExecutionResult
    {
        public bool Success { get; set; }
        public long OrderId { get; set; }
        public decimal ExecutedQuantity { get; set; }
        public decimal AveragePrice { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class TradeHistory
    {
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; }
        public TradeDirection Direction { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public long OrderId { get; set; }
        public string StrategyName { get; set; }
        public decimal Fee { get; set; }
        public decimal Pnl { get; set; }
    }

    public class PortfolioSummary
    {
        public decimal TotalValue { get; set; }
        public decimal AvailableBalance { get; set; }
        public int PositionCount { get; set; }
        public decimal TodayPnl { get; set; }
        public decimal TodayPnlPercent { get; set; }
        public int TotalTrades { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}
