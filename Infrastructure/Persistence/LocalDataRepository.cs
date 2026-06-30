using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

public sealed class LocalDataRepository : IAlertDeliveryStore, IChartIntradayCacheStore
{
    private const int MaxAccountReplaySnapshots = 500;
    private const double SnapshotValueTolerance = 0.009999;
    private readonly LocalDatabase _database;

    public LocalDataRepository(LocalDatabase database)
    {
        _database = database;
        _database.Initialize();
    }

    public IReadOnlyList<StrategyConfigRecord> ReadStrategyConfigs()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, index_sec_id, etf_high, index_high, extra_price, take_profit_price,
                   sell_ratio, add_premium_limit, t1_weight, t2_weight, t3_weight,
                   t4_weight, t5_weight, t6_weight, adj_factor, enabled, created_at, updated_at
            FROM strategy_config
            ORDER BY code COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<StrategyConfigRecord>();
        while (reader.Read())
        {
            records.Add(new StrategyConfigRecord
            {
                Id = reader.GetInt64(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                IndexSecId = OptionalString(reader, 3),
                EtfHigh = OptionalDouble(reader, 4),
                IndexHigh = OptionalDouble(reader, 5),
                ExtraPrice = PercentValueParser.NormalizeStoredPercent(OptionalDouble(reader, 6)),
                TakeProfitPrice = PercentValueParser.NormalizeStoredPercent(OptionalDouble(reader, 7)),
                SellRatio = PercentValueParser.NormalizeStoredPercent(OptionalDouble(reader, 8)),
                AddPremiumLimit = PercentValueParser.NormalizeStoredPercent(OptionalDouble(reader, 9)),
                T1Weight = OptionalDouble(reader, 10),
                T2Weight = OptionalDouble(reader, 11),
                T3Weight = OptionalDouble(reader, 12),
                T4Weight = OptionalDouble(reader, 13),
                T5Weight = OptionalDouble(reader, 14),
                T6Weight = OptionalDouble(reader, 15),
                AdjFactor = OptionalDouble(reader, 16),
                Enabled = reader.GetInt64(17) != 0,
                CreatedAt = reader.GetString(18),
                UpdatedAt = reader.GetString(19)
            });
        }

        return records;
    }

    public void SaveStrategyConfig(StrategyConfigRecord record)
    {
        RequireText(record.Code, "ETF 代码");
        RequireText(record.Name, "ETF 名称");
        string now = LocalDatabase.NowText();
        record.Code = record.Code.Trim();
        record.Name = record.Name.Trim();
        record.IndexSecId = NullIfWhiteSpace(record.IndexSecId);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        if (record.Id <= 0)
        {
            record.CreatedAt = now;
            record.UpdatedAt = now;
            command.CommandText = """
                INSERT INTO strategy_config(
                    code, name, index_sec_id, etf_high, index_high, extra_price, take_profit_price,
                    sell_ratio, add_premium_limit, t1_weight, t2_weight, t3_weight,
                    t4_weight, t5_weight, t6_weight, adj_factor, enabled, created_at, updated_at)
                VALUES(
                    $code, $name, $index_sec_id, $etf_high, $index_high, $extra_price, $take_profit_price,
                    $sell_ratio, $add_premium_limit, $t1_weight, $t2_weight, $t3_weight,
                    $t4_weight, $t5_weight, $t6_weight, $adj_factor, $enabled, $created_at, $updated_at);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            record.UpdatedAt = now;
            command.CommandText = """
                UPDATE strategy_config
                SET code = $code,
                    name = $name,
                    index_sec_id = $index_sec_id,
                    etf_high = $etf_high,
                    index_high = $index_high,
                    extra_price = $extra_price,
                    take_profit_price = $take_profit_price,
                    sell_ratio = $sell_ratio,
                    add_premium_limit = $add_premium_limit,
                    t1_weight = $t1_weight,
                    t2_weight = $t2_weight,
                    t3_weight = $t3_weight,
                    t4_weight = $t4_weight,
                    t5_weight = $t5_weight,
                    t6_weight = $t6_weight,
                    adj_factor = $adj_factor,
                    enabled = $enabled,
                    updated_at = $updated_at
                WHERE id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", record.Id);
        }

        command.Parameters.AddWithValue("$code", record.Code);
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$index_sec_id", DbValue(record.IndexSecId));
        command.Parameters.AddWithValue("$etf_high", DbValue(record.EtfHigh));
        command.Parameters.AddWithValue("$index_high", DbValue(record.IndexHigh));
        record.ExtraPrice = PercentValueParser.NormalizeStoredPercent(record.ExtraPrice);
        record.TakeProfitPrice = PercentValueParser.NormalizeStoredPercent(record.TakeProfitPrice);
        record.SellRatio = PercentValueParser.NormalizeStoredPercent(record.SellRatio);
        record.AddPremiumLimit = PercentValueParser.NormalizeStoredPercent(record.AddPremiumLimit);
        command.Parameters.AddWithValue("$extra_price", DbValue(record.ExtraPrice));
        command.Parameters.AddWithValue("$take_profit_price", DbValue(record.TakeProfitPrice));
        command.Parameters.AddWithValue("$sell_ratio", DbValue(record.SellRatio));
        command.Parameters.AddWithValue("$add_premium_limit", DbValue(record.AddPremiumLimit));
        command.Parameters.AddWithValue("$t1_weight", DbValue(record.T1Weight));
        command.Parameters.AddWithValue("$t2_weight", DbValue(record.T2Weight));
        command.Parameters.AddWithValue("$t3_weight", DbValue(record.T3Weight));
        command.Parameters.AddWithValue("$t4_weight", DbValue(record.T4Weight));
        command.Parameters.AddWithValue("$t5_weight", DbValue(record.T5Weight));
        command.Parameters.AddWithValue("$t6_weight", DbValue(record.T6Weight));
        command.Parameters.AddWithValue("$adj_factor", DbValue(record.AdjFactor));
        command.Parameters.AddWithValue("$enabled", record.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$created_at", string.IsNullOrWhiteSpace(record.CreatedAt) ? now : record.CreatedAt);
        command.Parameters.AddWithValue("$updated_at", record.UpdatedAt);
        record.Id = Convert.ToInt64(command.ExecuteScalar());
    }

    public void DeleteStrategyConfig(long id) => DeleteById("strategy_config", id);

    public AccountStateRecord? ReadLatestAccountState()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, principal, cash_balance, total_assets, base_position_ratio,
                   sniper_pool_amount, memo, updated_at
            FROM account_state
            ORDER BY updated_at DESC, id DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new AccountStateRecord
        {
            Id = reader.GetInt64(0),
            Principal = reader.GetDouble(1),
            CashBalance = reader.GetDouble(2),
            TotalAssets = reader.GetDouble(3),
            BasePositionRatio = reader.GetDouble(4),
            SniperPoolAmount = reader.GetDouble(5),
            Memo = OptionalString(reader, 6),
            UpdatedAt = reader.GetString(7)
        };
    }

    public void SaveAccountState(AccountStateRecord record)
    {
        string now = LocalDatabase.NowText();
        record.UpdatedAt = now;
        record.Memo = NullIfWhiteSpace(record.Memo);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        if (record.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO account_state(principal, cash_balance, total_assets, base_position_ratio, sniper_pool_amount, memo, updated_at)
                VALUES($principal, $cash_balance, $total_assets, $base_position_ratio, $sniper_pool_amount, $memo, $updated_at);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE account_state
                SET principal = $principal,
                    cash_balance = $cash_balance,
                    total_assets = $total_assets,
                    base_position_ratio = $base_position_ratio,
                    sniper_pool_amount = $sniper_pool_amount,
                    memo = $memo,
                    updated_at = $updated_at
                WHERE id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", record.Id);
        }

        command.Parameters.AddWithValue("$principal", record.Principal);
        command.Parameters.AddWithValue("$cash_balance", record.CashBalance);
        command.Parameters.AddWithValue("$total_assets", record.TotalAssets);
        command.Parameters.AddWithValue("$base_position_ratio", record.BasePositionRatio);
        command.Parameters.AddWithValue("$sniper_pool_amount", record.SniperPoolAmount);
        command.Parameters.AddWithValue("$memo", DbValue(record.Memo));
        command.Parameters.AddWithValue("$updated_at", record.UpdatedAt);
        record.Id = Convert.ToInt64(command.ExecuteScalar());
    }

    public IReadOnlyList<PositionStateRecord> ReadPositionStates()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, strategy_code, actual_code, source, quantity, cost_amount, adj_factor, created_at, updated_at
            FROM position_state
            ORDER BY strategy_code COLLATE NOCASE, actual_code COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<PositionStateRecord>();
        while (reader.Read())
        {
            records.Add(new PositionStateRecord
            {
                Id = reader.GetInt64(0),
                StrategyCode = reader.GetString(1),
                ActualCode = reader.GetString(2),
                Source = reader.GetString(3),
                Quantity = reader.GetDouble(4),
                CostAmount = reader.GetDouble(5),
                AdjFactor = reader.GetDouble(6),
                CreatedAt = reader.GetString(7),
                UpdatedAt = reader.GetString(8)
            });
        }

