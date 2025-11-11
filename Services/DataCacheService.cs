using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

public class DataCacheService
{
    private readonly string _connectionString;

    public DataCacheService()
    {
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        string dbPath = Path.Combine(dataDirectory, "terminal_cache.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        string createFunding = @"CREATE TABLE IF NOT EXISTS funding_rates (
                symbol TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                rate REAL NOT NULL,
                PRIMARY KEY(symbol, timestamp)
            );";

        string createPrices = @"CREATE TABLE IF NOT EXISTS price_history (
                symbol TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                close REAL NOT NULL,
                PRIMARY KEY(symbol, timestamp)
            );";

        string createAccounts = @"CREATE TABLE IF NOT EXISTS accounts (
                id TEXT PRIMARY KEY,
                label TEXT NOT NULL,
                api_key TEXT NOT NULL,
                encrypted_secret TEXT,
                passphrase_hint TEXT,
                is_primary INTEGER NOT NULL,
                is_paper INTEGER NOT NULL,
                created_at INTEGER NOT NULL,
                notes TEXT
            );";

        string createOrders = @"CREATE TABLE IF NOT EXISTS orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                order_id TEXT UNIQUE NOT NULL,
                symbol TEXT NOT NULL,
                side TEXT NOT NULL,
                type TEXT NOT NULL,
                quantity REAL NOT NULL,
                price REAL,
                stop_price REAL,
                status TEXT NOT NULL,
                filled_qty REAL DEFAULT 0,
                avg_fill_price REAL,
                commission REAL DEFAULT 0,
                strategy_name TEXT,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL,
                filled_at INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_orders_symbol ON orders(symbol);
            CREATE INDEX IF NOT EXISTS idx_orders_created ON orders(created_at DESC);";

