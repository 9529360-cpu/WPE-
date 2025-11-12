using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services;

/// <summary>
/// 并行多币种监测 + 串行单订单执行器
/// 
/// 核心逻辑：
/// 1. 并行监测 N 个交易对（默认10个热门币种）
/// 2. AI 生成信号后推送到执行队列
/// 3. 单线程执行器：一次只持有1个订单，完成后再处理下一个
/// 4. 高频剥头皮策略：小额盈利（净收益 > 手续费+滑点）即可平仓
/// 5. 风控：止损、冷却时间、日亏损限制
/// </summary>
public sealed class ParallelScalpingController
{
    private readonly BinanceApiClient _api;
    private readonly DataCacheService _cache;
    private readonly BinanceStreamClient _stream;
    private readonly AIRiskManager _riskManager;
    private readonly TradingAccountManager _accountManager;
    
    private readonly Channel<ScalpingSignal> _signalQueue;
    private readonly Dictionary<string, DateTime> _lastTradeTime = new();
    private readonly Dictionary<string, int> _symbolFailures = new();
    
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private Task? _executorTask;
    
    private Position? _currentPosition;
    private readonly object _positionLock = new();
    
    private bool _isRunning;
    
    // 配置参数
    private readonly string[] _hotSymbols = new[]
    {
        "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "ADAUSDT",
        "DOGEUSDT", "MATICUSDT", "DOTUSDT", "AVAXUSDT", "LINKUSDT"
    };
    
    private readonly TimeSpan _symbolCooldown = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(30);
    private readonly double _minConfidence = 0.70;
    private readonly double _makerFee = 0.0002; // 0.02%
    private readonly double _takerFee = 0.0004; // 0.04%
    private readonly double _baseSlippage = 0.0001; // 0.01%
    private readonly double _minNetProfitUsdt = 0.5; // 最小净收益 0.5 USDT
    
    public bool IsRunning => _isRunning;
    
    public ParallelScalpingController()
    {
        _api = ServiceLocator.Api;
        _cache = ServiceLocator.Cache;
        _stream = ServiceLocator.Stream;
        _riskManager = new AIRiskManager();
        _accountManager = new TradingAccountManager(_cache);
        
        // 创建信号队列（无界，但会限流）
        _signalQueue = Channel.CreateUnbounded<ScalpingSignal>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }
    
    /// <summary>
    /// 启动并行监测 + 串行执行
    /// </summary>
    public async Task StartAsync(AccountType accountType = AccountType.Simulated)
    {
        if (_isRunning)
        {
            return;
        }
        
        // 验证 API 配置
        ValidateApiConfig();
        
        // 初始化账户
        _accountManager.SwitchAccount(accountType);
        if (_accountManager.ActiveAccount == null)
        {
            _accountManager.CreateSimulatedAccount("高频剥头皮账户", 10_000m);
            _accountManager.SwitchAccount(AccountType.Simulated);
        }
        
        _cts = new CancellationTokenSource();
        _isRunning = true;
        
        // 启动监测任务（并行）
        _monitorTask = Task.Run(() => MonitorAllSymbolsAsync(_cts.Token), _cts.Token);
        
        // 启动执行任务（串行）
        _executorTask = Task.Run(() => ExecuteSerialOrdersAsync(_cts.Token), _cts.Token);
        
        LogService.Info("[ParallelScalping] 高频剥头皮交易已启动");
        LogService.Info($"[ParallelScalping] 监测币种: {string.Join(", ", _hotSymbols)}");
        LogService.Info($"[ParallelScalping] 最小净收益: {_minNetProfitUsdt} USDT");
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// 停止交易
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }
        
        _isRunning = false;
        _cts?.Cancel();
        
        // 平掉当前持仓
        lock (_positionLock)
        {
            if (_currentPosition != null)
            {
                LogService.Warning("[ParallelScalping] 停止交易，强制平仓当前持仓");
                _ = ForceClosePositionAsync(_currentPosition);
            }
        }
        
        // 等待任务完成
        if (_monitorTask != null)
        {
            await _monitorTask;
        }
        if (_executorTask != null)
        {
            await _executorTask;
        }
        
        _cts?.Dispose();
        _cts = null;
        
