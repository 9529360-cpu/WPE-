-- Database Schema for Binance Quantitative Trading Bot
-- SQLite Database Initialization Script
-- Version: 1.0.0
-- Last Updated: 2024-11-17

-- ============================================================================
-- TABLE: klines
-- Description: Store historical K-line (candlestick) data
-- ============================================================================
CREATE TABLE IF NOT EXISTS klines (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    interval TEXT NOT NULL,
    open_time INTEGER NOT NULL,
    open REAL NOT NULL,
    high REAL NOT NULL,
    low REAL NOT NULL,
    close REAL NOT NULL,
    volume REAL NOT NULL,
    close_time INTEGER NOT NULL,
    quote_volume REAL,
    trades INTEGER,
    taker_buy_base REAL,
    taker_buy_quote REAL,
    created_at INTEGER DEFAULT (strftime('%s', 'now')),
    UNIQUE(symbol, interval, open_time)
);

CREATE INDEX IF NOT EXISTS idx_klines_symbol_interval 
    ON klines(symbol, interval);
CREATE INDEX IF NOT EXISTS idx_klines_open_time 
    ON klines(open_time DESC);
CREATE INDEX IF NOT EXISTS idx_klines_lookup 
    ON klines(symbol, interval, open_time DESC);

-- ============================================================================
-- TABLE: trades
-- Description: Store executed trade records
-- ============================================================================
CREATE TABLE IF NOT EXISTS trades (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    trade_id TEXT UNIQUE,
    symbol TEXT NOT NULL,
    order_id TEXT NOT NULL,
    side TEXT NOT NULL CHECK(side IN ('BUY', 'SELL')),
    price REAL NOT NULL CHECK(price > 0),
    quantity REAL NOT NULL CHECK(quantity > 0),
    quote_quantity REAL NOT NULL,
    commission REAL NOT NULL DEFAULT 0,
    commission_asset TEXT,
    timestamp INTEGER NOT NULL,
    is_buyer INTEGER NOT NULL DEFAULT 0,
    is_maker INTEGER NOT NULL DEFAULT 0,
    strategy_name TEXT,
    notes TEXT,
    created_at INTEGER DEFAULT (strftime('%s', 'now'))
);

CREATE INDEX IF NOT EXISTS idx_trades_symbol 
    ON trades(symbol);
CREATE INDEX IF NOT EXISTS idx_trades_timestamp 
    ON trades(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_trades_strategy 
    ON trades(strategy_name);
CREATE INDEX IF NOT EXISTS idx_trades_order_id 
    ON trades(order_id);

-- ============================================================================
-- TABLE: positions
-- Description: Store current positions
-- ============================================================================
CREATE TABLE IF NOT EXISTS positions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL UNIQUE,
    quantity REAL NOT NULL,
    entry_price REAL NOT NULL CHECK(entry_price > 0),
    current_price REAL,
    unrealized_pnl REAL DEFAULT 0,
    realized_pnl REAL DEFAULT 0,
    average_cost REAL,
    strategy_name TEXT,
    opened_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    CHECK(quantity <> 0)
);

CREATE INDEX IF NOT EXISTS idx_positions_symbol 
    ON positions(symbol);
CREATE INDEX IF NOT EXISTS idx_positions_strategy 
    ON positions(strategy_name);

-- ============================================================================
-- TABLE: orders
-- Description: Store order history
-- ============================================================================
CREATE TABLE IF NOT EXISTS orders (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id TEXT UNIQUE NOT NULL,
    client_order_id TEXT,
    symbol TEXT NOT NULL,
    side TEXT NOT NULL CHECK(side IN ('BUY', 'SELL')),
    type TEXT NOT NULL CHECK(type IN ('LIMIT', 'MARKET', 'STOP_LOSS', 'STOP_LOSS_LIMIT', 'TAKE_PROFIT', 'TAKE_PROFIT_LIMIT')),
    status TEXT NOT NULL CHECK(status IN ('NEW', 'PARTIALLY_FILLED', 'FILLED', 'CANCELED', 'REJECTED', 'EXPIRED')),
    price REAL,
    stop_price REAL,
    quantity REAL NOT NULL CHECK(quantity > 0),
    executed_quantity REAL DEFAULT 0,
    cumulative_quote_quantity REAL DEFAULT 0,
    time_in_force TEXT DEFAULT 'GTC',
    strategy_name TEXT,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    filled_at INTEGER
);

