using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 风险管理服务 - 风险评估、规则引擎、仓位管理
    /// </summary>
    public interface IRiskManagementService
    {
        Task<bool> InitializeAsync(RiskConfig config);
        Task<RiskCheckResult> EvaluateTradeRiskAsync(TradeSignal signal);
        Task<RiskMetrics> GetCurrentRiskMetricsAsync();
        Task RecordTradeAsync(TradeHistory trade);
        Task<List<RiskEvent>> GetRiskEventsAsync(DateTime? startDate = null);
        Task<bool> UpdateRiskConfigAsync(RiskConfig config);
    }

    public class RiskManagementService : IRiskManagementService
    {
        private readonly ILogger _logger;
        private RiskConfig _config;
        private readonly List<TradeHistory> _tradeHistory = new List<TradeHistory>();
        private readonly List<RiskEvent> _riskEvents = new List<RiskEvent>();
        private bool _isInitialized = false;
        private decimal _initialCapital = 0;

        public RiskManagementService(ILogger logger = null)
        {
            _logger = logger ?? LoggerFactory.CreateLogger<RiskManagementService>();
        }

        /// <summary>
        /// 初始化风险管理服务
        /// </summary>
        public async Task<bool> InitializeAsync(RiskConfig config)
        {
            try
            {
                _logger.Info("初始化风险管理服务");
                
                _config = config ?? throw new ArgumentNullException(nameof(config));
                _initialCapital = _config.InitialCapital;

                _isInitialized = true;
                _logger.Info($"风险管理初始化成功 - 初始资金: {_initialCapital}");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.Error($"初始化风险管理失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 评估交易风险
        /// </summary>
        public async Task<RiskCheckResult> EvaluateTradeRiskAsync(TradeSignal signal)
        {
            EnsureInitialized();

            try
            {
                _logger.Debug($"评估交易风险: {signal.Symbol} {signal.Direction} {signal.Quantity}");

                var result = new RiskCheckResult
                {
                    Approved = true,
                    AdjustedQuantity = signal.Quantity
                };

                // 1. 检查单笔交易限额
                decimal tradeValue = signal.Quantity * signal.Price;
                decimal maxTradeValue = _initialCapital * (_config.MaxPositionSizePercent / 100m);
                
                if (tradeValue > maxTradeValue)
                {
                    _logger.Warning($"单笔交易超限: {tradeValue} > {maxTradeValue}");
                    
                    if (_config.AutoAdjustPosition)
                    {
                        // 自动调整数量
                        result.AdjustedQuantity = maxTradeValue / signal.Price;
                        result.Warnings.Add($"交易数量已调整: {signal.Quantity} → {result.AdjustedQuantity}");
                        _logger.Info($"自动调整数量: {result.AdjustedQuantity}");
                    }
                    else
                    {
                        result.Approved = false;
                        result.Reason = $"单笔交易超限 ({tradeValue:F2} > {maxTradeValue:F2})";
                        RecordRiskEvent(RiskEventType.PositionLimit, result.Reason, signal);
                        return result;
                    }
                }

                // 2. 检查最大持仓数
                // TODO: 获取当前持仓数，这里简化处理
                // int currentPositions = await GetCurrentPositionCountAsync();
                // if (currentPositions >= _config.MaxPositions)
                // {
                //     result.Approved = false;
                //     result.Reason = $"持仓数已达上限 ({_config.MaxPositions})";
                //     RecordRiskEvent(RiskEventType.MaxPositionsReached, result.Reason, signal);
                //     return result;
                // }

                // 3. 检查每日亏损限制
                var todayPnl = CalculateTodayPnl();
                decimal maxDailyLoss = _initialCapital * (_config.MaxDailyLossPercent / 100m);
                
                if (Math.Abs(todayPnl) > maxDailyLoss && todayPnl < 0)
                {
                    result.Approved = false;
                    result.Reason = $"今日亏损已达上限 ({Math.Abs(todayPnl):F2} > {maxDailyLoss:F2})";
                    RecordRiskEvent(RiskEventType.DailyLossLimit, result.Reason, signal);
                    _logger.Warning(result.Reason);
                    return result;
                }

                // 4. 检查总回撤限制
                var drawdown = CalculateMaxDrawdown();
                if (drawdown.Percent > _config.MaxDrawdownPercent)
                {
                    result.Approved = false;
                    result.Reason = $"回撤已超限 ({drawdown.Percent:F2}% > {_config.MaxDrawdownPercent}%)";
                    RecordRiskEvent(RiskEventType.DrawdownLimit, result.Reason, signal);
                    _logger.Warning(result.Reason);
                    return result;
                }

                // 5. 检查杠杆倍数（期货）
                if (_config.MaxLeverage > 0 && signal.Metadata.ContainsKey("Leverage"))
                {
                    var leverage = Convert.ToDecimal(signal.Metadata["Leverage"]);
                    if (leverage > _config.MaxLeverage)
                    {
                        result.Approved = false;
                        result.Reason = $"杠杆倍数超限 ({leverage} > {_config.MaxLeverage})";
                        RecordRiskEvent(RiskEventType.LeverageLimit, result.Reason, signal);
                        return result;
                    }
                }

                // 6. 风险评分
                result.RiskScore = CalculateRiskScore(signal, tradeValue);
                
                if (result.RiskScore > 80)
                {
                    result.Warnings.Add($"高风险交易 (风险评分: {result.RiskScore})");
                }

                return await Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.Error($"风险评估失败: {ex.Message}", ex);
                return new RiskCheckResult
                {
                    Approved = false,
                    Reason = $"风险评估异常: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 获取当前风险指标
        /// </summary>
        public async Task<RiskMetrics> GetCurrentRiskMetricsAsync()
        {
            EnsureInitialized();

            try
            {
                _logger.Debug("计算风险指标");

                var todayPnl = CalculateTodayPnl();
                var drawdown = CalculateMaxDrawdown();
                var sharpeRatio = CalculateSharpeRatio();
                var winRate = CalculateWinRate();

                return await Task.FromResult(new RiskMetrics
                {
                    TotalTrades = _tradeHistory.Count,
                    WinRate = winRate,
                    TodayPnl = todayPnl,
                    TodayPnlPercent = _initialCapital > 0 ? (todayPnl / _initialCapital) * 100 : 0,
                    MaxDrawdown = drawdown.Amount,
                    MaxDrawdownPercent = drawdown.Percent,
                    SharpeRatio = sharpeRatio,
                    RiskEvents = _riskEvents.Count(e => e.Timestamp.Date == DateTime.Today),
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"计算风险指标失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 记录交易
        /// </summary>
        public async Task RecordTradeAsync(TradeHistory trade)
        {
            EnsureInitialized();

            try
            {
                _tradeHistory.Add(trade);
                _logger.Debug($"记录交易: {trade.Symbol} {trade.Direction} {trade.Quantity}");
                
                // 检查是否触发风险事件
                await CheckPostTradeRisksAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"记录交易失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取风险事件
        /// </summary>
        public async Task<List<RiskEvent>> GetRiskEventsAsync(DateTime? startDate = null)
        {
            EnsureInitialized();

            try
            {
                var query = _riskEvents.AsEnumerable();

                if (startDate.HasValue)
                {
                    query = query.Where(e => e.Timestamp >= startDate.Value);
                }

                return await Task.FromResult(query.OrderByDescending(e => e.Timestamp).ToList());
            }
            catch (Exception ex)
            {
                _logger.Error($"获取风险事件失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 更新风险配置
        /// </summary>
        public async Task<bool> UpdateRiskConfigAsync(RiskConfig config)
        {
            try
            {
                _logger.Info("更新风险配置");
                _config = config ?? throw new ArgumentNullException(nameof(config));
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.Error($"更新风险配置失败: {ex.Message}", ex);
                return false;
            }
        }

        #region 私有方法

        private decimal CalculateTodayPnl()
        {
            var todayTrades = _tradeHistory.Where(t => t.Timestamp.Date == DateTime.Today);
            return todayTrades.Sum(t => t.Pnl);
        }

        private (decimal Amount, decimal Percent) CalculateMaxDrawdown()
        {
            if (_tradeHistory.Count == 0)
            {
                return (0, 0);
            }

            decimal peak = _initialCapital;
            decimal maxDrawdown = 0;

            decimal runningBalance = _initialCapital;
            foreach (var trade in _tradeHistory.OrderBy(t => t.Timestamp))
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

        private decimal CalculateSharpeRatio()
        {
            if (_tradeHistory.Count < 2)
            {
                return 0;
            }

            // 简化计算：(平均收益 - 无风险利率) / 收益标准差
            var returns = _tradeHistory.Select(t => t.Pnl).ToList();
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

        private decimal CalculateWinRate()
        {
            if (_tradeHistory.Count == 0) return 0;

            int winCount = _tradeHistory.Count(t => t.Pnl > 0);
            return ((decimal)winCount / _tradeHistory.Count) * 100;
        }

        private decimal CalculateRiskScore(TradeSignal signal, decimal tradeValue)
        {
            decimal score = 0;

            // 基于交易规模
            decimal sizeRatio = tradeValue / _initialCapital;
            score += sizeRatio * 40; // 最高40分

            // 基于当前回撤
            var drawdown = CalculateMaxDrawdown();
            score += (drawdown.Percent / _config.MaxDrawdownPercent) * 30; // 最高30分

            // 基于今日亏损
            var todayPnl = CalculateTodayPnl();
            if (todayPnl < 0)
            {
                decimal lossRatio = Math.Abs(todayPnl) / (_initialCapital * (_config.MaxDailyLossPercent / 100m));
                score += lossRatio * 30; // 最高30分
            }

            return Math.Min(100, score);
        }

        private void RecordRiskEvent(RiskEventType eventType, string description, TradeSignal signal)
        {
            var riskEvent = new RiskEvent
            {
                Timestamp = DateTime.Now,
                EventType = eventType,
                Description = description,
                Symbol = signal.Symbol,
                Severity = DetermineSeverity(eventType)
            };

            _riskEvents.Add(riskEvent);
            _logger.Warning($"风险事件: {eventType} - {description}");
        }

        private RiskSeverity DetermineSeverity(RiskEventType eventType)
        {
            switch (eventType)
            {
                case RiskEventType.DrawdownLimit:
                case RiskEventType.DailyLossLimit:
                    return RiskSeverity.High;
                    
                case RiskEventType.PositionLimit:
                case RiskEventType.MaxPositionsReached:
                    return RiskSeverity.Medium;
                    
                default:
                    return RiskSeverity.Low;
            }
        }

        private async Task CheckPostTradeRisksAsync()
        {
            // 交易后风险检查
            var metrics = await GetCurrentRiskMetricsAsync();
            
            if (metrics.MaxDrawdownPercent > _config.MaxDrawdownPercent * 0.9m)
            {
                _logger.Warning($"接近最大回撤限制: {metrics.MaxDrawdownPercent:F2}%");
            }
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("RiskManagementService未初始化，请先调用InitializeAsync");
            }
        }

        #endregion
    }

    #region 数据模型

    public class RiskConfig
    {
        public decimal InitialCapital { get; set; } = 10000m;
        public decimal MaxPositionSizePercent { get; set; } = 10m; // 单笔最大仓位%
        public int MaxPositions { get; set; } = 5; // 最大持仓数
        public decimal MaxDailyLossPercent { get; set; } = 5m; // 每日最大亏损%
        public decimal MaxDrawdownPercent { get; set; } = 20m; // 最大回撤%
        public decimal MaxLeverage { get; set; } = 3m; // 最大杠杆倍数
        public bool AutoAdjustPosition { get; set; } = true; // 自动调整仓位
    }

    public class RiskCheckResult
    {
        public bool Approved { get; set; }
        public string Reason { get; set; }
        public decimal AdjustedQuantity { get; set; }
        public decimal RiskScore { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class RiskMetrics
    {
        public int TotalTrades { get; set; }
        public decimal WinRate { get; set; }
        public decimal TodayPnl { get; set; }
        public decimal TodayPnlPercent { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal MaxDrawdownPercent { get; set; }
        public decimal SharpeRatio { get; set; }
        public int RiskEvents { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class RiskEvent
    {
        public DateTime Timestamp { get; set; }
        public RiskEventType EventType { get; set; }
        public string Description { get; set; }
        public string Symbol { get; set; }
        public RiskSeverity Severity { get; set; }
    }

    public enum RiskEventType
    {
        PositionLimit,
        MaxPositionsReached,
        DailyLossLimit,
        DrawdownLimit,
        LeverageLimit,
        Other
    }

    public enum RiskSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    #endregion
}
