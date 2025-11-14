using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        // 新增：AI信号表
        string createSignals = @"CREATE TABLE IF NOT EXISTS signals (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                action TEXT NOT NULL,
                confidence REAL NOT NULL,
                reason TEXT,
                source TEXT,
                timestamp INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_signals_symbol ON signals(symbol);
            CREATE INDEX IF NOT EXISTS idx_signals_timestamp ON signals(timestamp DESC);";

        // 新增：决策记录表
        string createDecisionRecords = @"CREATE TABLE IF NOT EXISTS decision_records (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER UNIQUE NOT NULL,
                state_json TEXT NOT NULL,
                decision_json TEXT NOT NULL,
                outcome_json TEXT,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_decision_records_timestamp ON decision_records(timestamp DESC);";

        string createLearningState = @"CREATE TABLE IF NOT EXISTS learning_state (
                key TEXT PRIMARY KEY,
                json TEXT NOT NULL,
                updated_at INTEGER NOT NULL
            );";

        string createAlerts = @"CREATE TABLE IF NOT EXISTS alerts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                rule_name TEXT,
                severity TEXT,
                message TEXT,
                timestamp INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_alerts_timestamp ON alerts(timestamp DESC);";

        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = string.Join(Environment.NewLine, new[] { createFunding, createPrices, createAccounts, createOrders, createTrades, createStrategyPerf, createDailyPnl, createSignals, createDecisionRecords, createLearningState, createAlerts });
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task SaveFundingRatesAsync(IEnumerable<FundingRateSnapshot> snapshots)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        foreach (FundingRateSnapshot snapshot in snapshots)
        {
            foreach (FundingHistoryPoint point in snapshot.History)
            {
                await using SqliteCommand cmd = connection.CreateCommand();
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
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT rate FROM funding_rates WHERE symbol = $symbol ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<double>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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
        await using DbTransaction transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        long timestamp = now.ToUnixTimeMilliseconds();

        foreach (decimal close in closes)
        {
            await using SqliteCommand cmd = connection.CreateCommand();
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
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT close FROM price_history WHERE symbol = $symbol ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.Parameters.AddWithValue("$limit", limit);

        var closes = new List<double>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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
        await using SqliteCommand cmd = connection.CreateCommand();
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
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, label, api_key, encrypted_secret, passphrase_hint, is_primary, is_paper, created_at, notes FROM accounts";

        var result = new List<AccountProfile>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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
        await using SqliteCommand cmd = connection.CreateCommand();
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
        await using SqliteCommand cmd = connection.CreateCommand();

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
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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
        await using SqliteCommand cmd = connection.CreateCommand();
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
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT date, realized_pnl, unrealized_pnl, total_pnl, trade_count, winning_trades, losing_trades 
                            FROM daily_pnl 
                            ORDER BY date DESC 
                            LIMIT $days";
        cmd.Parameters.AddWithValue("$days", days);

        var result = new List<DailyPnlSnapshot>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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
        await using SqliteCommand cmd = connection.CreateCommand();
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
        await using SqliteCommand cmd = connection.CreateCommand();
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
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT strategy_name, symbol, total_trades, winning_trades, losing_trades, total_pnl, win_rate, profit_factor, sharpe_ratio, max_drawdown, updated_at
                            FROM strategy_performance 
                            WHERE strategy_name = $name AND symbol = $sym";
        cmd.Parameters.AddWithValue("$name", strategyName);
        cmd.Parameters.AddWithValue("$sym", symbol);

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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

    // 新增方法：保存 AI 信号
    public async Task SaveSignalAsync(string symbol, string action, double confidence, string? reason, string? source, DateTime timestamp)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO signals(symbol, action, confidence, reason, source, timestamp) VALUES ($symbol, $action, $confidence, $reason, $source, $ts);";
        cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$confidence", confidence);
        cmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", new DateTimeOffset(timestamp).ToUnixTimeMilliseconds());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // 新增方法：查询 AI 信号
    public async Task<IReadOnlyList<SignalRecord>> LoadSignalsAsync(string? symbol = null, int limit = 100)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            cmd.CommandText = "SELECT id, symbol, action, confidence, reason, source, timestamp FROM signals ORDER BY timestamp DESC LIMIT $limit";
        }
        else
        {
            cmd.CommandText = "SELECT id, symbol, action, confidence, reason, source, timestamp FROM signals WHERE symbol = $symbol ORDER BY timestamp DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$symbol", symbol);
        }
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<SignalRecord>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new SignalRecord
            {
                Id = reader.GetInt64(0),
                Symbol = reader.GetString(1),
                Action = reader.GetString(2),
                Confidence = reader.GetDouble(3),
                Reason = reader.IsDBNull(4) ? null : reader.GetString(4),
                Source = reader.IsDBNull(5) ? null : reader.GetString(5),
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)).UtcDateTime
            });
        }

        return result;
    }

    public async Task SaveDecisionRecordAsync(long timestampMs, string stateJson, string decisionJson)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO decision_records(timestamp, state_json, decision_json, created_at) VALUES ($ts, $state, $decision, $created);";
        cmd.Parameters.AddWithValue("$ts", timestampMs);
        cmd.Parameters.AddWithValue("$state", stateJson);
        cmd.Parameters.AddWithValue("$decision", decisionJson);
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task UpdateDecisionRecordOutcomeAsync(long timestampMs, string outcomeJson)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE decision_records SET outcome_json = $outcome WHERE timestamp = $ts;";
        cmd.Parameters.AddWithValue("$outcome", outcomeJson);
        cmd.Parameters.AddWithValue("$ts", timestampMs);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DecisionRecordDb>> LoadDecisionRecordsAsync(int limit = 100)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT timestamp, state_json, decision_json, outcome_json FROM decision_records ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<DecisionRecordDb>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new DecisionRecordDb
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)).UtcDateTime,
                StateJson = reader.GetString(1),
                DecisionJson = reader.GetString(2),
                OutcomeJson = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return result;
    }

    public class SignalRecord
    {
        public long Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string? Reason { get; set; }
        public string? Source { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DecisionRecordDb
    {
        public DateTime Timestamp { get; set; }
        public string StateJson { get; set; } = string.Empty;
        public string DecisionJson { get; set; } = string.Empty;
        public string? OutcomeJson { get; set; }
    }

    public async Task SaveLearningStateAsync(string key, string json)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO learning_state(key, json, updated_at) VALUES ($key, $json, $ts)
                            ON CONFLICT(key) DO UPDATE SET json = $json, updated_at = $ts;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<string?> LoadLearningStateAsync(string key)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT json FROM learning_state WHERE key = $key LIMIT 1";
        cmd.Parameters.AddWithValue("$key", key);

        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return reader.GetString(0);
        }

        return null;
    }

    public async Task SaveAlertAsync(string ruleName, string severity, string message, DateTime timestamp)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO alerts(rule_name, severity, message, timestamp) VALUES ($rule, $sev, $msg, $ts);";
        cmd.Parameters.AddWithValue("$rule", (object?)ruleName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sev", severity);
        cmd.Parameters.AddWithValue("$msg", (object?)message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", new DateTimeOffset(timestamp).ToUnixTimeMilliseconds());
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    // Ensure alerts table has resolution columns and return true if migration executed
    private async Task EnsureAlertsSchemaAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        // check columns
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(alerts);";
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                existing.Add(reader.GetString(1));
            }
        }

        if (!existing.Contains("is_resolved"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE alerts ADD COLUMN is_resolved INTEGER DEFAULT 0;";
            await alter.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (!existing.Contains("resolved_at"))
        {
            await using var alter2 = connection.CreateCommand();
            alter2.CommandText = "ALTER TABLE alerts ADD COLUMN resolved_at INTEGER;";
            await alter2.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AlertRow>> LoadAlertsAsync(int limit = 200)
    {
        await EnsureAlertsSchemaAsync().ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, rule_name, severity, message, timestamp, is_resolved, resolved_at FROM alerts ORDER BY timestamp DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<AlertRow>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new AlertRow
            {
                Id = reader.GetInt64(0),
                RuleName = reader.IsDBNull(1) ? null : reader.GetString(1),
                Severity = reader.IsDBNull(2) ? null : reader.GetString(2),
                Message = reader.IsDBNull(3) ? null : reader.GetString(3),
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)).UtcDateTime,
                IsResolved = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                ResolvedAt = reader.IsDBNull(6) ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)).UtcDateTime
            });
        }

        return result;
    }

    public async Task UpdateAlertResolvedAsync(long id, bool resolved, DateTime? resolvedAt)
    {
        await EnsureAlertsSchemaAsync().ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE alerts SET is_resolved = $res, resolved_at = $rt WHERE id = $id";
        cmd.Parameters.AddWithValue("$res", resolved ? 1 : 0);
        cmd.Parameters.AddWithValue("$rt", resolvedAt.HasValue ? new DateTimeOffset(resolvedAt.Value).ToUnixTimeMilliseconds() : (object?)DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public class AlertRow
    {
        public long Id { get; set; }
        public string? RuleName { get; set; }
        public string? Severity { get; set; }
        public string? Message { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsResolved { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}
