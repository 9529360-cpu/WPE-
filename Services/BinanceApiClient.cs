using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

public class BinanceApiClient : IDisposable
{
    private const string RestEndpoint = "https://fapi.binance.com";
    private static readonly TimeSpan[] RetryDelays = new[]
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    };

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    // 🆕 健壮性组件
    private readonly RateLimiter _rateLimiter = new();
    private readonly ApiCircuitBreaker _circuitBreaker = new(failureThreshold: 5, cooldown: TimeSpan.FromMinutes(1));
    private readonly ApiHealthMonitor _healthMonitor = new();

    private string? _apiKey;
    private byte[]? _secretBytes;

    public BinanceApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(RestEndpoint),
            Timeout = TimeSpan.FromSeconds(10) // 🆕 默认10秒超时
        };
    }

    // 🆕 公开健康状态
    public ApiHealthReport GetHealthReport(TimeSpan? window = null) => _healthMonitor.GetHealthReport(window);
    public CircuitBreakerState CircuitBreakerState => _circuitBreaker.State;
    public double ApiSuccessRate => _healthMonitor.SuccessRate;

    public void SetApiCredentials(string apiKey, string secretKey)
    {
        _apiKey = apiKey;
        _secretBytes = Encoding.UTF8.GetBytes(secretKey);

        // 🔧 添加日志记录
        LogService.Info("[BinanceApiClient] API 凭证已设置: Key={MaskedKey}, SecretLength={SecretLength}",
            apiKey.Length > 8 ? $"{apiKey.Substring(0, 8)}...{apiKey.Substring(apiKey.Length - 4)}" : "****",
            secretKey.Length);
    }

    public async Task<IReadOnlyList<FundingRateSnapshot>> GetFundingRatesAsync(string? symbol = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            query["symbol"] = symbol.ToUpperInvariant();
        }

        List<FundingRateDto> fundingRates = await SendPublicAsync<List<FundingRateDto>>(HttpMethod.Get, "/fapi/v1/fundingRate", query, cancellationToken).ConfigureAwait(false);
        List<MarkPriceDto> markPrices = await SendPublicAsync<List<MarkPriceDto>>(HttpMethod.Get, "/fapi/v1/premiumIndex", symbol is null ? null : new Dictionary<string, string?> { ["symbol"] = symbol.ToUpperInvariant() }, cancellationToken).ConfigureAwait(false);
        var markMap = markPrices.ToDictionary(m => m.Symbol, m => m);

        return fundingRates
            .GroupBy(r => r.Symbol)
            .Select(g => MapFunding(g.Key, g.OrderByDescending(x => x.FundingTime).Take(limit).ToArray(), markMap))
            .OrderByDescending(f => Math.Abs(f.LastFundingRate))
            .ToArray();
    }

    public async Task<IReadOnlyList<TickerQuote>> GetMiniTickersAsync(IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default)
    {
        List<TickerDto> tickers = await SendPublicAsync<List<TickerDto>>(HttpMethod.Get, "/fapi/v1/ticker/24hr", null, cancellationToken).ConfigureAwait(false);
        HashSet<string>? filter = null;
        if (symbols is not null)
        {
            filter = new HashSet<string>(symbols.Select(s => s.ToUpperInvariant()));
        }

        return tickers
            .Where(t => filter is null || filter.Contains(t.Symbol))
            .Select(MapTicker)
            .ToArray();
    }

    public async Task<IReadOnlyList<AccountBalance>> GetAccountBalancesAsync(CancellationToken cancellationToken = default)
    {
        EnsureSigned();
        AccountDto account = await SendSignedAsync<AccountDto>(HttpMethod.Get, "/fapi/v2/account", null, cancellationToken).ConfigureAwait(false);
        return account.Assets.Select(MapBalance).Where(b => b.WalletBalance != 0 || b.AvailableBalance != 0).ToArray();
    }

    public async Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureSigned();
        AccountDto account = await SendSignedAsync<AccountDto>(HttpMethod.Get, "/fapi/v2/account", null, cancellationToken).ConfigureAwait(false);
        return account.Positions
            .Where(p => decimal.TryParse(p.PositionAmt, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal qty) && qty != 0)
            .Select(MapPosition)
            .ToArray();
    }

    public async Task<IReadOnlyList<OrderResponse>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        EnsureSigned();
        var query = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            query["symbol"] = symbol.ToUpperInvariant();
        }

        List<OrderDto> orders = await SendSignedAsync<List<OrderDto>>(HttpMethod.Get, "/fapi/v1/openOrders", query, cancellationToken).ConfigureAwait(false);
        return orders.Select(MapOrderResponse).ToArray();
    }

    public async Task<OrderResponse> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        EnsureSigned();
        Dictionary<string, string?> query = BuildOrderPayload(request);
        OrderDto order = await SendSignedAsync<OrderDto>(HttpMethod.Post, "/fapi/v1/order", query, cancellationToken).ConfigureAwait(false);
        return MapOrderResponse(order);
    }

    public async Task<IReadOnlyList<OrderResponse>> PlaceBatchOrdersAsync(BatchOrderRequest batch, CancellationToken cancellationToken = default)
    {
        EnsureSigned();
        Dictionary<string, string?>[] payload = batch.Orders.Select(BuildOrderPayload).ToArray();
        var query = new Dictionary<string, string?>
        {
            ["batchOrders"] = JsonSerializer.Serialize(payload)
        };

        List<OrderDto> orders = await SendSignedAsync<List<OrderDto>>(HttpMethod.Post, "/fapi/v1/batchOrders", query, cancellationToken).ConfigureAwait(false);
        return orders.Select(MapOrderResponse).ToArray();
    }

    public async Task<OrderResponse> CancelOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default)
    {
        EnsureSigned();
        var query = new Dictionary<string, string?>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture)
        };
        OrderDto order = await SendSignedAsync<OrderDto>(HttpMethod.Delete, "/fapi/v1/order", query, cancellationToken).ConfigureAwait(false);
        return MapOrderResponse(order);
    }

    public async Task<IReadOnlyList<TradeExecution>> GetRecentTradesAsync(string symbol, int limit = 20, CancellationToken cancellationToken = default)
    {
        EnsureSigned();
        var query = new Dictionary<string, string?>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
        };

        List<UserTradeDto> trades = await SendSignedAsync<List<UserTradeDto>>(HttpMethod.Get, "/fapi/v1/userTrades", query, cancellationToken).ConfigureAwait(false);
        return trades.Select(MapTrade).ToArray();
    }

    public async Task<IReadOnlyList<decimal>> GetKlineClosesAsync(string symbol, string interval, int limit = 500, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["interval"] = interval,
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
        };

        string raw = await SendPublicAsync<string>(HttpMethod.Get, "/fapi/v1/klines", query, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.EnumerateArray()
            .Select(k => decimal.Parse(k[4].GetString()!, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private Dictionary<string, string?> BuildOrderPayload(OrderRequest request)
    {
        var payload = new Dictionary<string, string?>
        {
            ["symbol"] = request.Symbol.ToUpperInvariant(),
            ["side"] = request.Side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = request.Type switch
            {
                OrderType.Market => "MARKET",
                OrderType.Limit => "LIMIT",
                OrderType.StopLoss => "STOP_MARKET",
                OrderType.StopLossLimit => "STOP",
                OrderType.TakeProfit => "TAKE_PROFIT_MARKET",
                OrderType.TakeProfitLimit => "TAKE_PROFIT",
                _ => "LIMIT"
            },
            ["quantity"] = request.Quantity.ToString(CultureInfo.InvariantCulture)
        };

        if (request.Type is OrderType.Limit or OrderType.StopLossLimit or OrderType.TakeProfitLimit)
        {
            payload["price"] = request.Price.ToString(CultureInfo.InvariantCulture);
        }

        if (request.Type is OrderType.StopLoss or OrderType.StopLossLimit or OrderType.TakeProfit or OrderType.TakeProfitLimit)
        {
            payload["stopPrice"] = request.StopPrice.ToString(CultureInfo.InvariantCulture);
        }

        if (request.Type is OrderType.Limit or OrderType.StopLossLimit or OrderType.TakeProfitLimit)
        {
            payload["timeInForce"] = request.TimeInForce switch
            {
                TimeInForce.Gtc => "GTC",
                TimeInForce.Ioc => "IOC",
                TimeInForce.Fok => "FOK",
                _ => "GTC"
            };
        }

        return payload;
    }

    private async Task<T> SendPublicAsync<T>(HttpMethod method, string path, IDictionary<string, string?>? query, CancellationToken cancellationToken)
    {
        // 🆕 等待速率限制
        await _rateLimiter.WaitForRestApiAsync(weight: 1, cancellationToken);

        var request = new HttpRequestMessage(method, BuildUri(path, query));
        return await SendWithRetryAsync<T>(request, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendSignedAsync<T>(HttpMethod method, string path, IDictionary<string, string?>? query, CancellationToken cancellationToken)
    {
        EnsureSigned();
        query ??= new Dictionary<string, string?>();
        query["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        string queryString = BuildQueryString(query);
        string signature = ComputeSignature(queryString);
        query["signature"] = signature;

        // 🆕 订单API需要额外限速
        if (path.Contains("/order", StringComparison.OrdinalIgnoreCase))
        {
            await _rateLimiter.WaitForOrderApiAsync(cancellationToken);
        }
        else
        {
            await _rateLimiter.WaitForRestApiAsync(weight: 1, cancellationToken);
        }

        var request = new HttpRequestMessage(method, BuildUri(path, query));
        request.Headers.Add("X-MBX-APIKEY", _apiKey);
        return await SendWithRetryAsync<T>(request, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 🆕 带重试和熔断的请求发送
    /// </summary>
    private async Task<T> SendWithRetryAsync<T>(HttpRequestMessage request, string endpoint, CancellationToken cancellationToken)
    {
        // 检查熔断器
        if (!_circuitBreaker.AllowRequest())
        {
            TimeSpan? retry = _circuitBreaker.TimeUntilRetry();
            throw new InvalidOperationException($"API熔断中,{retry?.TotalSeconds:F0}秒后自动恢复");
        }

        Exception? lastException = null;
        DateTime startTime = DateTime.UtcNow;

        for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                TimeSpan duration = DateTime.UtcNow - startTime;

                // 🆕 429 速率限制错误,需要重试
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    _healthMonitor.RecordCall(endpoint, false, duration, "Rate limit exceeded", 429);

                    if (attempt < RetryDelays.Length)
                    {
                        TimeSpan delay = RetryDelays[attempt];
                        StartupDiagnostics.Log($"API RateLimit: {endpoint}, 等待 {delay.TotalSeconds}s 重试 (attempt {attempt + 1})");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    throw new InvalidOperationException("API速率限制,已达最大重试次数");
                }

                // 🆕 5xx 服务器错误,可重试
                if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
                {
                    _healthMonitor.RecordCall(endpoint, false, duration, $"Server error {response.StatusCode}", (int)response.StatusCode);

                    if (attempt < RetryDelays.Length)
                    {
                        TimeSpan delay = RetryDelays[attempt];
                        StartupDiagnostics.Log($"API ServerError: {endpoint} {response.StatusCode}, 等待 {delay.TotalSeconds}s 重试");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    _circuitBreaker.RecordFailure();
                    throw new HttpRequestException($"Binance服务器错误: {response.StatusCode}");
                }

                // 其他错误直接抛出
                response.EnsureSuccessStatusCode();

                // 解析响应
                T data;
                if (typeof(T) == typeof(string))
                {
                    string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    data = (T)(object)text;
                }
                else
                {
                    await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    data = await JsonSerializer.DeserializeAsync<T>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Failed to deserialize Binance response");
                }

                // 🆕 记录成功
                _healthMonitor.RecordCall(endpoint, true, duration);
                _circuitBreaker.RecordSuccess();
                return data;
            }
            catch (OperationCanceledException)
            {
                throw; // 用户取消,不重试
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                lastException = ex;
                TimeSpan duration = DateTime.UtcNow - startTime;
                _healthMonitor.RecordCall(endpoint, false, duration, ex.Message);

                TimeSpan delay = RetryDelays[attempt];
                StartupDiagnostics.Log($"API Exception: {endpoint} - {ex.Message}, 等待 {delay.TotalSeconds}s 重试");
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                TimeSpan duration = DateTime.UtcNow - startTime;
                _healthMonitor.RecordCall(endpoint, false, duration, ex.Message);
                _circuitBreaker.RecordFailure();
                throw;
            }
        }

        _circuitBreaker.RecordFailure();
        throw lastException ?? new InvalidOperationException("API请求失败");
    }

    private string ComputeSignature(string queryString)
    {
        if (_secretBytes is null)
        {
            throw new InvalidOperationException("API secret has not been configured");
        }

        using var hmac = new HMACSHA256(_secretBytes);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(queryString));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static Uri BuildUri(string path, IDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return new Uri(path, UriKind.Relative);
        }

        string queryString = BuildQueryString(query);
        return new Uri($"{path}?{queryString}", UriKind.Relative);
    }

    private static string BuildQueryString(IDictionary<string, string?> query)
    {
        return string.Join('&', query
            .Where(kvp => kvp.Value is not null)
            .Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value!)}"));
    }

    private void EnsureSigned()
    {
        if (string.IsNullOrEmpty(_apiKey) || _secretBytes is null)
        {
            throw new InvalidOperationException("请先在 API 管理中配置 Binance API Key 与 Secret");
        }
    }

    private static FundingRateSnapshot MapFunding(string symbol, FundingRateDto[] rates, IDictionary<string, MarkPriceDto> markMap)
    {
        FundingRateDto latest = rates.First();
        markMap.TryGetValue(symbol, out MarkPriceDto? mark);
        var history = rates.OrderBy(r => r.FundingTime).Select(r => new FundingHistoryPoint
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(r.FundingTime).UtcDateTime,
            FundingRate = r.FundingRate
        }).ToList();

        double avg7 = history.TakeLast(21).Average(h => h.FundingRate);
        double predicted = history.TakeLast(12).Any() ? history.TakeLast(12).Average(h => h.FundingRate) : history.LastOrDefault()?.FundingRate ?? 0d;

        return new FundingRateSnapshot
        {
            Symbol = symbol,
            Pair = symbol,
            ContractType = mark?.ContractType ?? "PERPETUAL",
            BaseAsset = mark?.Symbol?.Replace("USDT", string.Empty) ?? symbol,
            QuoteAsset = "USDT",
            MarkPrice = mark?.MarkPrice ?? 0,
            IndexPrice = mark?.IndexPrice ?? 0,
            LastFundingRate = latest.FundingRate,
            PredictedFundingRate = predicted,
            Avg7dFundingRate = avg7,
            NextFundingTime = DateTimeOffset.FromUnixTimeMilliseconds(latest.FundingTime).UtcDateTime.AddHours(8),
            OpenInterest = mark?.EstimatedSettlePrice ?? 0,
            History = history
        };
    }

    private static TickerQuote MapTicker(TickerDto dto)
    {
        return new TickerQuote(
            dto.Symbol,
            dto.LastPrice,
            dto.IndexPrice,
            dto.PriceChangePercent,
            dto.Volume,
            dto.HighPrice,
            dto.LowPrice);
    }

    private static PositionSnapshot MapPosition(PositionDto dto)
    {
        decimal.TryParse(dto.PositionAmt, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal qty);
        decimal.TryParse(dto.EntryPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal entry);
        decimal.TryParse(dto.MarkPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal mark);
        decimal.TryParse(dto.UnrealizedProfit, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal pnl);
        decimal.TryParse(dto.Leverage, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal leverage);
        decimal.TryParse(dto.MaintMargin, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal maintMargin);

        return new PositionSnapshot
        {
            Symbol = dto.Symbol,
            PositionAmt = qty,
            EntryPrice = entry,
            MarkPrice = mark,
            UnrealizedProfit = pnl,
            Leverage = leverage,
            MaintenanceMargin = maintMargin,
            IsIsolated = dto.Isolated
        };
    }

    private static AccountBalance MapBalance(AccountAssetDto dto)
    {
        decimal.TryParse(dto.WalletBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal wallet);
        decimal.TryParse(dto.AvailableBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal available);
        decimal.TryParse(dto.UnrealizedProfit, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal pnl);
        decimal.TryParse(dto.MarginBalance, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal margin);

        return new AccountBalance
        {
            Asset = dto.Asset,
            WalletBalance = wallet,
            AvailableBalance = available,
            CrossUnrealizedPnl = pnl,
            MarginBalance = margin
        };
    }

    private static OrderResponse MapOrderResponse(OrderDto dto)
    {
        return new OrderResponse
        {
            Symbol = dto.Symbol,
            OrderId = dto.OrderId,
            ClientOrderId = dto.ClientOrderId ?? string.Empty,
            ExecutedQuantity = dto.ExecutedQty,
            CumulativeQuoteQuantity = dto.CumQuote,
            Price = dto.Price,
            AvgPrice = dto.AvgPrice,
            Status = dto.Status ?? string.Empty,
            Time = DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdateTime ?? dto.Time).UtcDateTime
        };
    }

    private static TradeExecution MapTrade(UserTradeDto dto)
    {
        return new TradeExecution
        {
            Symbol = dto.Symbol,
            Side = dto.IsBuyer ? OrderSide.Buy : OrderSide.Sell,
            Quantity = dto.Qty,
            Price = dto.Price,
            Time = DateTimeOffset.FromUnixTimeMilliseconds(dto.Time).UtcDateTime
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private record FundingRateDto
    {
        public string Symbol { get; init; } = string.Empty;
        public double FundingRate { get; init; }
        public long FundingTime { get; init; }
    }

    private record MarkPriceDto
    {
        public string Symbol { get; init; } = string.Empty;
        public string ContractType { get; init; } = "PERPETUAL";
        public double MarkPrice { get; init; }
        public double IndexPrice { get; init; }
        public double EstimatedSettlePrice { get; init; }
    }

    private record TickerDto
    {
        public string Symbol { get; init; } = string.Empty;
        public double LastPrice { get; init; }
        public double PriceChangePercent { get; init; }
        public double Volume { get; init; }
        public double HighPrice { get; init; }
        public double LowPrice { get; init; }
        public double IndexPrice { get; init; }
    }

    private record AccountDto
    {
        public List<PositionDto> Positions { get; init; } = new();
        public List<AccountAssetDto> Assets { get; init; } = new();
    }

    private record PositionDto
    {
        public string Symbol { get; init; } = string.Empty;
        public string PositionAmt { get; init; } = "0";
        public string EntryPrice { get; init; } = "0";
        public string MarkPrice { get; init; } = "0";
        public string UnrealizedProfit { get; init; } = "0";
        public string Leverage { get; init; } = "0";
        public string MaintMargin { get; init; } = "0";
        public bool Isolated { get; init; }
    }

    private record AccountAssetDto
    {
        public string Asset { get; init; } = string.Empty;
        public string WalletBalance { get; init; } = "0";
        public string AvailableBalance { get; init; } = "0";
        public string MarginBalance { get; init; } = "0";
        public string UnrealizedProfit { get; init; } = "0";
    }

    private record OrderDto
    {
        public string Symbol { get; init; } = string.Empty;
        public long OrderId { get; init; }
        public string? ClientOrderId { get; init; }
        public decimal Price { get; init; }
        public decimal AvgPrice { get; init; }
        public decimal ExecutedQty { get; init; }
        public decimal CumQuote { get; init; }
        public string? Status { get; init; }
        public long Time { get; init; }
        public long? UpdateTime { get; init; }
    }

    private record UserTradeDto
    {
        public string Symbol { get; init; } = string.Empty;
        public decimal Qty { get; init; }
        public decimal Price { get; init; }
        public bool IsBuyer { get; init; }
        public long Time { get; init; }
    }
}
