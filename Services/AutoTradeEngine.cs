using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

public class AutoTradeEngine : IAsyncDisposable
{
    private readonly BinanceApiClient _apiClient;
    private readonly AiForecastService _aiService;
    private readonly RiskEngine _riskEngine;
    private readonly NotificationService _notificationService;
    private readonly AppSettings _settings;

    private readonly Dictionary<string, RunningStrategy> _strategies = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();

    public AutoTradeEngine(
        BinanceApiClient apiClient,
        AiForecastService aiService,
        RiskEngine riskEngine,
        NotificationService notificationService,
        AppSettings settings)
    {
        _apiClient = apiClient;
        _aiService = aiService;
        _riskEngine = riskEngine;
        _notificationService = notificationService;
        _settings = settings;
    }

    public event Action<AutoTradeStatus>? StatusChanged;

    public async Task StartStrategyAsync(StrategyConfig config, CancellationToken cancellationToken = default)
    {
        if (!config.EnableAutoTrade)
            throw new InvalidOperationException("策略未开启自动交易，请先勾选 \"启用自动交易\"。");
        if (string.IsNullOrWhiteSpace(config.StrategyName))
            throw new InvalidOperationException("策略名称不能为空");
        if (string.IsNullOrWhiteSpace(config.Symbol))
            throw new InvalidOperationException("交易对不能为空");

        var key = BuildKey(config.StrategyName, config.Symbol);
        RunningStrategy strategy;
        lock (_syncRoot)
        {
            if (_strategies.ContainsKey(key))
                throw new InvalidOperationException($"策略 {config.StrategyName} ({config.Symbol}) 已在运行中");
            strategy = new RunningStrategy(CloneConfig(config), cancellationToken);
            _strategies[key] = strategy;
        }

        UpdateStatus(strategy, AutoTradeState.Starting, TradeSignal.None, "策略正在初始化");
        strategy.Runner = Task.Run(() => RunStrategyAsync(strategy, strategy.Cancellation.Token), CancellationToken.None);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopStrategyAsync(string strategyName, string symbol, bool flattenPosition = false, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(strategyName, symbol);
        RunningStrategy? strategy;
        lock (_syncRoot)
        {
            if (!_strategies.TryGetValue(key, out strategy))
                throw new InvalidOperationException($"未找到正在运行的策略 {strategyName} ({symbol})");
        }

        strategy.Cancellation.Cancel();
        if (strategy.Runner is not null)
        {
            try
            {
                await strategy.Runner.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        if (flattenPosition)
        {
            try
            {
                await FlattenPositionAsync(strategy.Config.Symbol, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UpdateStatus(strategy, AutoTradeState.Error, TradeSignal.Exit, $"平仓失败：{ex.Message}");
            }
        }

        lock (_syncRoot)
        {
            _strategies.Remove(key);
        }

        UpdateStatus(strategy, AutoTradeState.Stopped, TradeSignal.None, "策略已停止");
        strategy.Dispose();
    }

    public IReadOnlyList<AutoTradeStatus> GetStatuses()
    {
        lock (_syncRoot)
        {
            return _strategies.Values.Select(s => s.Status).ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<RunningStrategy> snapshot;
        lock (_syncRoot)
        {
            snapshot = _strategies.Values.ToList();
            _strategies.Clear();
        }

        foreach (var strategy in snapshot)
            strategy.Cancellation.Cancel();

        foreach (var strategy in snapshot)
        {
            try
            {
                if (strategy.Runner is not null)
                    await strategy.Runner.ConfigureAwait(false);
            }
            catch
            {
                // ignore background errors during shutdown
            }
            strategy.Dispose();
        }
    }

    private async Task RunStrategyAsync(RunningStrategy strategy, CancellationToken cancellationToken)
    {
        var config = strategy.Config;
        var pollInterval = ResolveInterval(config.Timeframe, _settings.RefreshIntervalSeconds);
        var riskRules = BuildRiskRules(config).ToArray();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UpdateStatus(strategy, AutoTradeState.Evaluating, strategy.Status.Signal, "正在评估行情信号...");

                var forecast = await _aiService.ForecastAsync(new ForecastRequest
                {
                    Symbol = config.Symbol,
                    Interval = config.Timeframe,
                    HistoryPoints = Math.Max(200, Math.Max(GetParameter(config, "ma_fast", 21), GetParameter(config, "ma_slow", 55)) * 5),
                    Horizon = 12
                }, cancellationToken).ConfigureAwait(false);

                if (forecast.Historical.Count == 0)
                {
                    UpdateStatus(strategy, AutoTradeState.Error, TradeSignal.None, "无法获取历史价格用于预测");
                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var currentPrice = (decimal)forecast.Historical.Last();
                if (currentPrice <= 0)
                {
                    UpdateStatus(strategy, AutoTradeState.Error, TradeSignal.None, "获取的行情价格无效");
                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var fastPeriod = GetParameter(config, "ma_fast", 21);
                var slowPeriod = GetParameter(config, "ma_slow", 55);
                var fastMa = MovingAverage(forecast.Historical, fastPeriod);
                var slowMa = MovingAverage(forecast.Historical, slowPeriod);
                var delta = forecast.Predicted.LastOrDefault() - forecast.Historical.Last();
                var deltaPct = forecast.Historical.Last() == 0 ? 0 : delta / forecast.Historical.Last();

                var positions = await _apiClient.GetPositionsAsync(cancellationToken).ConfigureAwait(false);
                var position = positions.FirstOrDefault(p => string.Equals(p.Symbol, config.Symbol, StringComparison.OrdinalIgnoreCase));
                var currentQty = position?.PositionAmt ?? 0m;

                var balances = await _apiClient.GetAccountBalancesAsync(cancellationToken).ConfigureAwait(false);
                var equity = balances.Sum(b => b.MarginBalance);

                var riskReport = await _riskEngine.GenerateReportAsync(positions, riskRules, config.Symbol, equity).ConfigureAwait(false);
                if (riskReport.BreachedRules.Any())
                {
                    var breach = string.Join("/", riskReport.BreachedRules.Select(r => r.Name));
                    UpdateStatus(strategy, AutoTradeState.RiskOff, TradeSignal.Exit, $"风险规则触发：{breach}");
                    if (currentQty != 0)
                    {
                        await ExecuteOrderAsync(strategy, TradeSignal.Exit, currentQty, currentPrice, cancellationToken).ConfigureAwait(false);
                        await NotifyAsync($"[{config.StrategyName}] 风险限制触发（{breach}），已尝试平仓", cancellationToken).ConfigureAwait(false);
                    }

                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var signal = DetermineSignal(deltaPct, fastMa, slowMa, currentQty, config);
                if (signal == TradeSignal.None)
                {
                    UpdateStatus(strategy, AutoTradeState.Idle, TradeSignal.None, $"无入场信号 (Δ={deltaPct:P2})", targetPosition: currentQty, filledPosition: currentQty);
                    await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ExecuteOrderAsync(strategy, signal, currentQty, currentPrice, cancellationToken).ConfigureAwait(false);
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                UpdateStatus(strategy, AutoTradeState.Error, strategy.Status.Signal, $"网络请求失败：{ex.Message}");
                await NotifyAsync($"[{strategy.Config.StrategyName}] 网络请求异常：{ex.Message}", cancellationToken).ConfigureAwait(false);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (TaskCanceledException ex)
            {
                UpdateStatus(strategy, AutoTradeState.Error, strategy.Status.Signal, $"请求超时：{ex.Message}");
                await NotifyAsync($"[{strategy.Config.StrategyName}] 请求超时：{ex.Message}", cancellationToken).ConfigureAwait(false);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (InvalidOperationException ex)
            {
                UpdateStatus(strategy, AutoTradeState.Error, strategy.Status.Signal, $"操作无效：{ex.Message}");
                await NotifyAsync($"[{strategy.Config.StrategyName}] 配置或数据异常：{ex.Message}", cancellationToken).ConfigureAwait(false);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (JsonException ex)
            {
                UpdateStatus(strategy, AutoTradeState.Error, strategy.Status.Signal, $"数据解析失败：{ex.Message}");
                await NotifyAsync($"[{strategy.Config.StrategyName}] JSON解析异常：{ex.Message}", cancellationToken).ConfigureAwait(false);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        UpdateStatus(strategy, AutoTradeState.Stopped, TradeSignal.None, "策略已停止");
    }

    private async Task ExecuteOrderAsync(RunningStrategy strategy, TradeSignal signal, decimal currentQty, decimal currentPrice, CancellationToken cancellationToken)
    {
        var config = strategy.Config;
        var targetQty = signal switch
        {
            TradeSignal.Long => ComputeTargetQuantity(config, currentPrice),
            TradeSignal.Short => -ComputeTargetQuantity(config, currentPrice),
            TradeSignal.Exit => 0m,
            _ => currentQty
        };

        var delta = targetQty - currentQty;
        if (Math.Abs(delta) < 0.0001m)
        {
            UpdateStatus(strategy, AutoTradeState.Idle, signal, "仓位已满足目标，无需交易", targetQty, currentQty);
            return;
        }

        var side = delta > 0 ? OrderSide.Buy : OrderSide.Sell;
        var quantity = Math.Abs(delta);
        if (quantity < 0.001m)
            quantity = 0.001m;

        UpdateStatus(strategy, AutoTradeState.Executing, signal, $"执行市价单 {side} {quantity}", targetQty, currentQty);

        var order = await _apiClient.PlaceOrderAsync(new OrderRequest
        {
            Symbol = config.Symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = quantity
        }, cancellationToken).ConfigureAwait(false);

        var refreshedPositions = await _apiClient.GetPositionsAsync(cancellationToken).ConfigureAwait(false);
        var position = refreshedPositions.FirstOrDefault(p => string.Equals(p.Symbol, config.Symbol, StringComparison.OrdinalIgnoreCase));
        var filled = position?.PositionAmt ?? (side == OrderSide.Buy ? currentQty + order.ExecutedQuantity : currentQty - order.ExecutedQuantity);

        UpdateStatus(strategy, AutoTradeState.Idle, signal, $"订单 {order.OrderId} 执行完成，现持仓 {filled}", targetQty, filled, order.OrderId);
        await NotifyAsync($"[{config.StrategyName}] {side} {quantity} @ 市价，订单 {order.OrderId}", cancellationToken).ConfigureAwait(false);
    }

    private async Task FlattenPositionAsync(string symbol, CancellationToken cancellationToken)
    {
        var positions = await _apiClient.GetPositionsAsync(cancellationToken).ConfigureAwait(false);
        var position = positions.FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (position is null || position.PositionAmt == 0)
            return;

        var side = position.PositionAmt > 0 ? OrderSide.Sell : OrderSide.Buy;
        await _apiClient.PlaceOrderAsync(new OrderRequest
        {
            Symbol = symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = Math.Abs(position.PositionAmt)
        }, cancellationToken).ConfigureAwait(false);
    }

    private void UpdateStatus(RunningStrategy strategy, AutoTradeState state, TradeSignal signal, string message, decimal? targetPosition = null, decimal? filledPosition = null, long? orderId = null)
    {
        var status = strategy.Status with
        {
            State = state,
            Signal = signal,
            Message = message,
            TargetPosition = targetPosition ?? strategy.Status.TargetPosition,
            FilledPosition = filledPosition ?? strategy.Status.FilledPosition,
            LastOrderId = orderId,
            UpdatedAt = DateTime.UtcNow
        };
        strategy.Status = status;
        StatusChanged?.Invoke(status);
    }

    private static TradeSignal DetermineSignal(double deltaPct, double fastMa, double slowMa, decimal currentQty, StrategyConfig config)
    {
        var entryThreshold = config.TakeProfitPercent <= 0 ? 0.005 : config.TakeProfitPercent / 100.0;
        var exitThreshold = config.StopLossPercent <= 0 ? 0.002 : config.StopLossPercent / 100.0;
        var bullish = fastMa >= slowMa;
        var bearish = fastMa <= slowMa;

        if (deltaPct >= entryThreshold && bullish)
            return TradeSignal.Long;
        if (deltaPct <= -entryThreshold && bearish)
            return TradeSignal.Short;
        if (currentQty != 0 && Math.Abs(deltaPct) <= exitThreshold)
            return TradeSignal.Exit;

        return TradeSignal.None;
    }

    private static decimal ComputeTargetQuantity(StrategyConfig config, decimal currentPrice)
    {
        if (currentPrice <= 0)
            return 0m;

        var notional = (decimal)config.Capital * config.Leverage;
        var quantity = notional / currentPrice;
        if (config.MaxPositions > 0)
            quantity = Math.Min(quantity, config.MaxPositions);
        return Math.Round(quantity, 3, MidpointRounding.AwayFromZero);
    }

    private static int GetParameter(StrategyConfig config, string name, int fallback)
    {
        var parameter = config.Parameters.FirstOrDefault(p => string.Equals(p.Parameter, name, StringComparison.OrdinalIgnoreCase));
        if (parameter is not null && int.TryParse(parameter.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            return value;
        return fallback;
    }

    private static double MovingAverage(IReadOnlyList<double> source, int period)
    {
        if (source.Count == 0)
            return 0;
        if (period <= 0)
            return source.Last();

        var length = Math.Min(period, source.Count);
        var sum = 0d;
        for (int i = source.Count - length; i < source.Count; i++)
            sum += source[i];
        return sum / length;
    }

    private static TimeSpan ResolveInterval(string timeframe, int fallbackSeconds)
    {
        var seconds = ParseTimeframeSeconds(timeframe);
        if (seconds <= 0)
            seconds = fallbackSeconds;
        seconds = Math.Max(10, seconds / 3);
        return TimeSpan.FromSeconds(seconds);
    }

    private static int ParseTimeframeSeconds(string timeframe)
    {
        if (string.IsNullOrWhiteSpace(timeframe))
            return 0;

        var unit = timeframe[^1];
        if (!int.TryParse(timeframe[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return 0;

        return unit switch
        {
            'm' or 'M' => value * 60,
            'h' or 'H' => value * 60 * 60,
            'd' or 'D' => value * 24 * 60 * 60,
            _ => 0
        };
    }

    private static string BuildKey(string strategyName, string symbol) =>
        $"{strategyName.Trim().ToUpperInvariant()}::{symbol.Trim().ToUpperInvariant()}";

    private static StrategyConfig CloneConfig(StrategyConfig config)
    {
        return new StrategyConfig
        {
            StrategyName = config.StrategyName,
            Symbol = config.Symbol.ToUpperInvariant(),
            Timeframe = config.Timeframe,
            Capital = config.Capital,
            Leverage = config.Leverage,
            MaxPositions = config.MaxPositions,
            StopLossPercent = config.StopLossPercent,
            TakeProfitPercent = config.TakeProfitPercent,
            EnableAutoTrade = config.EnableAutoTrade,
            EnableHedgeMode = config.EnableHedgeMode,
            ExecutionVenue = config.ExecutionVenue,
            Parameters = config.Parameters.Select(p => new StrategyParameterRow
            {
                Parameter = p.Parameter,
                Value = p.Value,
                Description = p.Description
            }).ToList()
        };
    }

    private IEnumerable<RiskRule> BuildRiskRules(StrategyConfig config)
    {
        var varThreshold = (decimal)config.Capital * 0.1m;
        var leverageThreshold = Math.Max(1, config.Leverage * 2);
        return new[]
        {
            new RiskRule { Name = "VaR 控制", Threshold = varThreshold, Comparator = ">", Action = "降低仓位", IsActive = true },
            new RiskRule { Name = "杠杆上限", Threshold = leverageThreshold, Comparator = "LEV>", Action = "降低杠杆", IsActive = true }
        };
    }

    private async Task NotifyAsync(string message, CancellationToken cancellationToken)
    {
        if (!_settings.EnableNotifications)
            return;

        var tasks = new List<Task>();
        if (!string.IsNullOrWhiteSpace(_settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(_settings.TelegramChatId))
        {
            tasks.Add(_notificationService.SendTelegramAsync(_settings.TelegramBotToken, _settings.TelegramChatId, message, cancellationToken));
        }
        if (!string.IsNullOrWhiteSpace(_settings.DingTalkWebhook))
        {
            tasks.Add(_notificationService.SendDingTalkAsync(_settings.DingTalkWebhook, message, cancellationToken));
        }

        foreach (var task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // ignore notification errors
            }
        }
    }

    private sealed class RunningStrategy : IDisposable
    {
        public RunningStrategy(StrategyConfig config, CancellationToken externalToken)
        {
            Config = config;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            Status = new AutoTradeStatus
            {
                StrategyName = config.StrategyName,
                Symbol = config.Symbol,
                State = AutoTradeState.Starting,
                Signal = TradeSignal.None,
                Message = "等待启动",
                TargetPosition = 0,
                FilledPosition = 0,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public StrategyConfig Config { get; }
        public CancellationTokenSource Cancellation { get; }
        public Task? Runner { get; set; }
        public AutoTradeStatus Status { get; set; }

        public void Dispose()
        {
            Cancellation.Dispose();
        }
    }
}