        string createTrades = @"CREATE TABLE IF NOT EXISTS trades (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                trade_id TEXT UNIQUE NOT NULL,
                order_id TEXT NOT NULL,
                symbol TEXT NOT NULL,
                side TEXT NOT NULL,
                quantity REAL NOT NULL,
                price REAL NOT NULL,
                commission REAL NOT NULL,
                realized_pnl REAL,
                timestamp INTEGER NOT NULL,
                FOREIGN KEY (order_id) REFERENCES orders(order_id)
            );
            CREATE INDEX IF NOT EXISTS idx_trades_symbol ON trades(symbol);
            CREATE INDEX IF NOT EXISTS idx_trades_timestamp ON trades(timestamp DESC);";

        string createStrategyPerf = @"CREATE TABLE IF NOT EXISTS strategy_performance (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                strategy_name TEXT NOT NULL,
                symbol TEXT NOT NULL,
                total_trades INTEGER DEFAULT 0,
                winning_trades INTEGER DEFAULT 0,
                losing_trades INTEGER DEFAULT 0,
                total_pnl REAL DEFAULT 0,
                win_rate REAL DEFAULT 0,
                profit_factor REAL DEFAULT 0,
                sharpe_ratio REAL DEFAULT 0,
                max_drawdown REAL DEFAULT 0,
                updated_at INTEGER NOT NULL,
                UNIQUE(strategy_name, symbol)
            );";

        string createDailyPnl = @"CREATE TABLE IF NOT EXISTS daily_pnl (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                date TEXT UNIQUE NOT NULL,
                realized_pnl REAL DEFAULT 0,
                unrealized_pnl REAL DEFAULT 0,
                total_pnl REAL DEFAULT 0,
                trade_count INTEGER DEFAULT 0,
                winning_trades INTEGER DEFAULT 0,
                losing_trades INTEGER DEFAULT 0
            );";

        await using var cmd = connection.CreateCommand();
        // Fix: use Environment.NewLine to join multiple DDL statements
        cmd.CommandText = string.Join(Environment.NewLine, new[] { createFunding, createPrices, createAccounts, createOrders, createTrades, createStrategyPerf, createDailyPnl });
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task SaveFundingRatesAsync(IEnumerable<FundingRateSnapshot> snapshots)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        foreach (var snapshot in snapshots)
        {
            foreach (var point in snapshot.History)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO funding_rates(symbol, timestamp, rate) VALUES ($symbol, $timestamp, $rate);";
                cmd.Parameters.AddWithValue("$symbol", snapshot.Symbol);
                cmd.Parameters.AddWithValue("$timestamp", new DateTimeOffset(point.Timestamp).ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$rate", point.FundingRate);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<double>> LoadFundingRatesAsync(string symbol, int limit)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT rate FROM funding_rates WHERE symbol = $symbol ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<double>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(reader.GetDouble(0));
        }

        results.Reverse();
        return results;
    }

    public async Task SavePricesAsync(string symbol, IEnumerable<decimal> closes)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        long timestamp = now.ToUnixTimeMilliseconds();

        foreach (decimal close in closes)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO price_history(symbol, timestamp, close) VALUES ($symbol, $timestamp, $close);";
            cmd.Parameters.AddWithValue("$symbol", symbol);
            cmd.Parameters.AddWithValue("$timestamp", timestamp--);
            cmd.Parameters.AddWithValue("$close", close);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<double>> LoadReturnsAsync(string symbol, int limit)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT close FROM price_history WHERE symbol = $symbol ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.Parameters.AddWithValue("$limit", limit);

        var closes = new List<double>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            closes.Add(reader.GetDouble(0));
        }

        closes.Reverse();
        if (closes.Count < 2)
        {
            return Array.Empty<double>();
        }

        return closes.Zip(closes.Skip(1), (prev, next) => Math.Log(next / prev)).ToArray();
    }

    public async Task SaveAccountProfileAsync(AccountProfile profile)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO accounts(id, label, api_key, encrypted_secret, passphrase_hint, is_primary, is_paper, created_at, notes)
                            VALUES ($id, $label, $api, $secret, $hint, $primary, $paper, $created, $notes);";
        cmd.Parameters.AddWithValue("$id", profile.Id.ToString());
        cmd.Parameters.AddWithValue("$label", profile.Label);
        cmd.Parameters.AddWithValue("$api", profile.ApiKey);
        cmd.Parameters.AddWithValue("$secret", (object?)profile.EncryptedSecret ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hint", (object?)profile.PassphraseHint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$primary", profile.IsPrimary ? 1 : 0);
        cmd.Parameters.AddWithValue("$paper", profile.IsPaperTrading ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", new DateTimeOffset(profile.CreatedAt).ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$notes", (object?)profile.Notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountProfile>> LoadAccountsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, label, api_key, encrypted_secret, passphrase_hint, is_primary, is_paper, created_at, notes FROM accounts";

        var result = new List<AccountProfile>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var profile = new AccountProfile
            {
                Label = reader.GetString(1),
                ApiKey = reader.GetString(2),
                EncryptedSecret = reader.IsDBNull(3) ? null : reader.GetString(3),
                PassphraseHint = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsPrimary = reader.GetInt32(5) == 1,
                IsPaperTrading = reader.GetInt32(6) == 1,
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
            profile.Id = Guid.Parse(reader.GetString(0));
            profile.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(7)).UtcDateTime;
            result.Add(profile);
        }

        return result;
    }

    public async Task SaveOrderAsync(OrderHistoryRecord order)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO orders(order_id, symbol, side, type, quantity, price, stop_price, status, filled_qty, avg_fill_price, commission, strategy_name, created_at, updated_at, filled_at)
                            VALUES ($oid, $sym, $side, $type, $qty, $price, $stop, $status, $filled, $avg, $comm, $strat, $created, $updated, $filledAt);";
        cmd.Parameters.AddWithValue("$oid", order.OrderId);
        cmd.Parameters.AddWithValue("$sym", order.Symbol);
        cmd.Parameters.AddWithValue("$side", order.Side);
        cmd.Parameters.AddWithValue("$type", order.Type);
        cmd.Parameters.AddWithValue("$qty", order.Quantity);
        cmd.Parameters.AddWithValue("$price", (object?)order.Price ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$stop", (object?)order.StopPrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", order.Status);
        cmd.Parameters.AddWithValue("$filled", order.FilledQuantity);
        cmd.Parameters.AddWithValue("$avg", (object?)order.AvgFillPrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$comm", order.Commission);
        cmd.Parameters.AddWithValue("$strat", (object?)order.StrategyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", new DateTimeOffset(order.CreatedAt).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$updated", new DateTimeOffset(order.UpdatedAt).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$filledAt", order.FilledAt.HasValue ? new DateTimeOffset(order.FilledAt.Value).ToUnixTimeMilliseconds() : DBNull.Value);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrderHistoryRecord>> LoadOrdersAsync(string? symbol = null, int limit = 100)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            cmd.CommandText = "SELECT order_id, symbol, side, type, quantity, price, stop_price, status, filled_qty, avg_fill_price, commission, strategy_name, created_at, updated_at, filled_at FROM orders ORDER BY created_at DESC LIMIT $limit";
        }
        else
        {
            cmd.CommandText = "SELECT order_id, symbol, side, type, quantity, price, stop_price, status, filled_qty, avg_fill_price, commission, strategy_name, created_at, updated_at, filled_at FROM orders WHERE symbol = $symbol ORDER BY created_at DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$symbol", symbol);
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<OrderHistoryRecord>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new OrderHistoryRecord
            {
                OrderId = reader.GetString(0),
                Symbol = reader.GetString(1),
                Side = reader.GetString(2),
                Type = reader.GetString(3),
                Quantity = reader.GetDouble(4),
                Price = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                StopPrice = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                Status = reader.GetString(7),
                FilledQuantity = reader.GetDouble(8),
                AvgFillPrice = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                Commission = reader.GetDouble(10),
                StrategyName = reader.IsDBNull(11) ? null : reader.GetString(11),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(12)).UtcDateTime,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(13)).UtcDateTime,
                FilledAt = reader.IsDBNull(14) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(14)).UtcDateTime
            });
        }

        return result;
    }

    public async Task UpdateDailyPnlAsync(string date, double realizedPnl, int tradeCount, bool isWin)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO daily_pnl(date, realized_pnl, trade_count, winning_trades, losing_trades)
                            VALUES ($date, $pnl, $count, $win, $loss)
                            ON CONFLICT(date) DO UPDATE SET
                                realized_pnl = realized_pnl + $pnl,
                                trade_count = trade_count + $count,
                                winning_trades = winning_trades + $win,
                                losing_trades = losing_trades + $loss;";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$pnl", realizedPnl);
        cmd.Parameters.AddWithValue("$count", tradeCount);
        cmd.Parameters.AddWithValue("$win", isWin ? 1 : 0);
        cmd.Parameters.AddWithValue("$loss", isWin ? 0 : 1);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // 🆕 查询每日盈亏
    public async Task<IReadOnlyList<DailyPnlSnapshot>> LoadDailyPnlAsync(int days = 30)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT date, realized_pnl, unrealized_pnl, total_pnl, trade_count, winning_trades, losing_trades 
                            FROM daily_pnl 
                            ORDER BY date DESC 
                            LIMIT $days";
        cmd.Parameters.AddWithValue("$days", days);

        var result = new List<DailyPnlSnapshot>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new DailyPnlSnapshot
            {
                Date = DateTime.Parse(reader.GetString(0)),
                RealizedPnl = reader.GetDouble(1),
                UnrealizedPnl = reader.GetDouble(2),
                TotalPnl = reader.GetDouble(3),
                TradeCount = reader.GetInt32(4),
                WinningTrades = reader.GetInt32(5),
                LosingTrades = reader.GetInt32(6)
            });
        }

        result.Reverse(); // 按日期升序返回
        return result;
    }

    // 🆕 保存交易记录
    public async Task SaveTradeAsync(TradeRecord trade)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO trades(trade_id, order_id, symbol, side, quantity, price, commission, realized_pnl, timestamp)
                            VALUES ($tid, $oid, $sym, $side, $qty, $price, $comm, $pnl, $ts);";
        cmd.Parameters.AddWithValue("$tid", trade.TradeId);
        cmd.Parameters.AddWithValue("$oid", trade.OrderId);
        cmd.Parameters.AddWithValue("$sym", trade.Symbol);
        cmd.Parameters.AddWithValue("$side", trade.Side);
        cmd.Parameters.AddWithValue("$qty", trade.Quantity);
        cmd.Parameters.AddWithValue("$price", trade.Price);
        cmd.Parameters.AddWithValue("$comm", trade.Commission);
        cmd.Parameters.AddWithValue("$pnl", (object?)trade.RealizedPnl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", new DateTimeOffset(trade.Timestamp).ToUnixTimeMilliseconds());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

        // 同步更新每日盈亏
        if (trade.RealizedPnl.HasValue)
        {
            string date = trade.Timestamp.ToString("yyyy-MM-dd");
            await UpdateDailyPnlAsync(date, trade.RealizedPnl.Value, 1, trade.RealizedPnl.Value > 0);
        }
    }

    // 🆕 更新策略绩效
    public async Task UpdateStrategyPerformanceAsync(string strategyName, string symbol, StrategyPerformanceUpdate update)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO strategy_performance(strategy_name, symbol, total_trades, winning_trades, losing_trades, total_pnl, win_rate, profit_factor, sharpe_ratio, max_drawdown, updated_at)
                            VALUES ($name, $sym, $total, $wins, $losses, $pnl, $wr, $pf, $sr, $dd, $updated)
                            ON CONFLICT(strategy_name, symbol) DO UPDATE SET
                                total_trades = total_trades + $total,
                                winning_trades = winning_trades + $wins,
                                losing_trades = losing_trades + $losses,
                                total_pnl = total_pnl + $pnl,
                                win_rate = CAST(winning_trades + $wins AS REAL) / (total_trades + $total),
                                profit_factor = $pf,
                                sharpe_ratio = $sr,
                                max_drawdown = CASE WHEN $dd < max_drawdown THEN $dd ELSE max_drawdown END,
                                updated_at = $updated;";
        cmd.Parameters.AddWithValue("$name", strategyName);
        cmd.Parameters.AddWithValue("$sym", symbol);
        cmd.Parameters.AddWithValue("$total", update.TradeCount);
        cmd.Parameters.AddWithValue("$wins", update.WinningTrades);
        cmd.Parameters.AddWithValue("$losses", update.LosingTrades);
        cmd.Parameters.AddWithValue("$pnl", update.PnlDelta);
        cmd.Parameters.AddWithValue("$wr", update.WinRate);
        cmd.Parameters.AddWithValue("$pf", update.ProfitFactor);
        cmd.Parameters.AddWithValue("$sr", update.SharpeRatio);
        cmd.Parameters.AddWithValue("$dd", update.MaxDrawdown);
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // 🆕 查询策略绩效
    public async Task<StrategyPerformanceRecord?> LoadStrategyPerformanceAsync(string strategyName, string symbol)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT strategy_name, symbol, total_trades, winning_trades, losing_trades, total_pnl, win_rate, profit_factor, sharpe_ratio, max_drawdown, updated_at
                            FROM strategy_performance 
                            WHERE strategy_name = $name AND symbol = $sym";
        cmd.Parameters.AddWithValue("$name", strategyName);
        cmd.Parameters.AddWithValue("$sym", symbol);

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new StrategyPerformanceRecord
            {
                StrategyName = reader.GetString(0),
                Symbol = reader.GetString(1),
                TotalTrades = reader.GetInt32(2),
                WinningTrades = reader.GetInt32(3),
                LosingTrades = reader.GetInt32(4),
                TotalPnl = reader.GetDouble(5),
                WinRate = reader.GetDouble(6),
                ProfitFactor = reader.GetDouble(7),
                SharpeRatio = reader.GetDouble(8),
                MaxDrawdown = reader.GetDouble(9),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(10)).UtcDateTime
            };
        }

        return null;
    }
}
