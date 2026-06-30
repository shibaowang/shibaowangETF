using System.IO;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

public sealed class LocalDatabase
{
    public const string AppFolderName = "CrossETF.Terminal.UiShell.Reference";
    public const string DatabaseFileName = "cross_etf_terminal.db";

    public LocalDatabase(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName,
            DatabaseFileName);
    }

    public string DatabasePath { get; }

    public void Initialize()
    {
        string? directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
        ExecuteNonQuery(connection, "PRAGMA foreign_keys=ON;");
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS strategy_config (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                index_sec_id TEXT,
                etf_high REAL,
                index_high REAL,
                extra_price REAL,
                take_profit_price REAL,
                sell_ratio REAL,
                add_premium_limit REAL,
                t1_weight REAL,
                t2_weight REAL,
                t3_weight REAL,
                t4_weight REAL,
                t5_weight REAL,
                t6_weight REAL,
                adj_factor REAL,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        EnsureColumn(connection, "strategy_config", "extra_price", "REAL");
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS account_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                principal REAL NOT NULL DEFAULT 0,
                cash_balance REAL NOT NULL DEFAULT 0,
                total_assets REAL NOT NULL DEFAULT 0,
                base_position_ratio REAL NOT NULL DEFAULT 0,
                sniper_pool_amount REAL NOT NULL DEFAULT 0,
                memo TEXT,
                updated_at TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS position_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                strategy_code TEXT NOT NULL,
                actual_code TEXT NOT NULL,
                source TEXT NOT NULL CHECK(source IN ('场内ETF','场外替代')),
                quantity REAL NOT NULL DEFAULT 0,
                cost_amount REAL NOT NULL DEFAULT 0,
                adj_factor REAL NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS otc_channel (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                strategy_code TEXT NOT NULL,
                otc_code TEXT NOT NULL,
                class_type TEXT NOT NULL CHECK(class_type IN ('A类','C类')),
                enabled INTEGER NOT NULL DEFAULT 1,
                daily_limit REAL NOT NULL DEFAULT 0,
                priority INTEGER NOT NULL DEFAULT 999,
                min_buy REAL NOT NULL DEFAULT 0,
                memo TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS trade_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                time TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                actual_code TEXT,
                action TEXT NOT NULL CHECK(action IN ('买入','卖出','分红','送股','拆分','合并','除权校准','CASH','入金','出金')),
                price REAL NOT NULL DEFAULT 0,
                quantity REAL NOT NULL DEFAULT 0,
                amount REAL NOT NULL DEFAULT 0,
                tier TEXT,
                source TEXT,
                fee REAL NOT NULL DEFAULT 0,
                memo TEXT,
                net_cash_impact REAL NOT NULL DEFAULT 0,
                principal REAL NOT NULL DEFAULT 0,
                cash_balance REAL NOT NULL DEFAULT 0,
                total_assets REAL NOT NULL DEFAULT 0
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS runtime_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                time TEXT NOT NULL,
                level TEXT NOT NULL,
                module TEXT NOT NULL,
                message TEXT NOT NULL,
                detail TEXT
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS market_quote_cache (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                display_name TEXT,
                market_type TEXT NOT NULL,
                source TEXT NOT NULL,
                price REAL,
                last_close REAL,
                change_value REAL,
                change_percent REAL,
                high_value REAL,
                low_value REAL,
                open_value REAL,
                volume REAL,
                amount REAL,
                iopv REAL,
                quote_time TEXT,
                received_at TEXT NOT NULL,
                raw_code TEXT,
                raw_payload TEXT
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS market_source_status (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source TEXT NOT NULL,
                status TEXT NOT NULL,
                last_success_at TEXT,
                last_failure_at TEXT,
                failure_count INTEGER NOT NULL DEFAULT 0,
                cooldown_until TEXT,
                last_error TEXT,
                updated_at TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS market_history_cache (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                market_type TEXT NOT NULL,
                source TEXT NOT NULL,
                high_value REAL,
                cache_date TEXT NOT NULL,
                raw_payload TEXT,
                updated_at TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS chart_intraday_cache (
                strategy_code TEXT NOT NULL,
                actual_code TEXT,
                trade_date TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                payload TEXT NOT NULL,
                quality TEXT NOT NULL,
                source TEXT NOT NULL,
                PRIMARY KEY(strategy_code, trade_date)
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS alert_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                alert_type TEXT NOT NULL,
                severity TEXT NOT NULL,
                strategy_code TEXT,
                actual_code TEXT,
                title TEXT NOT NULL,
                content TEXT NOT NULL,
                dedupe_key TEXT NOT NULL,
                wechat_enabled INTEGER NOT NULL DEFAULT 0,
                wechat_status TEXT,
                wechat_error TEXT,
                wechat_sent_at TEXT,
                voice_enabled INTEGER NOT NULL DEFAULT 0,
                voice_status TEXT,
                voice_error TEXT,
                voice_played_at TEXT,
                source TEXT,
                content_hash TEXT
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS alert_delivery_state (
                dedupe_key TEXT PRIMARY KEY,
                last_alert_type TEXT,
                last_strategy_code TEXT,
                last_action TEXT,
                last_reason TEXT,
                last_content_hash TEXT,
                last_sent_at TEXT,
                last_status TEXT,
                last_title TEXT,
                last_content TEXT
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS account_replay_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                calculated_at TEXT NOT NULL,
                replay_status TEXT NOT NULL,
                replay_error TEXT,
                cash_balance REAL,
                principal REAL,
                total_position_cost REAL,
                known_market_value REAL,
                total_assets REAL,
                total_realized_pnl REAL,
                total_unrealized_pnl REAL,
                total_pnl REAL,
                total_return_rate REAL,
                cash_ratio REAL,
                position_ratio REAL,
                base_position_ratio REAL,
                market_value_complete INTEGER NOT NULL DEFAULT 0,
                last_trade_log_id INTEGER
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS account_replay_snapshot (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                total_assets REAL,
                total_pnl REAL,
                total_unrealized_pnl REAL,
                cash_balance REAL,
                principal REAL,
                market_value_complete INTEGER NOT NULL DEFAULT 0
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS position_replay_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                calculated_at TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                actual_code TEXT NOT NULL,
                source TEXT NOT NULL,
                quantity REAL NOT NULL DEFAULT 0,
                cost_amount REAL NOT NULL DEFAULT 0,
                average_cost REAL NOT NULL DEFAULT 0,
                adj_factor REAL NOT NULL DEFAULT 1,
                today_buy_quantity REAL NOT NULL DEFAULT 0,
                today_buy_amount REAL NOT NULL DEFAULT 0,
                market_price REAL,
                market_value REAL,
                daily_pnl REAL,
                realized_pnl REAL NOT NULL DEFAULT 0,
                unrealized_pnl REAL,
                total_pnl REAL,
                return_rate REAL,
                quote_status TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS otc_position_replay_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                calculated_at TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                actual_code TEXT NOT NULL,
                quantity REAL NOT NULL DEFAULT 0,
                cost_amount REAL NOT NULL DEFAULT 0,
                average_cost REAL NOT NULL DEFAULT 0,
                nav REAL,
                market_value REAL,
                daily_pnl REAL,
                unrealized_pnl REAL,
                return_rate REAL,
                quote_status TEXT NOT NULL
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS strategy_decision_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                calculated_at TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                name TEXT,
                action_instruction TEXT,
                strategy_status TEXT,
                preferred_source TEXT,
                target_tier TEXT,
                target_amount REAL,
                available_cash REAL,
                suggested_price REAL,
                premium REAL,
                return_rate REAL,
                etf_drawdown REAL,
                index_drawdown REAL,
                base_mode TEXT,
                base_ratio REAL,
                base_fixed_amount REAL,
                base_target_amount REAL,
                base_current_cost REAL,
                base_completion_rate REAL,
                base_gap_amount REAL,
                base_target_capped INTEGER NOT NULL DEFAULT 0,
                real_sniper_pool REAL,
                tier_total_parts REAL,
                tier_cumulative_target REAL,
                tier_executed_amount REAL,
                tier_remain_amount REAL,
                prerequisite_status TEXT,
                prerequisite_message TEXT,
                is_actionable INTEGER NOT NULL DEFAULT 0
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS order_draft_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                draft_key TEXT NOT NULL,
                calculated_at TEXT NOT NULL,
                snapshot_key TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                name TEXT,
                action_instruction TEXT,
                side TEXT NOT NULL,
                source TEXT NOT NULL,
                target_tier TEXT,
                target_amount REAL,
                price REAL,
                quantity REAL NOT NULL DEFAULT 0,
                amount REAL NOT NULL DEFAULT 0,
                draft_status TEXT NOT NULL,
                reason TEXT,
                is_executable INTEGER NOT NULL DEFAULT 0
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS order_draft_leg_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                draft_id INTEGER NOT NULL DEFAULT 0,
                draft_key TEXT NOT NULL,
                calculated_at TEXT NOT NULL,
                snapshot_key TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                actual_code TEXT,
                side TEXT NOT NULL,
                source TEXT NOT NULL,
                channel_class TEXT,
                priority INTEGER,
                price REAL,
                nav REAL,
                quantity REAL NOT NULL DEFAULT 0,
                amount REAL NOT NULL DEFAULT 0,
                leg_status TEXT NOT NULL,
                reason TEXT
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS order_finalization_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                finalized_at TEXT NOT NULL,
                draft_calculated_at TEXT NOT NULL,
                draft_key TEXT NOT NULL,
                snapshot_key TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                name TEXT,
                action_instruction TEXT,
                side TEXT NOT NULL,
                source TEXT NOT NULL,
                target_tier TEXT,
                target_amount REAL,
                price REAL,
                quantity REAL NOT NULL DEFAULT 0,
                amount REAL NOT NULL DEFAULT 0,
                finalization_status TEXT NOT NULL,
                reason TEXT,
                memo TEXT
            );
            """);
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS order_finalization_leg_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                finalization_id INTEGER NOT NULL DEFAULT 0,
                finalized_at TEXT NOT NULL,
                draft_key TEXT NOT NULL,
                snapshot_key TEXT NOT NULL,
                strategy_code TEXT NOT NULL,
                actual_code TEXT,
                side TEXT NOT NULL,
                source TEXT NOT NULL,
                channel_class TEXT,
                priority INTEGER,
                price REAL,
                nav REAL,
                quantity REAL NOT NULL DEFAULT 0,
                amount REAL NOT NULL DEFAULT 0,
                leg_status TEXT NOT NULL,
                reason TEXT
            );
            """);
        EnsureColumn(connection, "strategy_decision_state", "base_mode", "TEXT");
        EnsureColumn(connection, "strategy_decision_state", "base_ratio", "REAL");
        EnsureColumn(connection, "strategy_decision_state", "base_fixed_amount", "REAL");
        EnsureColumn(connection, "strategy_decision_state", "base_completion_rate", "REAL");
        EnsureColumn(connection, "strategy_decision_state", "base_target_capped", "INTEGER NOT NULL DEFAULT 0");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_strategy_config_code ON strategy_config(code);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_position_state_strategy_code ON position_state(strategy_code);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_otc_channel_strategy_code ON otc_channel(strategy_code);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_trade_log_time ON trade_log(time);");
        ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS ux_market_quote_cache_key ON market_quote_cache(symbol, market_type, source);");
        ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS ux_market_source_status_source ON market_source_status(source);");
        ExecuteNonQuery(connection, "CREATE UNIQUE INDEX IF NOT EXISTS ux_market_history_cache_key ON market_history_cache(symbol, market_type, source, cache_date);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_chart_intraday_cache_strategy_date ON chart_intraday_cache(strategy_code, trade_date);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_account_replay_state_calculated_at ON account_replay_state(calculated_at);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_account_replay_snapshot_created_at ON account_replay_snapshot(created_at);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_position_replay_state_strategy_code ON position_replay_state(strategy_code);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_otc_position_replay_state_strategy_code ON otc_position_replay_state(strategy_code);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_strategy_decision_state_strategy_code ON strategy_decision_state(strategy_code);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_strategy_decision_state_calculated_at ON strategy_decision_state(calculated_at);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_order_draft_state_strategy_code ON order_draft_state(strategy_code);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_order_draft_state_snapshot_key ON order_draft_state(snapshot_key);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_order_draft_leg_state_draft_key ON order_draft_leg_state(draft_key);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_order_finalization_state_finalized_at ON order_finalization_state(finalized_at);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_order_finalization_state_snapshot_key ON order_finalization_state(snapshot_key);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_order_finalization_leg_state_finalization_id ON order_finalization_leg_state(finalization_id);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_alert_log_created_at ON alert_log(created_at);");
        ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS ix_alert_log_dedupe_key ON alert_log(dedupe_key);");

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = """
            INSERT INTO app_settings(key, value, updated_at)
            VALUES ('schema_version', '1', $updated_at)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
            """;
        versionCommand.Parameters.AddWithValue("$updated_at", NowText());
        versionCommand.ExecuteNonQuery();
    }

    public SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    public static string NowText() => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var probe = connection.CreateCommand();
        probe.CommandText = $"PRAGMA table_info({tableName});";
        using (var reader = probe.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        ExecuteNonQuery(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
    }
}