CREATE INDEX IF NOT EXISTS idx_orders_symbol 
    ON orders(symbol);
CREATE INDEX IF NOT EXISTS idx_orders_status 
    ON orders(status);
CREATE INDEX IF NOT EXISTS idx_orders_created_at 
    ON orders(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_orders_strategy 
    ON orders(strategy_name);

-- ============================================================================
-- TABLE: account_balance
-- Description: Store account balance snapshots
-- ============================================================================
CREATE TABLE IF NOT EXISTS account_balance (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    asset TEXT NOT NULL,
    free REAL NOT NULL DEFAULT 0,
    locked REAL NOT NULL DEFAULT 0,
    total REAL GENERATED ALWAYS AS (free + locked) STORED,
    timestamp INTEGER NOT NULL,
    UNIQUE(asset, timestamp)
);

CREATE INDEX IF NOT EXISTS idx_balance_asset 
    ON account_balance(asset);
CREATE INDEX IF NOT EXISTS idx_balance_timestamp 
    ON account_balance(timestamp DESC);

-- ============================================================================
-- TABLE: strategy_performance
-- Description: Store strategy performance metrics
-- ============================================================================
CREATE TABLE IF NOT EXISTS strategy_performance (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    strategy_name TEXT NOT NULL,
    symbol TEXT NOT NULL,
    total_trades INTEGER DEFAULT 0,
    winning_trades INTEGER DEFAULT 0,
    losing_trades INTEGER DEFAULT 0,
    total_pnl REAL DEFAULT 0,
    total_commission REAL DEFAULT 0,
    max_drawdown REAL DEFAULT 0,
    sharpe_ratio REAL,
    win_rate REAL,
    profit_factor REAL,
    avg_win REAL,
    avg_loss REAL,
    started_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    UNIQUE(strategy_name, symbol)
);

CREATE INDEX IF NOT EXISTS idx_performance_strategy 
    ON strategy_performance(strategy_name);
CREATE INDEX IF NOT EXISTS idx_performance_updated 
    ON strategy_performance(updated_at DESC);

-- ============================================================================
-- TABLE: risk_events
-- Description: Store risk management events and alerts
-- ============================================================================
CREATE TABLE IF NOT EXISTS risk_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_type TEXT NOT NULL CHECK(event_type IN ('STOP_LOSS', 'MAX_DRAWDOWN', 'POSITION_LIMIT', 'DAILY_LOSS', 'BLACKLIST')),
    severity TEXT NOT NULL CHECK(severity IN ('INFO', 'WARNING', 'CRITICAL')),
    symbol TEXT,
    strategy_name TEXT,
    message TEXT NOT NULL,
    details TEXT,
    triggered_at INTEGER NOT NULL,
    resolved_at INTEGER,
    is_resolved INTEGER DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_risk_events_type 
    ON risk_events(event_type);
CREATE INDEX IF NOT EXISTS idx_risk_events_severity 
    ON risk_events(severity);
CREATE INDEX IF NOT EXISTS idx_risk_events_triggered 
    ON risk_events(triggered_at DESC);
CREATE INDEX IF NOT EXISTS idx_risk_events_resolved 
    ON risk_events(is_resolved);

-- ============================================================================
-- TABLE: feature_store
-- Description: Store computed features for ML models
-- ============================================================================
CREATE TABLE IF NOT EXISTS feature_store (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    timeframe TEXT NOT NULL,
    timestamp INTEGER NOT NULL,
    feature_name TEXT NOT NULL,
    feature_value REAL NOT NULL,
    created_at INTEGER DEFAULT (strftime('%s', 'now')),
    UNIQUE(symbol, timeframe, timestamp, feature_name)
);

CREATE INDEX IF NOT EXISTS idx_features_symbol_timeframe 
    ON feature_store(symbol, timeframe);
CREATE INDEX IF NOT EXISTS idx_features_timestamp 
    ON feature_store(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_features_name 
    ON feature_store(feature_name);

-- ============================================================================
-- TABLE: strategy_config
-- Description: Store strategy configurations
-- ============================================================================
CREATE TABLE IF NOT EXISTS strategy_config (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    strategy_name TEXT NOT NULL UNIQUE,
    strategy_type TEXT NOT NULL,
    parameters TEXT NOT NULL, -- JSON string
    symbols TEXT NOT NULL, -- Comma-separated or JSON array
    is_active INTEGER DEFAULT 1,
    created_at INTEGER DEFAULT (strftime('%s', 'now')),
    updated_at INTEGER DEFAULT (strftime('%s', 'now'))
);

CREATE INDEX IF NOT EXISTS idx_strategy_config_active 
    ON strategy_config(is_active);
CREATE INDEX IF NOT EXISTS idx_strategy_config_type 
    ON strategy_config(strategy_type);

-- ============================================================================
-- TABLE: system_logs
-- Description: Store application logs
-- ============================================================================
CREATE TABLE IF NOT EXISTS system_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    log_level TEXT NOT NULL CHECK(log_level IN ('DEBUG', 'INFO', 'WARNING', 'ERROR', 'CRITICAL')),
    category TEXT NOT NULL,
    message TEXT NOT NULL,
    exception TEXT,
    stack_trace TEXT,
    timestamp INTEGER NOT NULL,
    created_at INTEGER DEFAULT (strftime('%s', 'now'))
);

CREATE INDEX IF NOT EXISTS idx_logs_level 
    ON system_logs(log_level);
CREATE INDEX IF NOT EXISTS idx_logs_category 
    ON system_logs(category);
CREATE INDEX IF NOT EXISTS idx_logs_timestamp 
    ON system_logs(timestamp DESC);

-- ============================================================================
-- TABLE: api_keys (ENCRYPTED STORAGE)
-- Description: Store encrypted API keys
-- Note: Values should be encrypted before storage
-- ============================================================================
CREATE TABLE IF NOT EXISTS api_keys (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    key_name TEXT NOT NULL UNIQUE,
    api_key_encrypted TEXT NOT NULL,
    secret_key_encrypted TEXT NOT NULL,
    permissions TEXT, -- JSON string
    is_active INTEGER DEFAULT 1,
    created_at INTEGER DEFAULT (strftime('%s', 'now')),
    last_used_at INTEGER
);

CREATE INDEX IF NOT EXISTS idx_api_keys_active 
    ON api_keys(is_active);

-- ============================================================================
-- TABLE: backtest_results
-- Description: Store backtesting results
-- ============================================================================
CREATE TABLE IF NOT EXISTS backtest_results (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    strategy_name TEXT NOT NULL,
    symbol TEXT NOT NULL,
    start_date INTEGER NOT NULL,
    end_date INTEGER NOT NULL,
    initial_capital REAL NOT NULL,
    final_capital REAL NOT NULL,
    total_return REAL NOT NULL,
    annual_return REAL,
    sharpe_ratio REAL,
    max_drawdown REAL,
    win_rate REAL,
    total_trades INTEGER,
    parameters TEXT, -- JSON string
    results_json TEXT, -- Detailed results
    created_at INTEGER DEFAULT (strftime('%s', 'now'))
);

CREATE INDEX IF NOT EXISTS idx_backtest_strategy 
    ON backtest_results(strategy_name);
CREATE INDEX IF NOT EXISTS idx_backtest_created 
    ON backtest_results(created_at DESC);

-- ============================================================================
-- VIEWS: Convenient query views
-- ============================================================================

-- View: Recent trades with profit/loss
CREATE VIEW IF NOT EXISTS v_recent_trades AS
SELECT 
    t.id,
    t.symbol,
    t.side,
    t.price,
    t.quantity,
    t.quote_quantity,
    t.commission,
    t.strategy_name,
    datetime(t.timestamp, 'unixepoch') as trade_time,
    CASE 
        WHEN t.side = 'SELL' THEN (t.price - LAG(t.price) OVER (PARTITION BY t.symbol ORDER BY t.timestamp)) * t.quantity
        ELSE NULL 
    END as realized_pnl
FROM trades t
ORDER BY t.timestamp DESC;

-- View: Current positions with metrics
CREATE VIEW IF NOT EXISTS v_current_positions AS
SELECT 
    p.symbol,
    p.quantity,
    p.entry_price,
    p.current_price,
    p.unrealized_pnl,
    p.realized_pnl,
    p.strategy_name,
    ROUND(((p.current_price - p.entry_price) / p.entry_price) * 100, 2) as pnl_percentage,
    datetime(p.opened_at, 'unixepoch') as opened_at,
    datetime(p.updated_at, 'unixepoch') as updated_at
FROM positions p
WHERE p.quantity != 0;

-- View: Strategy performance summary
CREATE VIEW IF NOT EXISTS v_strategy_summary AS
SELECT 
    sp.strategy_name,
    sp.symbol,
    sp.total_trades,
    sp.winning_trades,
    sp.losing_trades,
    ROUND(sp.win_rate * 100, 2) as win_rate_pct,
    sp.total_pnl,
    sp.total_commission,
    sp.max_drawdown,
    sp.sharpe_ratio,
    sp.profit_factor,
    datetime(sp.started_at, 'unixepoch') as started_at,
    datetime(sp.updated_at, 'unixepoch') as updated_at
FROM strategy_performance sp
ORDER BY sp.total_pnl DESC;

-- ============================================================================
-- TRIGGERS: Automatic data maintenance
-- ============================================================================

-- Trigger: Update updated_at timestamp for positions
CREATE TRIGGER IF NOT EXISTS trg_positions_updated_at
AFTER UPDATE ON positions
FOR EACH ROW
BEGIN
    UPDATE positions 
    SET updated_at = strftime('%s', 'now') 
    WHERE id = NEW.id;
END;

-- Trigger: Update updated_at timestamp for orders
CREATE TRIGGER IF NOT EXISTS trg_orders_updated_at
AFTER UPDATE ON orders
FOR EACH ROW
BEGIN
    UPDATE orders 
    SET updated_at = strftime('%s', 'now') 
    WHERE id = NEW.id;
END;

-- Trigger: Update strategy_config updated_at
CREATE TRIGGER IF NOT EXISTS trg_strategy_config_updated_at
AFTER UPDATE ON strategy_config
FOR EACH ROW
BEGIN
    UPDATE strategy_config 
    SET updated_at = strftime('%s', 'now') 
    WHERE id = NEW.id;
END;

-- ============================================================================
-- INITIAL DATA: Default configurations
-- ============================================================================

-- Insert default system configuration if needed
-- (Can be uncommented and customized)

-- INSERT OR IGNORE INTO strategy_config (strategy_name, strategy_type, parameters, symbols)
-- VALUES ('Default Mean Reversion', 'MeanReversion', 
--         '{"entry_z_score": 1.5, "exit_z_score": 0.5, "lookback_period": 20, "base_quantity": 1}',
--         'BTCUSDT,ETHUSDT');

-- ============================================================================
-- MAINTENANCE QUERIES
-- ============================================================================

-- To optimize database performance, run periodically:
-- VACUUM;
-- ANALYZE;

-- To check database integrity:
-- PRAGMA integrity_check;

-- To view table sizes:
-- SELECT name, 
--        (SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND tbl_name=m.name) as index_count,
--        (SELECT COUNT(*) FROM pragma_table_info(m.name)) as column_count
-- FROM sqlite_master m WHERE type='table' ORDER BY name;

-- ============================================================================
-- CLEANUP QUERIES (Use with caution)
-- ============================================================================

-- Delete old logs (older than 90 days):
-- DELETE FROM system_logs WHERE timestamp < strftime('%s', 'now', '-90 days');

-- Delete old klines (keep only last 2 years):
-- DELETE FROM klines WHERE open_time < strftime('%s', 'now', '-730 days');

-- Archive old trades:
-- CREATE TABLE IF NOT EXISTS trades_archive AS SELECT * FROM trades WHERE timestamp < strftime('%s', 'now', '-365 days');
-- DELETE FROM trades WHERE timestamp < strftime('%s', 'now', '-365 days');

-- ============================================================================
-- END OF SCHEMA
-- ============================================================================

-- Verify installation
SELECT 'Database schema initialized successfully!' as status;