        return records;
    }

    public void SavePositionState(PositionStateRecord record)
    {
        RequireText(record.StrategyCode, "策略代码");
        RequireText(record.ActualCode, "实际代码");
        RequireAllowed(record.Source, "来源", "场内ETF", "场外替代");
        string now = LocalDatabase.NowText();
        record.StrategyCode = record.StrategyCode.Trim();
        record.ActualCode = record.ActualCode.Trim();

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        if (record.Id <= 0)
        {
            record.CreatedAt = now;
            record.UpdatedAt = now;
            command.CommandText = """
                INSERT INTO position_state(strategy_code, actual_code, source, quantity, cost_amount, adj_factor, created_at, updated_at)
                VALUES($strategy_code, $actual_code, $source, $quantity, $cost_amount, $adj_factor, $created_at, $updated_at);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            record.UpdatedAt = now;
            command.CommandText = """
                UPDATE position_state
                SET strategy_code = $strategy_code,
                    actual_code = $actual_code,
                    source = $source,
                    quantity = $quantity,
                    cost_amount = $cost_amount,
                    adj_factor = $adj_factor,
                    updated_at = $updated_at
                WHERE id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", record.Id);
        }

        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$actual_code", record.ActualCode);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$quantity", record.Quantity);
        command.Parameters.AddWithValue("$cost_amount", record.CostAmount);
        command.Parameters.AddWithValue("$adj_factor", record.AdjFactor);
        command.Parameters.AddWithValue("$created_at", string.IsNullOrWhiteSpace(record.CreatedAt) ? now : record.CreatedAt);
        command.Parameters.AddWithValue("$updated_at", record.UpdatedAt);
        record.Id = Convert.ToInt64(command.ExecuteScalar());
    }

    public void DeletePositionState(long id) => DeleteById("position_state", id);

    public IReadOnlyList<OtcChannelRecord> ReadOtcChannels()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, strategy_code, otc_code, class_type, enabled, daily_limit, priority, min_buy, memo, created_at, updated_at
            FROM otc_channel
            ORDER BY strategy_code COLLATE NOCASE, priority, otc_code COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<OtcChannelRecord>();
        while (reader.Read())
        {
            records.Add(new OtcChannelRecord
            {
                Id = reader.GetInt64(0),
                StrategyCode = reader.GetString(1),
                OtcCode = reader.GetString(2),
                ClassType = reader.GetString(3),
                Enabled = reader.GetInt64(4) != 0,
                DailyLimit = reader.GetDouble(5),
                Priority = reader.GetInt32(6),
                MinBuy = reader.GetDouble(7),
                Memo = OptionalString(reader, 8),
                CreatedAt = reader.GetString(9),
                UpdatedAt = reader.GetString(10)
            });
        }

        return records;
    }

    public void SaveOtcChannel(OtcChannelRecord record)
    {
        RequireText(record.StrategyCode, "策略代码");
        RequireText(record.OtcCode, "场外基金代码");
        RequireAllowed(record.ClassType, "类别", "A类", "C类");
        string now = LocalDatabase.NowText();
        record.StrategyCode = record.StrategyCode.Trim();
        record.OtcCode = record.OtcCode.Trim();
        record.Memo = NullIfWhiteSpace(record.Memo);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        if (record.Id <= 0)
        {
            record.CreatedAt = now;
            record.UpdatedAt = now;
            command.CommandText = """
                INSERT INTO otc_channel(strategy_code, otc_code, class_type, enabled, daily_limit, priority, min_buy, memo, created_at, updated_at)
                VALUES($strategy_code, $otc_code, $class_type, $enabled, $daily_limit, $priority, $min_buy, $memo, $created_at, $updated_at);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            record.UpdatedAt = now;
            command.CommandText = """
                UPDATE otc_channel
                SET strategy_code = $strategy_code,
                    otc_code = $otc_code,
                    class_type = $class_type,
                    enabled = $enabled,
                    daily_limit = $daily_limit,
                    priority = $priority,
                    min_buy = $min_buy,
                    memo = $memo,
                    updated_at = $updated_at
                WHERE id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", record.Id);
        }

        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$otc_code", record.OtcCode);
        command.Parameters.AddWithValue("$class_type", record.ClassType);
        command.Parameters.AddWithValue("$enabled", record.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$daily_limit", record.DailyLimit);
        command.Parameters.AddWithValue("$priority", record.Priority);
        command.Parameters.AddWithValue("$min_buy", record.MinBuy);
        command.Parameters.AddWithValue("$memo", DbValue(record.Memo));
        command.Parameters.AddWithValue("$created_at", string.IsNullOrWhiteSpace(record.CreatedAt) ? now : record.CreatedAt);
        command.Parameters.AddWithValue("$updated_at", record.UpdatedAt);
        record.Id = Convert.ToInt64(command.ExecuteScalar());
    }

    public void DeleteOtcChannel(long id) => DeleteById("otc_channel", id);

    public IReadOnlyList<TradeLogRecord> ReadTradeLogs()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, time, strategy_code, actual_code, action, price, quantity, amount, tier, source,
                   fee, memo, net_cash_impact, principal, cash_balance, total_assets
            FROM trade_log
            ORDER BY time DESC, id DESC;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<TradeLogRecord>();
        while (reader.Read())
        {
            records.Add(new TradeLogRecord
            {
                Id = reader.GetInt64(0),
                Time = reader.GetString(1),
                StrategyCode = reader.GetString(2),
                ActualCode = OptionalString(reader, 3),
                Action = reader.GetString(4),
                Price = reader.GetDouble(5),
                Quantity = reader.GetDouble(6),
                Amount = reader.GetDouble(7),
                Tier = OptionalString(reader, 8),
                Source = OptionalString(reader, 9),
                Fee = reader.GetDouble(10),
                Memo = OptionalString(reader, 11),
                NetCashImpact = reader.GetDouble(12),
                Principal = reader.GetDouble(13),
                CashBalance = reader.GetDouble(14),
                TotalAssets = reader.GetDouble(15)
            });
        }

        return records;
    }

    public void SaveTradeLog(TradeLogRecord record)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        SaveTradeLog(connection, transaction, record);
        transaction.Commit();
    }

    public void SaveTradeLogsSnapshot(IEnumerable<long> idsToDelete, IEnumerable<TradeLogRecord> records)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (long id in idsToDelete.Distinct().Where(id => id > 0))
        {
            DeleteById(connection, transaction, "trade_log", id);
        }

        foreach (TradeLogRecord record in records)
        {
            SaveTradeLog(connection, transaction, record);
        }

        transaction.Commit();
    }

    private static void SaveTradeLog(SqliteConnection connection, SqliteTransaction transaction, TradeLogRecord record)
    {
        RequireText(record.Time, "时间");
        RequireText(record.StrategyCode, "策略代码");
        RequireAllowed(record.Action, "动作", "买入", "卖出", "分红", "送股", "拆分", "合并", "除权校准", "CASH", "入金", "出金");
        record.Time = record.Time.Trim();
        record.StrategyCode = record.StrategyCode.Trim();
        record.ActualCode = NullIfWhiteSpace(record.ActualCode);
        record.Tier = NullIfWhiteSpace(record.Tier);
        record.Source = NullIfWhiteSpace(record.Source);
        record.Memo = NullIfWhiteSpace(record.Memo);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (record.Id <= 0)
        {
            command.CommandText = """
                INSERT INTO trade_log(time, strategy_code, actual_code, action, price, quantity, amount, tier, source,
                                      fee, memo, net_cash_impact, principal, cash_balance, total_assets)
                VALUES($time, $strategy_code, $actual_code, $action, $price, $quantity, $amount, $tier, $source,
                       $fee, $memo, $net_cash_impact, $principal, $cash_balance, $total_assets);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE trade_log
                SET time = $time,
                    strategy_code = $strategy_code,
                    actual_code = $actual_code,
                    action = $action,
                    price = $price,
                    quantity = $quantity,
                    amount = $amount,
                    tier = $tier,
                    source = $source,
                    fee = $fee,
                    memo = $memo,
                    net_cash_impact = $net_cash_impact,
                    principal = $principal,
                    cash_balance = $cash_balance,
                    total_assets = $total_assets
                WHERE id = $id;
                SELECT $id;
                """;
            command.Parameters.AddWithValue("$id", record.Id);
        }

        command.Parameters.AddWithValue("$time", record.Time);
        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$actual_code", DbValue(record.ActualCode));
        command.Parameters.AddWithValue("$action", record.Action);
        command.Parameters.AddWithValue("$price", record.Price);
        command.Parameters.AddWithValue("$quantity", record.Quantity);
        command.Parameters.AddWithValue("$amount", record.Amount);
        command.Parameters.AddWithValue("$tier", DbValue(record.Tier));
        command.Parameters.AddWithValue("$source", DbValue(record.Source));
        command.Parameters.AddWithValue("$fee", record.Fee);
        command.Parameters.AddWithValue("$memo", DbValue(record.Memo));
        command.Parameters.AddWithValue("$net_cash_impact", record.NetCashImpact);
        command.Parameters.AddWithValue("$principal", record.Principal);
        command.Parameters.AddWithValue("$cash_balance", record.CashBalance);
        command.Parameters.AddWithValue("$total_assets", record.TotalAssets);
        record.Id = Convert.ToInt64(command.ExecuteScalar());
    }

    public void DeleteTradeLog(long id) => DeleteById("trade_log", id);

    public void WriteRuntimeLog(string level, string module, string message, string? detail = null)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runtime_log(time, level, module, message, detail)
            VALUES($time, $level, $module, $message, $detail);
            """;
        command.Parameters.AddWithValue("$time", LocalDatabase.NowText());
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$module", module);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$detail", DbValue(detail));
        command.ExecuteNonQuery();
    }

    public BasePositionSettings ReadBasePositionSettings()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key, value
            FROM app_settings
            WHERE key IN ('base_position_mode', 'base_position_ratio', 'base_position_amount');
            """;

        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            values[reader.GetString(0)] = OptionalString(reader, 1);
        }

        string mode = values.TryGetValue("base_position_mode", out string? rawMode)
                      && string.Equals(rawMode, BasePositionSettings.AmountMode, StringComparison.OrdinalIgnoreCase)
            ? BasePositionSettings.AmountMode
            : BasePositionSettings.RatioMode;
        double ratio = PercentValueParser.TryParsePercentInput(values.GetValueOrDefault("base_position_ratio"), out double? parsedRatio, out _)
            ? parsedRatio ?? BasePositionSettings.DefaultRatio
            : BasePositionSettings.DefaultRatio;
        double amount = TryParseSettingDouble(values.GetValueOrDefault("base_position_amount"), out double parsedAmount)
            ? parsedAmount
            : 0;

        return BasePositionSettingsService.Normalize(new BasePositionSettings
        {
            Mode = mode,
            Ratio = ratio,
            FixedAmount = amount
        });
    }

    public void SaveBasePositionSettings(BasePositionSettings settings)
    {
        BasePositionSettings normalized = BasePositionSettingsService.Normalize(settings);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertAppSetting(connection, transaction, "base_position_mode", normalized.Mode);
        UpsertAppSetting(connection, transaction, "base_position_ratio", normalized.Ratio.ToString("R", CultureInfo.InvariantCulture));
        UpsertAppSetting(connection, transaction, "base_position_amount", normalized.FixedAmount.ToString("R", CultureInfo.InvariantCulture));
        transaction.Commit();
    }

    public string? ReadAppSetting(string key)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT value
            FROM app_settings
            WHERE key = $key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", key);
        object? value = command.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public void SaveAppSetting(string key, string value)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertAppSetting(connection, transaction, key, value);
        transaction.Commit();
    }

    public HotkeySettings ReadHotkeySettings()
    {
        return HotkeySettings.FromStoredValues(
            ReadAppSetting(HotkeySettings.EnabledSettingKey),
            ReadAppSetting(HotkeySettings.ModifiersSettingKey),
            ReadAppSetting(HotkeySettings.KeySettingKey));
    }

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        if (!settings.TryValidate(out string? error))
        {
            throw new InvalidOperationException(error ?? "快捷键设置无效。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertAppSetting(connection, transaction, HotkeySettings.EnabledSettingKey, settings.Enabled ? "true" : "false");
        UpsertAppSetting(connection, transaction, HotkeySettings.ModifiersSettingKey, HotkeySettings.FormatModifiers(settings.Modifiers));
        UpsertAppSetting(connection, transaction, HotkeySettings.KeySettingKey, HotkeySettings.FormatKey(settings.Key));
        transaction.Commit();
    }

    public AlertSettings ReadAlertSettings()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key, value
            FROM app_settings
            WHERE key IN (
                'alert_pushplus_enabled',
                'alert_pushplus_token',
                'alert_voice_enabled',
                'alert_repeat_interval_minutes',
                'alert_severe_interval_minutes',
                'alert_market_interval_minutes'
            );
            """;

        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            values[reader.GetString(0)] = OptionalString(reader, 1);
        }

        return AlertSettings.FromStoredValues(values);
    }

    public void SaveAlertSettings(AlertSettings settings)
    {
        AlertSettings normalized = AlertSettings.Normalize(settings);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertAppSetting(connection, transaction, AlertSettings.PushPlusEnabledKey, normalized.PushPlusEnabled ? "true" : "false");
        UpsertAppSetting(connection, transaction, AlertSettings.PushPlusTokenKey, normalized.PushPlusToken);
        UpsertAppSetting(connection, transaction, AlertSettings.VoiceEnabledKey, normalized.VoiceEnabled ? "true" : "false");
        UpsertAppSetting(connection, transaction, AlertSettings.RepeatIntervalMinutesKey, normalized.RepeatIntervalMinutes.ToString(CultureInfo.InvariantCulture));
        UpsertAppSetting(connection, transaction, AlertSettings.SevereIntervalMinutesKey, normalized.SevereIntervalMinutes.ToString(CultureInfo.InvariantCulture));
        UpsertAppSetting(connection, transaction, AlertSettings.MarketIntervalMinutesKey, normalized.MarketIntervalMinutes.ToString(CultureInfo.InvariantCulture));
        transaction.Commit();
    }

    public IReadOnlyList<StrategyDecisionStateRecord> ReadStrategyDecisionStates()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, calculated_at, strategy_code, name, action_instruction, strategy_status,
                   preferred_source, target_tier, target_amount, available_cash, suggested_price,
                   premium, return_rate, etf_drawdown, index_drawdown, base_mode, base_ratio,
                   base_fixed_amount, base_target_amount, base_current_cost, base_completion_rate,
                   base_gap_amount, base_target_capped, real_sniper_pool, tier_total_parts,
                   tier_cumulative_target, tier_executed_amount, tier_remain_amount,
                   prerequisite_status, prerequisite_message, is_actionable
            FROM strategy_decision_state
            ORDER BY strategy_code COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<StrategyDecisionStateRecord>();
        while (reader.Read())
        {
            records.Add(ReadStrategyDecisionState(reader));
        }

        return records;
    }

    public void SaveStrategyDecisionStates(IEnumerable<StrategyDecisionStateRecord> records)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(connection, transaction, "DELETE FROM strategy_decision_state;");
        foreach (StrategyDecisionStateRecord record in records)
        {
            InsertStrategyDecisionState(connection, transaction, record);
        }

        transaction.Commit();
    }

    public IReadOnlyList<OrderDraftStateRecord> ReadOrderDraftStates()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, draft_key, calculated_at, snapshot_key, strategy_code, name, action_instruction,
                   side, source, target_tier, target_amount, price, quantity, amount, draft_status,
                   reason, is_executable
            FROM order_draft_state
            ORDER BY is_executable DESC, amount DESC, strategy_code COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<OrderDraftStateRecord>();
        while (reader.Read())
        {
            records.Add(ReadOrderDraftState(reader));
        }

        return records;
    }

    public IReadOnlyList<OrderDraftLegStateRecord> ReadOrderDraftLegStates()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, draft_id, draft_key, calculated_at, snapshot_key, strategy_code, actual_code,
                   side, source, channel_class, priority, price, nav, quantity, amount, leg_status, reason
            FROM order_draft_leg_state
            ORDER BY draft_id, priority, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<OrderDraftLegStateRecord>();
        while (reader.Read())
        {
            records.Add(ReadOrderDraftLegState(reader));
        }

        return records;
    }

    public void SaveOrderDraftStates(IEnumerable<OrderDraftStateRecord> drafts, IEnumerable<OrderDraftLegStateRecord> legs)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(connection, transaction, "DELETE FROM order_draft_leg_state;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM order_draft_state;");

        var draftIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (OrderDraftStateRecord draft in drafts)
        {
            long id = InsertOrderDraftState(connection, transaction, draft);
            draft.Id = id;
            draftIds[draft.DraftKey] = id;
        }

        foreach (OrderDraftLegStateRecord leg in legs)
        {
            if (draftIds.TryGetValue(leg.DraftKey, out long draftId))
            {
                leg.DraftId = draftId;
            }

            InsertOrderDraftLegState(connection, transaction, leg);
        }

        transaction.Commit();
    }

    public IReadOnlyList<OrderFinalizationStateRecord> ReadOrderFinalizationStates(int limit = 20)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, finalized_at, draft_calculated_at, draft_key, snapshot_key, strategy_code, name,
                   action_instruction, side, source, target_tier, target_amount, price, quantity, amount,
                   finalization_status, reason, memo
            FROM order_finalization_state
            ORDER BY finalized_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        using var reader = command.ExecuteReader();
        var records = new List<OrderFinalizationStateRecord>();
        while (reader.Read())
        {
            records.Add(ReadOrderFinalizationState(reader));
        }

        return records;
    }

    public IReadOnlyList<OrderFinalizationLegStateRecord> ReadOrderFinalizationLegStates(int limit = 100)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, finalization_id, finalized_at, draft_key, snapshot_key, strategy_code, actual_code,
                   side, source, channel_class, priority, price, nav, quantity, amount, leg_status, reason
            FROM order_finalization_leg_state
            ORDER BY finalized_at DESC, finalization_id DESC, priority, id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        using var reader = command.ExecuteReader();
        var records = new List<OrderFinalizationLegStateRecord>();
        while (reader.Read())
        {
            records.Add(ReadOrderFinalizationLegState(reader));
        }

        return records;
    }

    public int FinalizeOrderDrafts(IEnumerable<OrderDraftStateRecord> drafts, IEnumerable<OrderDraftLegStateRecord> legs, string? memo = null)
    {
        OrderDraftStateRecord[] executableDrafts = drafts
            .Where(draft => draft.IsExecutable && draft.Amount > 0)
            .ToArray();
        if (executableDrafts.Length == 0)
        {
            return 0;
        }

        var legsByDraft = legs
            .Where(leg => leg.Amount > 0)
            .GroupBy(leg => leg.DraftKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        string finalizedAt = LocalDatabase.NowText();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        int count = 0;

        foreach (OrderDraftStateRecord draft in executableDrafts)
        {
            long finalizationId = InsertOrderFinalizationState(connection, transaction, draft, finalizedAt, memo);
            if (legsByDraft.TryGetValue(draft.DraftKey, out OrderDraftLegStateRecord[]? draftLegs))
            {
                foreach (OrderDraftLegStateRecord leg in draftLegs)
                {
                    InsertOrderFinalizationLegState(connection, transaction, leg, finalizationId, finalizedAt);
                }
            }

            count++;
        }

        transaction.Commit();
        return count;
    }

    public IReadOnlyList<MarketQuoteRecord> ReadMarketQuoteCache()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, symbol, display_name, market_type, source, price, last_close, change_value, change_percent,
                   high_value, low_value, open_value, volume, amount, iopv, quote_time, received_at, raw_code, raw_payload
            FROM market_quote_cache
            ORDER BY market_type, symbol;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<MarketQuoteRecord>();
        while (reader.Read())
        {
            records.Add(ReadMarketQuote(reader));
        }

        return records;
    }

    public void SaveMarketQuotes(IEnumerable<MarketQuoteRecord> records)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (MarketQuoteRecord record in records)
        {
            SaveMarketQuote(connection, transaction, record);
        }
        transaction.Commit();
    }

    public void SaveMarketQuote(MarketQuoteRecord record)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        SaveMarketQuote(connection, transaction, record);
        transaction.Commit();
    }

    public IReadOnlyList<MarketSourceStatusRecord> ReadMarketSourceStatuses()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source, status, last_success_at, last_failure_at, failure_count, cooldown_until, last_error, updated_at
            FROM market_source_status
            ORDER BY source;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<MarketSourceStatusRecord>();
        while (reader.Read())
        {
            records.Add(new MarketSourceStatusRecord
            {
                Id = reader.GetInt64(0),
                Source = reader.GetString(1),
                Status = reader.GetString(2),
                LastSuccessAt = OptionalString(reader, 3),
                LastFailureAt = OptionalString(reader, 4),
                FailureCount = (int)reader.GetInt64(5),
                CooldownUntil = OptionalString(reader, 6),
                LastError = OptionalString(reader, 7),
                UpdatedAt = reader.GetString(8)
            });
        }

        return records;
    }

    public void SaveMarketSourceStatus(MarketSourceStatusRecord record)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO market_source_status(source, status, last_success_at, last_failure_at, failure_count, cooldown_until, last_error, updated_at)
            VALUES($source, $status, $last_success_at, $last_failure_at, $failure_count, $cooldown_until, $last_error, $updated_at)
            ON CONFLICT(source) DO UPDATE SET
                status = excluded.status,
                last_success_at = COALESCE(excluded.last_success_at, market_source_status.last_success_at),
                last_failure_at = COALESCE(excluded.last_failure_at, market_source_status.last_failure_at),
                failure_count = excluded.failure_count,
                cooldown_until = excluded.cooldown_until,
                last_error = excluded.last_error,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$status", record.Status);
        command.Parameters.AddWithValue("$last_success_at", DbValue(record.LastSuccessAt));
        command.Parameters.AddWithValue("$last_failure_at", DbValue(record.LastFailureAt));
        command.Parameters.AddWithValue("$failure_count", record.FailureCount);
        command.Parameters.AddWithValue("$cooldown_until", DbValue(record.CooldownUntil));
        command.Parameters.AddWithValue("$last_error", DbValue(record.LastError));
        command.Parameters.AddWithValue("$updated_at", string.IsNullOrWhiteSpace(record.UpdatedAt) ? LocalDatabase.NowText() : record.UpdatedAt);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<RuntimeLogRecord> ReadRecentRuntimeLogs(int limit = 100)
    {
        int effectiveLimit = Math.Clamp(limit, 1, 500);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, time, level, module, message, detail
            FROM runtime_log
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", effectiveLimit);

        using var reader = command.ExecuteReader();
        var records = new List<RuntimeLogRecord>();
        while (reader.Read())
        {
            records.Add(new RuntimeLogRecord
            {
                Id = reader.GetInt64(0),
                Time = reader.GetString(1),
                Level = reader.GetString(2),
                Module = reader.GetString(3),
                Message = reader.GetString(4),
                Detail = OptionalString(reader, 5)
            });
        }

        return records;
    }

    public IReadOnlyList<RuntimeLogRecord> ReadRuntimeLogsAfterId(long lastProcessedId, int limit = 100)
    {
        int effectiveLimit = Math.Clamp(limit, 1, 500);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, time, level, module, message, detail
            FROM runtime_log
            WHERE id > $last_processed_id
            ORDER BY id ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$last_processed_id", Math.Max(0, lastProcessedId));
        command.Parameters.AddWithValue("$limit", effectiveLimit);

        using var reader = command.ExecuteReader();
        var records = new List<RuntimeLogRecord>();
        while (reader.Read())
        {
            records.Add(new RuntimeLogRecord
            {
                Id = reader.GetInt64(0),
                Time = reader.GetString(1),
                Level = reader.GetString(2),
                Module = reader.GetString(3),
                Message = reader.GetString(4),
                Detail = OptionalString(reader, 5)
            });
        }

        return records;
    }

    public long GetMaxRuntimeLogId()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(id), 0) FROM runtime_log;";
        return Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public long? ReadRuntimeLogAlertCursor()
    {
        string? raw = ReadAppSetting(AlertSettings.RuntimeLogAlertCursorKey);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? Math.Max(0, parsed)
            : null;
    }

    public bool InitializeRuntimeLogAlertCursorIfMissing(out long cursor)
    {
        long? existing = ReadRuntimeLogAlertCursor();
        if (existing.HasValue)
        {
            cursor = existing.Value;
            return false;
        }

        cursor = GetMaxRuntimeLogId();
        SaveRuntimeLogAlertCursor(cursor);
        return true;
    }

    public void SaveRuntimeLogAlertCursor(long id)
        => SaveAppSetting(AlertSettings.RuntimeLogAlertCursorKey, Math.Max(0, id).ToString(CultureInfo.InvariantCulture));

    public AlertDeliveryStateRecord? ReadAlertDeliveryState(string dedupeKey)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT dedupe_key, last_alert_type, last_strategy_code, last_action, last_reason,
                   last_content_hash, last_sent_at, last_status, last_title, last_content
            FROM alert_delivery_state
            WHERE dedupe_key = $dedupe_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$dedupe_key", dedupeKey);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new AlertDeliveryStateRecord
            {
                DedupeKey = reader.GetString(0),
                LastAlertType = OptionalString(reader, 1),
                LastStrategyCode = OptionalString(reader, 2),
                LastAction = OptionalString(reader, 3),
                LastReason = OptionalString(reader, 4),
                LastContentHash = OptionalString(reader, 5),
                LastSentAt = OptionalString(reader, 6),
                LastStatus = OptionalString(reader, 7),
                LastTitle = OptionalString(reader, 8),
                LastContent = OptionalString(reader, 9)
            }
            : null;
    }

    public void SaveAlertDeliveryState(AlertDeliveryStateRecord record)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO alert_delivery_state(
                dedupe_key, last_alert_type, last_strategy_code, last_action, last_reason,
                last_content_hash, last_sent_at, last_status, last_title, last_content)
            VALUES(
                $dedupe_key, $last_alert_type, $last_strategy_code, $last_action, $last_reason,
                $last_content_hash, $last_sent_at, $last_status, $last_title, $last_content)
            ON CONFLICT(dedupe_key) DO UPDATE SET
                last_alert_type = excluded.last_alert_type,
                last_strategy_code = excluded.last_strategy_code,
                last_action = excluded.last_action,
                last_reason = excluded.last_reason,
                last_content_hash = excluded.last_content_hash,
                last_sent_at = excluded.last_sent_at,
                last_status = excluded.last_status,
                last_title = excluded.last_title,
                last_content = excluded.last_content;
            """;
        command.Parameters.AddWithValue("$dedupe_key", record.DedupeKey);
        command.Parameters.AddWithValue("$last_alert_type", DbValue(record.LastAlertType));
        command.Parameters.AddWithValue("$last_strategy_code", DbValue(record.LastStrategyCode));
        command.Parameters.AddWithValue("$last_action", DbValue(record.LastAction));
        command.Parameters.AddWithValue("$last_reason", DbValue(record.LastReason));
        command.Parameters.AddWithValue("$last_content_hash", DbValue(record.LastContentHash));
        command.Parameters.AddWithValue("$last_sent_at", DbValue(record.LastSentAt));
        command.Parameters.AddWithValue("$last_status", DbValue(record.LastStatus));
        command.Parameters.AddWithValue("$last_title", DbValue(record.LastTitle));
        command.Parameters.AddWithValue("$last_content", DbValue(record.LastContent));
        command.ExecuteNonQuery();
    }

    public void SaveAlertLog(AlertLogRecord record)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO alert_log(
                created_at, alert_type, severity, strategy_code, actual_code, title, content, dedupe_key,
                wechat_enabled, wechat_status, wechat_error, wechat_sent_at,
                voice_enabled, voice_status, voice_error, voice_played_at, source, content_hash)
            VALUES(
                $created_at, $alert_type, $severity, $strategy_code, $actual_code, $title, $content, $dedupe_key,
                $wechat_enabled, $wechat_status, $wechat_error, $wechat_sent_at,
                $voice_enabled, $voice_status, $voice_error, $voice_played_at, $source, $content_hash);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$created_at", record.CreatedAt);
        command.Parameters.AddWithValue("$alert_type", record.AlertType);
        command.Parameters.AddWithValue("$severity", record.Severity);
        command.Parameters.AddWithValue("$strategy_code", DbValue(record.StrategyCode));
        command.Parameters.AddWithValue("$actual_code", DbValue(record.ActualCode));
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$content", record.Content);
        command.Parameters.AddWithValue("$dedupe_key", record.DedupeKey);
        command.Parameters.AddWithValue("$wechat_enabled", record.WechatEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$wechat_status", DbValue(record.WechatStatus));
        command.Parameters.AddWithValue("$wechat_error", DbValue(record.WechatError));
        command.Parameters.AddWithValue("$wechat_sent_at", DbValue(record.WechatSentAt));
        command.Parameters.AddWithValue("$voice_enabled", record.VoiceEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$voice_status", DbValue(record.VoiceStatus));
        command.Parameters.AddWithValue("$voice_error", DbValue(record.VoiceError));
        command.Parameters.AddWithValue("$voice_played_at", DbValue(record.VoicePlayedAt));
        command.Parameters.AddWithValue("$source", DbValue(record.Source));
        command.Parameters.AddWithValue("$content_hash", DbValue(record.ContentHash));
        record.Id = Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<AlertLogRecord> ReadAlertLogs(int limit = 100)
    {
        int effectiveLimit = Math.Clamp(limit, 1, 500);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, alert_type, severity, strategy_code, actual_code, title, content, dedupe_key,
                   wechat_enabled, wechat_status, wechat_error, wechat_sent_at,
                   voice_enabled, voice_status, voice_error, voice_played_at, source, content_hash
            FROM alert_log
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", effectiveLimit);

        using var reader = command.ExecuteReader();
        var records = new List<AlertLogRecord>();
        while (reader.Read())
        {
            records.Add(new AlertLogRecord
            {
                Id = reader.GetInt64(0),
                CreatedAt = reader.GetString(1),
                AlertType = reader.GetString(2),
                Severity = reader.GetString(3),
                StrategyCode = OptionalString(reader, 4),
                ActualCode = OptionalString(reader, 5),
                Title = reader.GetString(6),
                Content = reader.GetString(7),
                DedupeKey = reader.GetString(8),
                WechatEnabled = reader.GetInt64(9) != 0,
                WechatStatus = OptionalString(reader, 10),
                WechatError = OptionalString(reader, 11),
                WechatSentAt = OptionalString(reader, 12),
                VoiceEnabled = reader.GetInt64(13) != 0,
                VoiceStatus = OptionalString(reader, 14),
                VoiceError = OptionalString(reader, 15),
                VoicePlayedAt = OptionalString(reader, 16),
                Source = OptionalString(reader, 17),
                ContentHash = OptionalString(reader, 18)
            });
        }

        return records;
    }

    public void ClearAlertLogs()
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM alert_log;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public ChartIntradayCacheEntry? ReadLatestChartIntradayCache(string strategyCode)
    {
        string normalized = ChartSubscriptionService.NormalizeKey(strategyCode);
        if (normalized.Length == 0)
        {
            return null;
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload, updated_at, source
            FROM chart_intraday_cache
            WHERE strategy_code = $strategy_code
              AND (
                  (quality = $eastmoney_quality AND source = $eastmoney_source)
                  OR (quality = $tencent_quality AND source = $tencent_source)
              )
            ORDER BY trade_date DESC, updated_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$strategy_code", normalized);
        command.Parameters.AddWithValue("$eastmoney_quality", "REAL_TRENDS2");
        command.Parameters.AddWithValue("$eastmoney_source", MarketSources.EastMoneyIntraday);
        command.Parameters.AddWithValue("$tencent_quality", MarketSources.TencentIntradayQuality);
        command.Parameters.AddWithValue("$tencent_source", MarketSources.TencentIntraday);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        string payload = reader.GetString(0);
        string source = reader.GetString(2);
        IReadOnlyList<IntradayPoint> points;
        try
        {
            points = ParseChartIntradayPayload(payload, source)
                .Where(point => point.Price > 0)
                .OrderBy(point => point.Time)
                .ToArray();
        }
        catch
        {
            return null;
        }

        if (points.Count == 0)
        {
            return null;
        }

        DateTimeOffset updatedAt = TryParseDateTimeOffset(reader.GetString(1), out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.Now;
        return new ChartIntradayCacheEntry(
            points,
            new ChartDataStatus(true, "使用最近真实分时缓存", true),
            updatedAt);
    }

    public void SaveChartIntradayCache(
        string strategyCode,
        string? actualCode,
        string rawPayload,
        DateTimeOffset fetchedAt,
        string source = MarketSources.EastMoneyIntraday,
        string quality = "REAL_TRENDS2")
    {
        string normalized = ChartSubscriptionService.NormalizeKey(strategyCode);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(rawPayload))
        {
            return;
        }

        IReadOnlyList<IntradayPoint> points;
        try
        {
            points = ParseChartIntradayPayload(rawPayload, source)
                .Where(point => point.Price > 0)
                .OrderBy(point => point.Time)
                .ToArray();
        }
        catch
        {
            return;
        }

        if (points.Count == 0)
        {
            return;
        }

        string tradeDate = points.Last().Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chart_intraday_cache(
                strategy_code, actual_code, trade_date, updated_at, payload, quality, source)
            VALUES(
                $strategy_code, $actual_code, $trade_date, $updated_at, $payload, $quality, $source)
            ON CONFLICT(strategy_code, trade_date) DO UPDATE SET
                actual_code = excluded.actual_code,
                updated_at = excluded.updated_at,
                payload = excluded.payload,
                quality = excluded.quality,
                source = excluded.source;
            """;
        command.Parameters.AddWithValue("$strategy_code", normalized);
        command.Parameters.AddWithValue("$actual_code", DbValue(NullIfWhiteSpace(actualCode)));
        command.Parameters.AddWithValue("$trade_date", tradeDate);
        command.Parameters.AddWithValue("$updated_at", fetchedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$payload", rawPayload);
        command.Parameters.AddWithValue("$quality", quality);
        command.Parameters.AddWithValue("$source", source);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<MarketQuoteRecord> ReadMarketHistoryCache()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, symbol, NULL AS display_name, market_type, source, NULL AS price, NULL AS last_close,
                   NULL AS change_value, NULL AS change_percent, high_value, NULL AS low_value, NULL AS open_value,
                   NULL AS volume, NULL AS amount, NULL AS iopv, cache_date AS quote_time, updated_at AS received_at,
                   symbol AS raw_code, raw_payload
            FROM market_history_cache
            ORDER BY updated_at DESC;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<MarketQuoteRecord>();
        while (reader.Read())
        {
            records.Add(ReadMarketQuote(reader));
        }

        return records;
    }

    public MarketQuoteRecord? ReadTodayMarketHistory(string symbol, string marketType)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, symbol, NULL AS display_name, market_type, source, NULL AS price, NULL AS last_close,
                   NULL AS change_value, NULL AS change_percent, high_value, NULL AS low_value, NULL AS open_value,
                   NULL AS volume, NULL AS amount, NULL AS iopv, cache_date AS quote_time, updated_at AS received_at,
                   symbol AS raw_code, raw_payload
            FROM market_history_cache
            WHERE symbol = $symbol AND market_type = $market_type AND source = $source AND cache_date = $cache_date
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$symbol", symbol);
        command.Parameters.AddWithValue("$market_type", marketType);
        command.Parameters.AddWithValue("$source", "EASTMONEY_HISTORY");
        command.Parameters.AddWithValue("$cache_date", DateTime.Now.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMarketQuote(reader) : null;
    }

    public MarketQuoteRecord? ReadLatestMarketHistory(string symbol, string marketType)
        => ReadLatestMarketHistory(symbol, marketType, MarketSources.EastMoneyHistory);

    private MarketQuoteRecord? ReadLatestMarketHistory(string symbol, string marketType, string? source)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, symbol, NULL AS display_name, market_type, source, NULL AS price, NULL AS last_close,
                   NULL AS change_value, NULL AS change_percent, high_value, NULL AS low_value, NULL AS open_value,
                   NULL AS volume, NULL AS amount, NULL AS iopv, cache_date AS quote_time, updated_at AS received_at,
                   symbol AS raw_code, raw_payload
            FROM market_history_cache
            WHERE symbol = $symbol AND market_type = $market_type
              AND ($source IS NULL OR source = $source)
            ORDER BY cache_date DESC, updated_at DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$symbol", symbol);
        command.Parameters.AddWithValue("$market_type", marketType);
        command.Parameters.AddWithValue("$source", DbValue(source));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMarketQuote(reader) : null;
    }

    public MarketHistoryOverwriteDecision SaveMarketHistory(string symbol, string marketType, double? highValue, string rawPayload)
        => SaveMarketHistory(symbol, marketType, highValue, rawPayload, MarketSources.EastMoneyHistory);

    public MarketHistoryOverwriteDecision SaveMarketHistory(
        string symbol,
        string marketType,
        double? highValue,
        string rawPayload,
        string source)
    {
        MarketQuoteRecord? existing = ReadLatestMarketHistory(symbol, marketType, source: null);
        MarketHistoryQualityInfo? oldQuality = existing?.RawPayload is null
            ? null
            : MarketHistoryQuality.Analyze(existing.RawPayload);
        MarketHistoryQualityInfo newQuality = MarketHistoryQuality.Analyze(rawPayload);
        bool isCoreIndex = string.Equals(marketType, "INDEX", StringComparison.OrdinalIgnoreCase)
                           && IsCoreIndexHistorySymbol(symbol);
        MarketHistoryOverwriteDecision decision = MarketHistoryQuality.DecideOverwrite(symbol, oldQuality, newQuality, isCoreIndex);
        if (!decision.AllowOverwrite)
        {
            WriteMarketHistoryGuardLog("WARN", decision.Code, decision.Detail);
            return decision;
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO market_history_cache(symbol, market_type, source, high_value, cache_date, raw_payload, updated_at)
            VALUES($symbol, $market_type, $source, $high_value, $cache_date, $raw_payload, $updated_at)
            ON CONFLICT(symbol, market_type, source, cache_date) DO UPDATE SET
                high_value = excluded.high_value,
                raw_payload = excluded.raw_payload,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$symbol", symbol);
        command.Parameters.AddWithValue("$market_type", marketType);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$high_value", DbValue(highValue));
        command.Parameters.AddWithValue("$cache_date", DateTime.Now.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$raw_payload", rawPayload);
        command.Parameters.AddWithValue("$updated_at", LocalDatabase.NowText());
        command.ExecuteNonQuery();
        if (decision.Code == "HISTORY_DEGRADED_MONTHLY_FALLBACK")
        {
            WriteMarketHistoryGuardLog("WARN", decision.Code, decision.Detail);
        }

        return decision;
    }

    private static IReadOnlyList<IntradayPoint> ParseChartIntradayPayload(string payload, string source)
        => string.Equals(source, MarketSources.TencentIntraday, StringComparison.OrdinalIgnoreCase)
            ? TencentIntradayParser.ParsePoints(payload)
            : EastMoneyIntradayParser.ParsePoints(payload);

    public AccountReplayStateRecord? ReadLatestAccountReplayState()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, calculated_at, replay_status, replay_error, cash_balance, principal,
                   total_position_cost, known_market_value, total_assets, total_realized_pnl,
                   total_unrealized_pnl, total_pnl, total_return_rate, cash_ratio, position_ratio,
                   base_position_ratio, market_value_complete, last_trade_log_id
            FROM account_replay_state
            ORDER BY calculated_at DESC, id DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAccountReplayState(reader) : null;
    }

    public IReadOnlyList<AccountReplayStateRecord> ReadAccountReplayHistory(int limit = 60)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, calculated_at, replay_status, replay_error, cash_balance, principal,
                   total_position_cost, known_market_value, total_assets, total_realized_pnl,
                   total_unrealized_pnl, total_pnl, total_return_rate, cash_ratio, position_ratio,
                   base_position_ratio, market_value_complete, last_trade_log_id
            FROM account_replay_state
            ORDER BY calculated_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        using var reader = command.ExecuteReader();
        var records = new List<AccountReplayStateRecord>();
        while (reader.Read())
        {
            records.Add(ReadAccountReplayState(reader));
        }

        records.Reverse();
        return records;
    }

    public IReadOnlyList<AccountReplaySnapshotRecord> ReadAccountReplaySnapshots(int limit = 60)
    {
        using var connection = _database.OpenConnection();
        int effectiveLimit = Math.Max(1, limit);
        var snapshots = ReadAccountReplaySnapshots(connection, effectiveLimit);
        if (snapshots.Count > 0)
        {
            return snapshots;
        }

        return ReadAccountReplayStateSnapshots(connection, effectiveLimit)
            .GroupBy(SnapshotDistinctKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(snapshot => ParseSortTime(snapshot.CreatedAt))
            .ThenBy(snapshot => snapshot.Id)
            .TakeLast(effectiveLimit)
            .ToList();
    }

    public IReadOnlyList<PositionReplayStateRecord> ReadPositionReplayStates()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, calculated_at, strategy_code, actual_code, source, quantity, cost_amount,
                   average_cost, adj_factor, today_buy_quantity, today_buy_amount, market_price,
                   market_value, daily_pnl, realized_pnl, unrealized_pnl, total_pnl, return_rate,
                   quote_status
            FROM position_replay_state
            ORDER BY strategy_code COLLATE NOCASE, actual_code COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<PositionReplayStateRecord>();
        while (reader.Read())
        {
            records.Add(new PositionReplayStateRecord
            {
                Id = reader.GetInt64(0),
                CalculatedAt = reader.GetString(1),
                StrategyCode = reader.GetString(2),
                ActualCode = reader.GetString(3),
                Source = reader.GetString(4),
                Quantity = reader.GetDouble(5),
                CostAmount = reader.GetDouble(6),
                AverageCost = reader.GetDouble(7),
                AdjFactor = reader.GetDouble(8),
                TodayBuyQuantity = reader.GetDouble(9),
                TodayBuyAmount = reader.GetDouble(10),
                MarketPrice = OptionalDouble(reader, 11),
                MarketValue = OptionalDouble(reader, 12),
                DailyPnl = OptionalDouble(reader, 13),
                RealizedPnl = reader.GetDouble(14),
                UnrealizedPnl = OptionalDouble(reader, 15),
                TotalPnl = OptionalDouble(reader, 16),
                ReturnRate = OptionalDouble(reader, 17),
                QuoteStatus = reader.GetString(18)
            });
        }

        return records;
    }

    public IReadOnlyList<OtcPositionReplayStateRecord> ReadOtcPositionReplayStates()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, calculated_at, strategy_code, actual_code, quantity, cost_amount, average_cost,
                   nav, market_value, daily_pnl, unrealized_pnl, return_rate, quote_status
            FROM otc_position_replay_state
            ORDER BY strategy_code COLLATE NOCASE, actual_code COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var records = new List<OtcPositionReplayStateRecord>();
        while (reader.Read())
        {
            records.Add(new OtcPositionReplayStateRecord
            {
                Id = reader.GetInt64(0),
                CalculatedAt = reader.GetString(1),
                StrategyCode = reader.GetString(2),
                ActualCode = reader.GetString(3),
                Quantity = reader.GetDouble(4),
                CostAmount = reader.GetDouble(5),
                AverageCost = reader.GetDouble(6),
                Nav = OptionalDouble(reader, 7),
                MarketValue = OptionalDouble(reader, 8),
                DailyPnl = OptionalDouble(reader, 9),
                UnrealizedPnl = OptionalDouble(reader, 10),
                ReturnRate = OptionalDouble(reader, 11),
                QuoteStatus = reader.GetString(12)
            });
        }

        return records;
    }

    public void SaveAccountReplayResult(AccountReplayResult result)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        InsertAccountReplayState(connection, transaction, result.Account);
        InsertAccountReplaySnapshotIfNeeded(connection, transaction, result.Account);
        if (result.Account.ReplayStatus != "财务异常")
        {
            ExecuteNonQuery(connection, transaction, "DELETE FROM position_replay_state;");
            ExecuteNonQuery(connection, transaction, "DELETE FROM otc_position_replay_state;");
            foreach (PositionReplayStateRecord position in result.Positions)
            {
                InsertPositionReplayState(connection, transaction, position);
            }

            foreach (OtcPositionReplayStateRecord position in result.OtcPositions)
            {
                InsertOtcPositionReplayState(connection, transaction, position);
            }
        }

        if (result.Errors.Count > 0)
        {
            InsertRuntimeLog(connection, transaction, "ERROR", "AccountReplay", "TradeLog 回放财务异常", string.Join(Environment.NewLine, result.Errors));
        }
        else if (result.Warnings.Count > 0)
        {
            InsertRuntimeLog(connection, transaction, "WARN", "AccountReplay", "TradeLog 回放估值不完整", string.Join(Environment.NewLine, result.Warnings));
        }

        transaction.Commit();
    }

    private static IReadOnlyList<AccountReplaySnapshotRecord> ReadAccountReplaySnapshots(SqliteConnection connection, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, total_assets, total_pnl, total_unrealized_pnl,
                   cash_balance, principal, market_value_complete
            FROM account_replay_snapshot
            ORDER BY created_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var records = new List<AccountReplaySnapshotRecord>();
        while (reader.Read())
        {
            records.Add(ReadAccountReplaySnapshot(reader));
        }

        records.Reverse();
        return records;
    }

    private static IReadOnlyList<AccountReplaySnapshotRecord> ReadAccountReplayStateSnapshots(SqliteConnection connection, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, calculated_at, total_assets, total_pnl, total_unrealized_pnl,
                   cash_balance, principal, market_value_complete
            FROM account_replay_state
            WHERE replay_status <> '财务异常'
            ORDER BY calculated_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var records = new List<AccountReplaySnapshotRecord>();
        while (reader.Read())
        {
            records.Add(new AccountReplaySnapshotRecord
            {
                Id = reader.GetInt64(0),
                CreatedAt = reader.GetString(1),
                TotalAssets = OptionalDouble(reader, 2),
                TotalPnl = OptionalDouble(reader, 3),
                TotalUnrealizedPnl = OptionalDouble(reader, 4),
                CashBalance = OptionalDouble(reader, 5),
                Principal = OptionalDouble(reader, 6),
                MarketValueComplete = reader.GetInt64(7) != 0
            });
        }

        records.Reverse();
        return records;
    }

    private static AccountReplayStateRecord ReadAccountReplayState(SqliteDataReader reader)
    {
        return new AccountReplayStateRecord
        {
            Id = reader.GetInt64(0),
            CalculatedAt = reader.GetString(1),
            ReplayStatus = reader.GetString(2),
            ReplayError = OptionalString(reader, 3),
            CashBalance = OptionalDouble(reader, 4),
            Principal = OptionalDouble(reader, 5),
            TotalPositionCost = OptionalDouble(reader, 6),
            KnownMarketValue = OptionalDouble(reader, 7),
            TotalAssets = OptionalDouble(reader, 8),
            TotalRealizedPnl = OptionalDouble(reader, 9),
            TotalUnrealizedPnl = OptionalDouble(reader, 10),
            TotalPnl = OptionalDouble(reader, 11),
            TotalReturnRate = OptionalDouble(reader, 12),
            CashRatio = OptionalDouble(reader, 13),
            PositionRatio = OptionalDouble(reader, 14),
            BasePositionRatio = OptionalDouble(reader, 15),
            MarketValueComplete = reader.GetInt64(16) != 0,
            LastTradeLogId = OptionalLong(reader, 17)
        };
    }

    private static AccountReplaySnapshotRecord ReadAccountReplaySnapshot(SqliteDataReader reader)
    {
        return new AccountReplaySnapshotRecord
        {
            Id = reader.GetInt64(0),
            CreatedAt = reader.GetString(1),
            TotalAssets = OptionalDouble(reader, 2),
            TotalPnl = OptionalDouble(reader, 3),
            TotalUnrealizedPnl = OptionalDouble(reader, 4),
            CashBalance = OptionalDouble(reader, 5),
            Principal = OptionalDouble(reader, 6),
            MarketValueComplete = reader.GetInt64(7) != 0
        };
    }

    private static void InsertAccountReplayState(SqliteConnection connection, SqliteTransaction transaction, AccountReplayStateRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO account_replay_state(
                calculated_at, replay_status, replay_error, cash_balance, principal, total_position_cost,
                known_market_value, total_assets, total_realized_pnl, total_unrealized_pnl, total_pnl,
                total_return_rate, cash_ratio, position_ratio, base_position_ratio, market_value_complete,
                last_trade_log_id)
            VALUES(
                $calculated_at, $replay_status, $replay_error, $cash_balance, $principal, $total_position_cost,
                $known_market_value, $total_assets, $total_realized_pnl, $total_unrealized_pnl, $total_pnl,
                $total_return_rate, $cash_ratio, $position_ratio, $base_position_ratio, $market_value_complete,
                $last_trade_log_id);
            """;
        command.Parameters.AddWithValue("$calculated_at", string.IsNullOrWhiteSpace(record.CalculatedAt) ? LocalDatabase.NowText() : record.CalculatedAt);
        command.Parameters.AddWithValue("$replay_status", record.ReplayStatus);
        command.Parameters.AddWithValue("$replay_error", DbValue(record.ReplayError));
        command.Parameters.AddWithValue("$cash_balance", DbValue(record.CashBalance));
        command.Parameters.AddWithValue("$principal", DbValue(record.Principal));
        command.Parameters.AddWithValue("$total_position_cost", DbValue(record.TotalPositionCost));
        command.Parameters.AddWithValue("$known_market_value", DbValue(record.KnownMarketValue));
        command.Parameters.AddWithValue("$total_assets", DbValue(record.TotalAssets));
        command.Parameters.AddWithValue("$total_realized_pnl", DbValue(record.TotalRealizedPnl));
        command.Parameters.AddWithValue("$total_unrealized_pnl", DbValue(record.TotalUnrealizedPnl));
        command.Parameters.AddWithValue("$total_pnl", DbValue(record.TotalPnl));
        command.Parameters.AddWithValue("$total_return_rate", DbValue(record.TotalReturnRate));
        command.Parameters.AddWithValue("$cash_ratio", DbValue(record.CashRatio));
        command.Parameters.AddWithValue("$position_ratio", DbValue(record.PositionRatio));
        command.Parameters.AddWithValue("$base_position_ratio", DbValue(record.BasePositionRatio));
        command.Parameters.AddWithValue("$market_value_complete", record.MarketValueComplete ? 1 : 0);
        command.Parameters.AddWithValue("$last_trade_log_id", record.LastTradeLogId.HasValue ? record.LastTradeLogId.Value : DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertAccountReplaySnapshotIfNeeded(SqliteConnection connection, SqliteTransaction transaction, AccountReplayStateRecord record)
    {
        if (record.ReplayStatus == "财务异常"
            || (!HasFiniteValue(record.TotalAssets)
                && !HasFiniteValue(record.TotalPnl)
                && !HasFiniteValue(record.TotalUnrealizedPnl)))
        {
            return;
        }

        var snapshot = new AccountReplaySnapshotRecord
        {
            CreatedAt = string.IsNullOrWhiteSpace(record.CalculatedAt) ? LocalDatabase.NowText() : record.CalculatedAt,
            TotalAssets = record.TotalAssets,
            TotalPnl = record.TotalPnl,
            TotalUnrealizedPnl = record.TotalUnrealizedPnl,
            CashBalance = record.CashBalance,
            Principal = record.Principal,
            MarketValueComplete = record.MarketValueComplete
        };

        if (ShouldSkipAccountReplaySnapshot(connection, transaction, snapshot))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO account_replay_snapshot(
                created_at, total_assets, total_pnl, total_unrealized_pnl,
                cash_balance, principal, market_value_complete)
            VALUES(
                $created_at, $total_assets, $total_pnl, $total_unrealized_pnl,
                $cash_balance, $principal, $market_value_complete);
            """;
        command.Parameters.AddWithValue("$created_at", snapshot.CreatedAt);
        command.Parameters.AddWithValue("$total_assets", DbValue(snapshot.TotalAssets));
        command.Parameters.AddWithValue("$total_pnl", DbValue(snapshot.TotalPnl));
        command.Parameters.AddWithValue("$total_unrealized_pnl", DbValue(snapshot.TotalUnrealizedPnl));
        command.Parameters.AddWithValue("$cash_balance", DbValue(snapshot.CashBalance));
        command.Parameters.AddWithValue("$principal", DbValue(snapshot.Principal));
        command.Parameters.AddWithValue("$market_value_complete", snapshot.MarketValueComplete ? 1 : 0);
        command.ExecuteNonQuery();

        PruneAccountReplaySnapshots(connection, transaction);
    }

    private static bool ShouldSkipAccountReplaySnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AccountReplaySnapshotRecord snapshot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, created_at, total_assets, total_pnl, total_unrealized_pnl,
                   cash_balance, principal, market_value_complete
            FROM account_replay_snapshot
            ORDER BY created_at DESC, id DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        AccountReplaySnapshotRecord latest = ReadAccountReplaySnapshot(reader);
        return ValuesEqual(latest.TotalAssets, snapshot.TotalAssets)
               && ValuesEqual(latest.TotalPnl, snapshot.TotalPnl)
               && ValuesEqual(latest.TotalUnrealizedPnl, snapshot.TotalUnrealizedPnl)
               && ValuesEqual(latest.CashBalance, snapshot.CashBalance)
               && ValuesEqual(latest.Principal, snapshot.Principal)
               && latest.MarketValueComplete == snapshot.MarketValueComplete;
    }

    private static void PruneAccountReplaySnapshots(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM account_replay_snapshot
            WHERE id NOT IN (
                SELECT id FROM account_replay_snapshot
                ORDER BY created_at DESC, id DESC
                LIMIT $limit
            );
            """;
        command.Parameters.AddWithValue("$limit", MaxAccountReplaySnapshots);
        command.ExecuteNonQuery();
    }

    private static void InsertPositionReplayState(SqliteConnection connection, SqliteTransaction transaction, PositionReplayStateRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO position_replay_state(
                calculated_at, strategy_code, actual_code, source, quantity, cost_amount, average_cost,
                adj_factor, today_buy_quantity, today_buy_amount, market_price, market_value, daily_pnl,
                realized_pnl, unrealized_pnl, total_pnl, return_rate, quote_status)
            VALUES(
                $calculated_at, $strategy_code, $actual_code, $source, $quantity, $cost_amount, $average_cost,
                $adj_factor, $today_buy_quantity, $today_buy_amount, $market_price, $market_value, $daily_pnl,
                $realized_pnl, $unrealized_pnl, $total_pnl, $return_rate, $quote_status);
            """;
        command.Parameters.AddWithValue("$calculated_at", record.CalculatedAt);
        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$actual_code", record.ActualCode);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$quantity", record.Quantity);
        command.Parameters.AddWithValue("$cost_amount", record.CostAmount);
        command.Parameters.AddWithValue("$average_cost", record.AverageCost);
        command.Parameters.AddWithValue("$adj_factor", record.AdjFactor);
        command.Parameters.AddWithValue("$today_buy_quantity", record.TodayBuyQuantity);
        command.Parameters.AddWithValue("$today_buy_amount", record.TodayBuyAmount);
        command.Parameters.AddWithValue("$market_price", DbValue(record.MarketPrice));
        command.Parameters.AddWithValue("$market_value", DbValue(record.MarketValue));
        command.Parameters.AddWithValue("$daily_pnl", DbValue(record.DailyPnl));
        command.Parameters.AddWithValue("$realized_pnl", record.RealizedPnl);
        command.Parameters.AddWithValue("$unrealized_pnl", DbValue(record.UnrealizedPnl));
        command.Parameters.AddWithValue("$total_pnl", DbValue(record.TotalPnl));
        command.Parameters.AddWithValue("$return_rate", DbValue(record.ReturnRate));
        command.Parameters.AddWithValue("$quote_status", record.QuoteStatus);
        command.ExecuteNonQuery();
    }

    private static void InsertOtcPositionReplayState(SqliteConnection connection, SqliteTransaction transaction, OtcPositionReplayStateRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO otc_position_replay_state(
                calculated_at, strategy_code, actual_code, quantity, cost_amount, average_cost,
                nav, market_value, daily_pnl, unrealized_pnl, return_rate, quote_status)
            VALUES(
                $calculated_at, $strategy_code, $actual_code, $quantity, $cost_amount, $average_cost,
                $nav, $market_value, $daily_pnl, $unrealized_pnl, $return_rate, $quote_status);
            """;
        command.Parameters.AddWithValue("$calculated_at", record.CalculatedAt);
        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$actual_code", record.ActualCode);
        command.Parameters.AddWithValue("$quantity", record.Quantity);
        command.Parameters.AddWithValue("$cost_amount", record.CostAmount);
        command.Parameters.AddWithValue("$average_cost", record.AverageCost);
        command.Parameters.AddWithValue("$nav", DbValue(record.Nav));
        command.Parameters.AddWithValue("$market_value", DbValue(record.MarketValue));
        command.Parameters.AddWithValue("$daily_pnl", DbValue(record.DailyPnl));
        command.Parameters.AddWithValue("$unrealized_pnl", DbValue(record.UnrealizedPnl));
        command.Parameters.AddWithValue("$return_rate", DbValue(record.ReturnRate));
        command.Parameters.AddWithValue("$quote_status", record.QuoteStatus);
        command.ExecuteNonQuery();
    }

    private static void InsertRuntimeLog(SqliteConnection connection, SqliteTransaction transaction, string level, string module, string message, string detail)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO runtime_log(time, level, module, message, detail)
            VALUES($time, $level, $module, $message, $detail);
            """;
        command.Parameters.AddWithValue("$time", LocalDatabase.NowText());
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$module", module);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$detail", detail);
        command.ExecuteNonQuery();
    }

    private static void UpsertAppSetting(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_settings(key, value, updated_at)
            VALUES($key, $value, $updated_at)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updated_at", LocalDatabase.NowText());
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void InsertStrategyDecisionState(SqliteConnection connection, SqliteTransaction transaction, StrategyDecisionStateRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO strategy_decision_state(
                calculated_at, strategy_code, name, action_instruction, strategy_status,
                preferred_source, target_tier, target_amount, available_cash, suggested_price,
                premium, return_rate, etf_drawdown, index_drawdown, base_mode, base_ratio,
                base_fixed_amount, base_target_amount, base_current_cost, base_completion_rate,
                base_gap_amount, base_target_capped, real_sniper_pool, tier_total_parts,
                tier_cumulative_target, tier_executed_amount, tier_remain_amount,
                prerequisite_status, prerequisite_message, is_actionable)
            VALUES(
                $calculated_at, $strategy_code, $name, $action_instruction, $strategy_status,
                $preferred_source, $target_tier, $target_amount, $available_cash, $suggested_price,
                $premium, $return_rate, $etf_drawdown, $index_drawdown, $base_mode, $base_ratio,
                $base_fixed_amount, $base_target_amount, $base_current_cost, $base_completion_rate,
                $base_gap_amount, $base_target_capped, $real_sniper_pool, $tier_total_parts,
                $tier_cumulative_target, $tier_executed_amount, $tier_remain_amount,
                $prerequisite_status, $prerequisite_message, $is_actionable);
            """;
        command.Parameters.AddWithValue("$calculated_at", string.IsNullOrWhiteSpace(record.CalculatedAt) ? LocalDatabase.NowText() : record.CalculatedAt);
        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$name", DbValue(record.Name));
        command.Parameters.AddWithValue("$action_instruction", DbValue(record.ActionInstruction));
        command.Parameters.AddWithValue("$strategy_status", DbValue(record.StrategyStatus));
        command.Parameters.AddWithValue("$preferred_source", DbValue(record.PreferredSource));
        command.Parameters.AddWithValue("$target_tier", DbValue(record.TargetTier));
        command.Parameters.AddWithValue("$target_amount", DbValue(record.TargetAmount));
        command.Parameters.AddWithValue("$available_cash", DbValue(record.AvailableCash));
        command.Parameters.AddWithValue("$suggested_price", DbValue(record.SuggestedPrice));
        command.Parameters.AddWithValue("$premium", DbValue(record.Premium));
        command.Parameters.AddWithValue("$return_rate", DbValue(record.ReturnRate));
        command.Parameters.AddWithValue("$etf_drawdown", DbValue(record.EtfDrawdown));
        command.Parameters.AddWithValue("$index_drawdown", DbValue(record.IndexDrawdown));
        command.Parameters.AddWithValue("$base_mode", DbValue(record.BaseMode));
        command.Parameters.AddWithValue("$base_ratio", DbValue(record.BaseRatio));
        command.Parameters.AddWithValue("$base_fixed_amount", DbValue(record.BaseFixedAmount));
        command.Parameters.AddWithValue("$base_target_amount", DbValue(record.BaseTargetAmount));
        command.Parameters.AddWithValue("$base_current_cost", DbValue(record.BaseCurrentCost));
        command.Parameters.AddWithValue("$base_completion_rate", DbValue(record.BaseCompletionRate));
        command.Parameters.AddWithValue("$base_gap_amount", DbValue(record.BaseGapAmount));
        command.Parameters.AddWithValue("$base_target_capped", record.BaseTargetCapped ? 1 : 0);
        command.Parameters.AddWithValue("$real_sniper_pool", DbValue(record.RealSniperPool));
        command.Parameters.AddWithValue("$tier_total_parts", DbValue(record.TierTotalParts));
        command.Parameters.AddWithValue("$tier_cumulative_target", DbValue(record.TierCumulativeTarget));
        command.Parameters.AddWithValue("$tier_executed_amount", DbValue(record.TierExecutedAmount));
        command.Parameters.AddWithValue("$tier_remain_amount", DbValue(record.TierRemainAmount));
        command.Parameters.AddWithValue("$prerequisite_status", DbValue(record.PrerequisiteStatus));
        command.Parameters.AddWithValue("$prerequisite_message", DbValue(record.PrerequisiteMessage));
        command.Parameters.AddWithValue("$is_actionable", record.IsActionable ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static long InsertOrderDraftState(SqliteConnection connection, SqliteTransaction transaction, OrderDraftStateRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO order_draft_state(
                draft_key, calculated_at, snapshot_key, strategy_code, name, action_instruction,
                side, source, target_tier, target_amount, price, quantity, amount, draft_status,
                reason, is_executable)
            VALUES(
                $draft_key, $calculated_at, $snapshot_key, $strategy_code, $name, $action_instruction,
                $side, $source, $target_tier, $target_amount, $price, $quantity, $amount, $draft_status,
                $reason, $is_executable);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$draft_key", record.DraftKey);
        command.Parameters.AddWithValue("$calculated_at", string.IsNullOrWhiteSpace(record.CalculatedAt) ? LocalDatabase.NowText() : record.CalculatedAt);
        command.Parameters.AddWithValue("$snapshot_key", record.SnapshotKey);
        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$name", DbValue(record.Name));
        command.Parameters.AddWithValue("$action_instruction", DbValue(record.ActionInstruction));
        command.Parameters.AddWithValue("$side", record.Side);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$target_tier", DbValue(record.TargetTier));
        command.Parameters.AddWithValue("$target_amount", DbValue(record.TargetAmount));
        command.Parameters.AddWithValue("$price", DbValue(record.Price));
        command.Parameters.AddWithValue("$quantity", record.Quantity);
        command.Parameters.AddWithValue("$amount", record.Amount);
        command.Parameters.AddWithValue("$draft_status", record.DraftStatus);
        command.Parameters.AddWithValue("$reason", DbValue(record.Reason));
        command.Parameters.AddWithValue("$is_executable", record.IsExecutable ? 1 : 0);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void InsertOrderDraftLegState(SqliteConnection connection, SqliteTransaction transaction, OrderDraftLegStateRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO order_draft_leg_state(
                draft_id, draft_key, calculated_at, snapshot_key, strategy_code, actual_code,
                side, source, channel_class, priority, price, nav, quantity, amount, leg_status, reason)
            VALUES(
                $draft_id, $draft_key, $calculated_at, $snapshot_key, $strategy_code, $actual_code,
                $side, $source, $channel_class, $priority, $price, $nav, $quantity, $amount, $leg_status, $reason);
            """;
        command.Parameters.AddWithValue("$draft_id", record.DraftId);
        command.Parameters.AddWithValue("$draft_key", record.DraftKey);
        command.Parameters.AddWithValue("$calculated_at", string.IsNullOrWhiteSpace(record.CalculatedAt) ? LocalDatabase.NowText() : record.CalculatedAt);
        command.Parameters.AddWithValue("$snapshot_key", record.SnapshotKey);
        command.Parameters.AddWithValue("$strategy_code", record.StrategyCode);
        command.Parameters.AddWithValue("$actual_code", DbValue(record.ActualCode));
        command.Parameters.AddWithValue("$side", record.Side);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$channel_class", DbValue(record.ChannelClass));
        command.Parameters.AddWithValue("$priority", record.Priority.HasValue ? record.Priority.Value : DBNull.Value);
        command.Parameters.AddWithValue("$price", DbValue(record.Price));
        command.Parameters.AddWithValue("$nav", DbValue(record.Nav));
        command.Parameters.AddWithValue("$quantity", record.Quantity);
        command.Parameters.AddWithValue("$amount", record.Amount);
        command.Parameters.AddWithValue("$leg_status", record.LegStatus);
        command.Parameters.AddWithValue("$reason", DbValue(record.Reason));
        command.ExecuteNonQuery();
    }

    private static long InsertOrderFinalizationState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OrderDraftStateRecord draft,
        string finalizedAt,
        string? memo)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO order_finalization_state(
                finalized_at, draft_calculated_at, draft_key, snapshot_key, strategy_code, name,
                action_instruction, side, source, target_tier, target_amount, price, quantity, amount,
                finalization_status, reason, memo)
            VALUES(
                $finalized_at, $draft_calculated_at, $draft_key, $snapshot_key, $strategy_code, $name,
                $action_instruction, $side, $source, $target_tier, $target_amount, $price, $quantity, $amount,
                $finalization_status, $reason, $memo);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$finalized_at", finalizedAt);
        command.Parameters.AddWithValue("$draft_calculated_at", draft.CalculatedAt);
        command.Parameters.AddWithValue("$draft_key", draft.DraftKey);
        command.Parameters.AddWithValue("$snapshot_key", draft.SnapshotKey);
        command.Parameters.AddWithValue("$strategy_code", draft.StrategyCode);
        command.Parameters.AddWithValue("$name", DbValue(draft.Name));
        command.Parameters.AddWithValue("$action_instruction", DbValue(draft.ActionInstruction));
        command.Parameters.AddWithValue("$side", draft.Side);
        command.Parameters.AddWithValue("$source", draft.Source);
        command.Parameters.AddWithValue("$target_tier", DbValue(draft.TargetTier));
        command.Parameters.AddWithValue("$target_amount", DbValue(draft.TargetAmount));
        command.Parameters.AddWithValue("$price", DbValue(draft.Price));
        command.Parameters.AddWithValue("$quantity", draft.Quantity);
        command.Parameters.AddWithValue("$amount", draft.Amount);
        command.Parameters.AddWithValue("$finalization_status", "已定稿");
        command.Parameters.AddWithValue("$reason", DbValue(draft.Reason));
        command.Parameters.AddWithValue("$memo", DbValue(memo));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void InsertOrderFinalizationLegState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OrderDraftLegStateRecord leg,
        long finalizationId,
        string finalizedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO order_finalization_leg_state(
                finalization_id, finalized_at, draft_key, snapshot_key, strategy_code, actual_code,
                side, source, channel_class, priority, price, nav, quantity, amount, leg_status, reason)
            VALUES(
                $finalization_id, $finalized_at, $draft_key, $snapshot_key, $strategy_code, $actual_code,
                $side, $source, $channel_class, $priority, $price, $nav, $quantity, $amount, $leg_status, $reason);
            """;
        command.Parameters.AddWithValue("$finalization_id", finalizationId);
        command.Parameters.AddWithValue("$finalized_at", finalizedAt);
        command.Parameters.AddWithValue("$draft_key", leg.DraftKey);
        command.Parameters.AddWithValue("$snapshot_key", leg.SnapshotKey);
        command.Parameters.AddWithValue("$strategy_code", leg.StrategyCode);
        command.Parameters.AddWithValue("$actual_code", DbValue(leg.ActualCode));
        command.Parameters.AddWithValue("$side", leg.Side);
        command.Parameters.AddWithValue("$source", leg.Source);
        command.Parameters.AddWithValue("$channel_class", DbValue(leg.ChannelClass));
        command.Parameters.AddWithValue("$priority", leg.Priority.HasValue ? leg.Priority.Value : DBNull.Value);
        command.Parameters.AddWithValue("$price", DbValue(leg.Price));
        command.Parameters.AddWithValue("$nav", DbValue(leg.Nav));
        command.Parameters.AddWithValue("$quantity", leg.Quantity);
        command.Parameters.AddWithValue("$amount", leg.Amount);
        command.Parameters.AddWithValue("$leg_status", "已定稿");
        command.Parameters.AddWithValue("$reason", DbValue(leg.Reason));
        command.ExecuteNonQuery();
    }

    private static StrategyDecisionStateRecord ReadStrategyDecisionState(SqliteDataReader reader)
    {
        return new StrategyDecisionStateRecord
        {
            Id = reader.GetInt64(0),
            CalculatedAt = reader.GetString(1),
            StrategyCode = reader.GetString(2),
            Name = OptionalString(reader, 3),
            ActionInstruction = OptionalString(reader, 4),
            StrategyStatus = OptionalString(reader, 5),
            PreferredSource = OptionalString(reader, 6),
            TargetTier = OptionalString(reader, 7),
            TargetAmount = OptionalDouble(reader, 8),
            AvailableCash = OptionalDouble(reader, 9),
            SuggestedPrice = OptionalDouble(reader, 10),
            Premium = OptionalDouble(reader, 11),
            ReturnRate = OptionalDouble(reader, 12),
            EtfDrawdown = OptionalDouble(reader, 13),
            IndexDrawdown = OptionalDouble(reader, 14),
            BaseMode = OptionalString(reader, 15),
            BaseRatio = OptionalDouble(reader, 16),
            BaseFixedAmount = OptionalDouble(reader, 17),
            BaseTargetAmount = OptionalDouble(reader, 18),
            BaseCurrentCost = OptionalDouble(reader, 19),
            BaseCompletionRate = OptionalDouble(reader, 20),
            BaseGapAmount = OptionalDouble(reader, 21),
            BaseTargetCapped = reader.GetInt64(22) != 0,
            RealSniperPool = OptionalDouble(reader, 23),
            TierTotalParts = OptionalDouble(reader, 24),
            TierCumulativeTarget = OptionalDouble(reader, 25),
            TierExecutedAmount = OptionalDouble(reader, 26),
            TierRemainAmount = OptionalDouble(reader, 27),
            PrerequisiteStatus = OptionalString(reader, 28),
            PrerequisiteMessage = OptionalString(reader, 29),
            IsActionable = reader.GetInt64(30) != 0
        };
    }

    private static OrderDraftStateRecord ReadOrderDraftState(SqliteDataReader reader)
    {
        return new OrderDraftStateRecord
        {
            Id = reader.GetInt64(0),
            DraftKey = reader.GetString(1),
            CalculatedAt = reader.GetString(2),
            SnapshotKey = reader.GetString(3),
            StrategyCode = reader.GetString(4),
            Name = OptionalString(reader, 5),
            ActionInstruction = OptionalString(reader, 6),
            Side = reader.GetString(7),
            Source = reader.GetString(8),
            TargetTier = OptionalString(reader, 9),
            TargetAmount = OptionalDouble(reader, 10),
            Price = OptionalDouble(reader, 11),
            Quantity = reader.GetDouble(12),
            Amount = reader.GetDouble(13),
            DraftStatus = reader.GetString(14),
            Reason = OptionalString(reader, 15),
            IsExecutable = reader.GetInt64(16) != 0
        };
    }

    private static OrderDraftLegStateRecord ReadOrderDraftLegState(SqliteDataReader reader)
    {
        return new OrderDraftLegStateRecord
        {
            Id = reader.GetInt64(0),
            DraftId = reader.GetInt64(1),
            DraftKey = reader.GetString(2),
            CalculatedAt = reader.GetString(3),
            SnapshotKey = reader.GetString(4),
            StrategyCode = reader.GetString(5),
            ActualCode = OptionalString(reader, 6),
            Side = reader.GetString(7),
            Source = reader.GetString(8),
            ChannelClass = OptionalString(reader, 9),
            Priority = OptionalInt(reader, 10),
            Price = OptionalDouble(reader, 11),
            Nav = OptionalDouble(reader, 12),
            Quantity = reader.GetDouble(13),
            Amount = reader.GetDouble(14),
            LegStatus = reader.GetString(15),
            Reason = OptionalString(reader, 16)
        };
    }

    private static OrderFinalizationStateRecord ReadOrderFinalizationState(SqliteDataReader reader)
    {
        return new OrderFinalizationStateRecord
        {
            Id = reader.GetInt64(0),
            FinalizedAt = reader.GetString(1),
            DraftCalculatedAt = reader.GetString(2),
            DraftKey = reader.GetString(3),
            SnapshotKey = reader.GetString(4),
            StrategyCode = reader.GetString(5),
            Name = OptionalString(reader, 6),
            ActionInstruction = OptionalString(reader, 7),
            Side = reader.GetString(8),
            Source = reader.GetString(9),
            TargetTier = OptionalString(reader, 10),
            TargetAmount = OptionalDouble(reader, 11),
            Price = OptionalDouble(reader, 12),
            Quantity = reader.GetDouble(13),
            Amount = reader.GetDouble(14),
            FinalizationStatus = reader.GetString(15),
            Reason = OptionalString(reader, 16),
            Memo = OptionalString(reader, 17)
        };
    }

    private static OrderFinalizationLegStateRecord ReadOrderFinalizationLegState(SqliteDataReader reader)
    {
        return new OrderFinalizationLegStateRecord
        {
            Id = reader.GetInt64(0),
            FinalizationId = reader.GetInt64(1),
            FinalizedAt = reader.GetString(2),
            DraftKey = reader.GetString(3),
            SnapshotKey = reader.GetString(4),
            StrategyCode = reader.GetString(5),
            ActualCode = OptionalString(reader, 6),
            Side = reader.GetString(7),
            Source = reader.GetString(8),
            ChannelClass = OptionalString(reader, 9),
            Priority = OptionalInt(reader, 10),
            Price = OptionalDouble(reader, 11),
            Nav = OptionalDouble(reader, 12),
            Quantity = reader.GetDouble(13),
            Amount = reader.GetDouble(14),
            LegStatus = reader.GetString(15),
            Reason = OptionalString(reader, 16)
        };
    }

    private static MarketQuoteRecord ReadMarketQuote(SqliteDataReader reader)
    {
        return new MarketQuoteRecord
        {
            Id = reader.GetInt64(0),
            Symbol = reader.GetString(1),
            DisplayName = OptionalString(reader, 2),
            MarketType = reader.GetString(3),
            Source = reader.GetString(4),
            Price = OptionalDouble(reader, 5),
            LastClose = OptionalDouble(reader, 6),
            ChangeValue = OptionalDouble(reader, 7),
            ChangePercent = OptionalDouble(reader, 8),
            HighValue = OptionalDouble(reader, 9),
            LowValue = OptionalDouble(reader, 10),
            OpenValue = OptionalDouble(reader, 11),
            Volume = OptionalDouble(reader, 12),
            Amount = OptionalDouble(reader, 13),
            Iopv = OptionalDouble(reader, 14),
            QuoteTime = OptionalString(reader, 15),
            ReceivedAt = reader.GetString(16),
            RawCode = OptionalString(reader, 17),
            RawPayload = OptionalString(reader, 18)
        };
    }

    private static void SaveMarketQuote(SqliteConnection connection, SqliteTransaction transaction, MarketQuoteRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO market_quote_cache(symbol, display_name, market_type, source, price, last_close, change_value,
                                           change_percent, high_value, low_value, open_value, volume, amount, iopv,
                                           quote_time, received_at, raw_code, raw_payload)
            VALUES($symbol, $display_name, $market_type, $source, $price, $last_close, $change_value,
                   $change_percent, $high_value, $low_value, $open_value, $volume, $amount, $iopv,
                   $quote_time, $received_at, $raw_code, $raw_payload)
            ON CONFLICT(symbol, market_type, source) DO UPDATE SET
                display_name = excluded.display_name,
                price = excluded.price,
                last_close = excluded.last_close,
                change_value = excluded.change_value,
                change_percent = excluded.change_percent,
                high_value = excluded.high_value,
                low_value = excluded.low_value,
                open_value = excluded.open_value,
                volume = excluded.volume,
                amount = excluded.amount,
                iopv = excluded.iopv,
                quote_time = excluded.quote_time,
                received_at = excluded.received_at,
                raw_code = excluded.raw_code,
                raw_payload = excluded.raw_payload;
            """;
        command.Parameters.AddWithValue("$symbol", record.Symbol);
        command.Parameters.AddWithValue("$display_name", DbValue(record.DisplayName));
        command.Parameters.AddWithValue("$market_type", record.MarketType);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$price", DbValue(record.Price));
        command.Parameters.AddWithValue("$last_close", DbValue(record.LastClose));
        command.Parameters.AddWithValue("$change_value", DbValue(record.ChangeValue));
        command.Parameters.AddWithValue("$change_percent", DbValue(record.ChangePercent));
        command.Parameters.AddWithValue("$high_value", DbValue(record.HighValue));
        command.Parameters.AddWithValue("$low_value", DbValue(record.LowValue));
        command.Parameters.AddWithValue("$open_value", DbValue(record.OpenValue));
        command.Parameters.AddWithValue("$volume", DbValue(record.Volume));
        command.Parameters.AddWithValue("$amount", DbValue(record.Amount));
        command.Parameters.AddWithValue("$iopv", DbValue(record.Iopv));
        command.Parameters.AddWithValue("$quote_time", DbValue(record.QuoteTime));
        command.Parameters.AddWithValue("$received_at", string.IsNullOrWhiteSpace(record.ReceivedAt) ? LocalDatabase.NowText() : record.ReceivedAt);
        command.Parameters.AddWithValue("$raw_code", DbValue(record.RawCode));
        command.Parameters.AddWithValue("$raw_payload", DbValue(record.RawPayload));
        command.ExecuteNonQuery();
    }

    private void WriteMarketHistoryGuardLog(string level, string message, string detail)
    {
        using var connection = _database.OpenConnection();
        using (var probe = connection.CreateCommand())
        {
            probe.CommandText = """
                SELECT time
                FROM runtime_log
                WHERE module = $module AND message = $message AND detail = $detail
                ORDER BY id DESC
                LIMIT 1;
                """;
            probe.Parameters.AddWithValue("$module", MarketSources.EastMoneyHistory);
            probe.Parameters.AddWithValue("$message", message);
            probe.Parameters.AddWithValue("$detail", detail);
            object? lastTime = probe.ExecuteScalar();
            if (lastTime is not null
                && DateTime.TryParse(Convert.ToString(lastTime, CultureInfo.InvariantCulture), out DateTime parsed)
                && DateTime.Now - parsed < TimeSpan.FromMinutes(2))
            {
                return;
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runtime_log(time, level, module, message, detail)
            VALUES($time, $level, $module, $message, $detail);
            """;
        command.Parameters.AddWithValue("$time", LocalDatabase.NowText());
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$module", MarketSources.EastMoneyHistory);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$detail", detail);
        command.ExecuteNonQuery();
    }

    private static bool IsCoreIndexHistorySymbol(string symbol)
        => string.Equals(symbol, "251.NDXTMC", StringComparison.OrdinalIgnoreCase)
           || string.Equals(symbol, "100.NDX100", StringComparison.OrdinalIgnoreCase);

    private void DeleteById(string tableName, long id)
    {
        if (id <= 0)
        {
            return;
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {tableName} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void DeleteById(SqliteConnection connection, SqliteTransaction transaction, string tableName, long id)
    {
        if (id <= 0)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {tableName} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static bool HasFiniteValue(double? value)
        => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);

    private static bool ValuesEqual(double? left, double? right)
    {
        if (!left.HasValue && !right.HasValue)
        {
            return true;
        }

        return left.HasValue
               && right.HasValue
               && Math.Abs(left.Value - right.Value) < SnapshotValueTolerance;
    }

    private static DateTime ParseSortTime(string? value)
        => DateTime.TryParse(value, out DateTime parsed) ? parsed : DateTime.MinValue;

    private static bool TryParseDateTimeOffset(string? value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result)
           || DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out result);

    private static string SnapshotDistinctKey(AccountReplaySnapshotRecord snapshot)
        => string.Join("|",
            snapshot.CreatedAt,
            snapshot.TotalAssets?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            snapshot.TotalPnl?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            snapshot.TotalUnrealizedPnl?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            snapshot.CashBalance?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            snapshot.Principal?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            snapshot.MarketValueComplete ? "1" : "0");

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object DbValue(double? value) => value.HasValue ? value.Value : DBNull.Value;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? OptionalString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static double? OptionalDouble(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static int? OptionalInt(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static long? OptionalLong(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static bool TryParseSettingDouble(string? value, out double result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0;
            return false;
        }

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result)
               || double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result);
    }

    private static void RequireText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }
    }

    private static void RequireAllowed(string value, string fieldName, params string[] allowedValues)
    {
        if (!allowedValues.Contains(value))
        {
            throw new InvalidOperationException($"{fieldName}只允许：{string.Join("、", allowedValues)}。");
        }
    }
}