        LogService.Info("[ParallelScalping] 高频剥头皮交易已停止");
    }
    
    /// <summary>
    /// 并行监测所有交易对
    /// </summary>
    private async Task MonitorAllSymbolsAsync(CancellationToken ct)
    {
        var tasks = _hotSymbols.Select(symbol => MonitorSingleSymbolAsync(symbol, ct)).ToArray();
        
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            LogService.Info("[ParallelScalping] 监测任务已取消");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[ParallelScalping] 监测任务异常");
        }
    }
    
    /// <summary>
    /// 监测单个交易对（独立任务）
    /// </summary>
    private async Task MonitorSingleSymbolAsync(string symbol, CancellationToken ct)
    {
        LogService.Info($"[ParallelScalping] 开始监测: {symbol}");
        
        string deepSeekKey = ConfigurationService.GetDeepSeekApiKey();
        var aiAgent = new DeepSeekTradingAgent(deepSeekKey);
        var dataProcessor = new MarketDataPreprocessor(_api, _cache);
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 检查冷却时间
                if (!CanTradeSymbol(symbol))
                {
                    await Task.Delay(_monitorInterval, ct);
                    continue;
                }
                
                // 获取市场数据
                MarketDataSnapshot marketData = await dataProcessor.CollectMarketDataAsync(symbol, ct);
                
                // AI 分析
                AITradingSignal aiSignal = await aiAgent.AnalyzeMarketSituationAsync(marketData, ct);
                
                // 验证信号
                if (aiSignal.Confidence >= _minConfidence && aiSignal.Action != SignalAction.Hold)
                {
                    var scalpingSignal = new ScalpingSignal
                    {
                        Symbol = symbol,
                        Action = aiSignal.Action == SignalAction.Buy ? "Buy" : "Sell",
                        Confidence = aiSignal.Confidence,
                        Reason = aiSignal.Reason,
                        Timestamp = DateTime.UtcNow
                    };
                    
                    // 推送到信号队列
                    await _signalQueue.Writer.WriteAsync(scalpingSignal, ct);
                    
                    LogService.Info($"[ParallelScalping] 生成信号: {symbol} {aiSignal.Action} (信心度: {aiSignal.Confidence:P0})");
                }
                
                await Task.Delay(_monitorInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, $"[ParallelScalping] 监测 {symbol} 异常");
                
                // 记录失败次数
                _symbolFailures[symbol] = _symbolFailures.GetValueOrDefault(symbol, 0) + 1;
                
                // 如果连续失败3次，暂停该交易对10分钟
                if (_symbolFailures[symbol] >= 3)
                {
                    _lastTradeTime[symbol] = DateTime.UtcNow.AddMinutes(10);
                    _symbolFailures[symbol] = 0;
                    LogService.Warning($"[ParallelScalping] {symbol} 连续失败，暂停10分钟");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
        
        LogService.Info($"[ParallelScalping] 停止监测: {symbol}");
    }
    
    /// <summary>
    /// 串行执行订单（单线程，一次只持有1个订单）
    /// </summary>
    private async Task ExecuteSerialOrdersAsync(CancellationToken ct)
    {
        LogService.Info("[ParallelScalping] 执行器已启动（串行模式）");
        
        await foreach (var signal in _signalQueue.Reader.ReadAllAsync(ct))
        {
            try
            {
                // 检查是否已有持仓
                lock (_positionLock)
                {
                    if (_currentPosition != null)
                    {
                        LogService.Debug($"[ParallelScalping] 跳过信号 {signal.Symbol}：当前持仓未平仓");
                        continue;
                    }
                }
                
                // 检查冷却时间
                if (!CanTradeSymbol(signal.Symbol))
                {
                    LogService.Debug($"[ParallelScalping] 跳过信号 {signal.Symbol}：冷却期内");
                    continue;
                }
                
                // 检查风控
                var account = _accountManager.ActiveAccount;
                if (account == null)
                {
                    LogService.Error("[ParallelScalping] 活动账户为空");
                    continue;
                }
                
                // 检查日亏损限制
                if (!CheckDailyLossLimit(account))
                {
                    LogService.Warning("[ParallelScalping] 触发日亏损限制，停止交易");
                    await StopAsync();
                    break;
                }
                
                // 执行开仓
                await ExecuteOpenPositionAsync(signal, account, ct);
                
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, $"[ParallelScalping] 执行信号异常: {signal.Symbol}");
            }
        }
        
        LogService.Info("[ParallelScalping] 执行器已停止");
    }
    
    /// <summary>
    /// 执行开仓 + 监控平仓（阻塞直到完成）
    /// </summary>
    private async Task ExecuteOpenPositionAsync(ScalpingSignal signal, TradingAccount account, CancellationToken ct)
    {
        try
        {
            // 计算仓位大小（固定 50 USDT 名义本金）
            decimal entryPrice = await GetCurrentPriceAsync(signal.Symbol);
            decimal quantity = CalculateQuantity(signal.Symbol, 50m, entryPrice);
            
            LogService.Info($"[ParallelScalping] 开仓: {signal.Symbol} {signal.Action} 数量: {quantity} 价格: {entryPrice}");
            
            // 确定方向
            OrderSide side = signal.Action == "Buy" ? OrderSide.Buy : OrderSide.Sell;
            
            // 模拟/实盘下单
            var position = new Position
            {
                Symbol = signal.Symbol,
                Side = side,
                Quantity = (double)quantity,
                EntryPrice = (double)entryPrice,
                CurrentPrice = (double)entryPrice,
                Status = PositionStatus.Open,
                OpenTime = DateTime.UtcNow
            };
            
            // 计算成本（手续费 + 滑点）
            double entryCost = CalculateEntryCost((double)entryPrice, (double)quantity);
            position.UnrealizedPnL = -entryCost; // 初始为负（成本）
            
            lock (_positionLock)
            {
                _currentPosition = position;
            }
            
            // 记录交易时间
            _lastTradeTime[signal.Symbol] = DateTime.UtcNow;
            
            // 监控持仓直到平仓
            await MonitorPositionUntilCloseAsync(position, ct);
            
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"[ParallelScalping] 开仓失败: {signal.Symbol}");
        }
    }
    
    /// <summary>
    /// 监控持仓直到平仓（高频检查，每秒1次）
    /// </summary>
    private async Task MonitorPositionUntilCloseAsync(Position position, CancellationToken ct)
    {
        LogService.Info($"[ParallelScalping] 开始监控持仓: {position.Symbol}");
        
        var startTime = DateTime.UtcNow;
        var maxHoldTime = TimeSpan.FromMinutes(30); // 最大持仓30分钟
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 获取当前价格
                decimal currentPrice = await GetCurrentPriceAsync(position.Symbol);
                position.CurrentPrice = (double)currentPrice;
                
                // 计算盈亏（净收益 = 价差 - 入场成本 - 出场成本）
                double priceDiff = (position.CurrentPrice - position.EntryPrice) * position.Quantity;
                if (position.Side == OrderSide.Sell)
                {
                    priceDiff = -priceDiff; // 做空方向相反
                }
                
                double exitCost = CalculateExitCost(position.CurrentPrice, position.Quantity);
                double netProfit = priceDiff - Math.Abs(position.UnrealizedPnL) - exitCost;
                
                position.UnrealizedPnL = netProfit;
                
                LogService.Debug($"[ParallelScalping] 持仓监控: {position.Symbol} 价格: {currentPrice:F2} 净收益: {netProfit:F4} USDT");
                
                // 平仓条件1：达到最小净收益
                if (netProfit >= _minNetProfitUsdt)
                {
                    LogService.Info($"[ParallelScalping] 触发止盈: {position.Symbol} 净收益: {netProfit:F4} USDT");
                    await ClosePositionAsync(position, "止盈");
                    break;
                }
                
                // 平仓条件2：止损（亏损超过1 USDT）
                if (netProfit <= -1.0)
                {
                    LogService.Warning($"[ParallelScalping] 触发止损: {position.Symbol} 净亏损: {netProfit:F4} USDT");
                    await ClosePositionAsync(position, "止损");
                    break;
                }
                
                // 平仓条件3：超时（持仓超过30分钟）
                if (DateTime.UtcNow - startTime >= maxHoldTime)
                {
                    LogService.Warning($"[ParallelScalping] 持仓超时: {position.Symbol} 强制平仓");
                    await ClosePositionAsync(position, "超时");
                    break;
                }
                
                await Task.Delay(TimeSpan.FromSeconds(1), ct); // 高频检查（1秒1次）
            }
            catch (OperationCanceledException)
            {
                await ClosePositionAsync(position, "系统停止");
                break;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, $"[ParallelScalping] 监控持仓异常: {position.Symbol}");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
    
    /// <summary>
    /// 平仓
    /// </summary>
    private async Task ClosePositionAsync(Position position, string reason)
    {
        try
        {
            position.Status = PositionStatus.Closed;
            position.ClosePrice = position.CurrentPrice;
            position.CloseTime = DateTime.UtcNow;
            position.RealizedPnL = position.UnrealizedPnL;
            
            // 更新账户（模拟）
            var account = _accountManager.ActiveAccount;
            if (account != null)
            {
                account.TotalTrades++;
                if (position.RealizedPnL >= 0)
                {
                    account.WinningTrades++;
                }
                account.TotalPnL += (decimal)position.RealizedPnL;
                
                LogService.Info($"[ParallelScalping] 平仓完成: {position.Symbol} {reason} 净收益: {position.RealizedPnL:F4} USDT");
                LogService.Info($"[ParallelScalping] 账户统计: 总交易: {account.TotalTrades} 胜率: {account.WinRate:P0} 总盈亏: {account.TotalPnL:F2} USDT");
            }
            
            lock (_positionLock)
            {
                _currentPosition = null;
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"[ParallelScalping] 平仓失败: {position.Symbol}");
        }
    }
    
    /// <summary>
    /// 强制平仓（系统停止时）
    /// </summary>
    private async Task ForceClosePositionAsync(Position position)
    {
        await ClosePositionAsync(position, "强制平仓");
    }
    
    /// <summary>
    /// 检查是否可以交易该交易对（冷却时间）
    /// </summary>
    private bool CanTradeSymbol(string symbol)
    {
        if (_lastTradeTime.TryGetValue(symbol, out DateTime lastTime))
        {
            return DateTime.UtcNow - lastTime >= _symbolCooldown;
        }
        return true;
    }
    
    /// <summary>
    /// 检查日亏损限制
    /// </summary>
    private bool CheckDailyLossLimit(TradingAccount account)
    {
        double dailyLossLimit = (double)account.NetValue * 0.05; // 5%
        return account.TodayPnL > -(decimal)dailyLossLimit;
    }
    
    /// <summary>
    /// 获取当前价格（从缓存或 API）
    /// </summary>
    private async Task<decimal> GetCurrentPriceAsync(string symbol)
    {
        try
        {
            var tickers = await _api.GetMiniTickersAsync(new[] { symbol });
            var ticker = tickers.FirstOrDefault();
            return ticker != null ? (decimal)ticker.LastPrice : 0m;
        }
        catch
        {
            // 回退：返回默认值
            return 0m;
        }
    }
    
    /// <summary>
    /// 计算仓位数量
    /// </summary>
    private decimal CalculateQuantity(string symbol, decimal nominalValue, decimal price)
    {
        // 固定名义本金（如 50 USDT）
        decimal rawQuantity = nominalValue / price;
        
        // 根据交易对精度调整（简化：保留3位小数）
        return Math.Round(rawQuantity, 3);
    }
    
    /// <summary>
    /// 计算入场成本（手续费 + 滑点）
    /// </summary>
    private double CalculateEntryCost(double entryPrice, double quantity)
    {
        double nominalValue = entryPrice * quantity;
        double fee = nominalValue * _takerFee; // 假设使用 Taker 费率
        double slippage = nominalValue * _baseSlippage;
        return fee + slippage;
    }
    
    /// <summary>
    /// 计算出场成本（手续费 + 滑点）
    /// </summary>
    private double CalculateExitCost(double exitPrice, double quantity)
    {
        double nominalValue = exitPrice * quantity;
        double fee = nominalValue * _takerFee;
        double slippage = nominalValue * _baseSlippage;
        return fee + slippage;
    }
    
    /// <summary>
    /// 验证 API 配置
    /// </summary>
    private void ValidateApiConfig()
    {
        var (binanceKey, binanceSecret) = ConfigurationService.GetBinanceCredentials();
        string deepSeekKey = ConfigurationService.GetDeepSeekApiKey();
        
        if (string.IsNullOrWhiteSpace(binanceKey) || string.IsNullOrWhiteSpace(binanceSecret))
        {
            throw new InvalidOperationException("Binance API 未配置");
        }
        
        if (string.IsNullOrWhiteSpace(deepSeekKey))
        {
            throw new InvalidOperationException("DeepSeek API 未配置");
        }
        
        _api.SetApiCredentials(binanceKey, binanceSecret);
    }
}

/// <summary>
/// 剥头皮交易信号
/// </summary>
public class ScalpingSignal
{
    public string Symbol { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
