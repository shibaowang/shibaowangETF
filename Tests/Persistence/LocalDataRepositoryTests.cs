using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;
using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public class LocalDataRepositoryTests
{
    [Fact]
    public void TemporaryDatabase_CreatesTablesAndPersistsLocalRecords()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_terminal_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveStrategyConfig(new StrategyConfigRecord
            {
                Code = "159509",
                Name = "本地录入ETF",
                IndexSecId = "100.NDX",
                IndexHigh = 20000,
                Enabled = true
            });
            repository.SaveAccountState(new AccountStateRecord
            {
                Principal = 100000,
                CashBalance = 25000,
                TotalAssets = 100000,
                BasePositionRatio = 0.5,
                SniperPoolAmount = 10000
            });
            repository.SavePositionState(new PositionStateRecord
            {
                StrategyCode = "159509",
                ActualCode = "159509",
                Source = "场内ETF",
                Quantity = 100,
                CostAmount = 1000,
                AdjFactor = 1
            });
            repository.SaveOtcChannel(new OtcChannelRecord
            {
                StrategyCode = "159509",
                OtcCode = "017091",
                ClassType = "A类",
                Enabled = true,
                DailyLimit = 50000,
                Priority = 1,
                MinBuy = 100
            });
            repository.SaveTradeLog(new TradeLogRecord
            {
                Time = "2026-06-13 10:00:00",
                StrategyCode = "159509",
                ActualCode = "159509",
                Action = "买入",
                Price = 1.23,
                Quantity = 100,
                Amount = 123
            });

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));

            Assert.Single(reopened.ReadStrategyConfigs());
            Assert.NotNull(reopened.ReadLatestAccountState());
            Assert.Single(reopened.ReadPositionStates());
            Assert.Single(reopened.ReadOtcChannels());
            Assert.Single(reopened.ReadTradeLogs());
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void TradeLog_SaveAndReadRoundTripsAllEditableFields()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_trade_log_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveTradeLog(new TradeLogRecord
            {
                Time = "2026-06-13 10:00:00",
                StrategyCode = "159941",
                ActualCode = "159941",
                Action = "买入",
                Price = 1.2345,
                Quantity = 1000.5678,
                Amount = 1235.2007,
                Tier = "狙击一档",
                Source = "场内ETF",
                Fee = 0.12,
                Memo = "人工验收",
                NetCashImpact = -1235.3207,
                Principal = 100000.55,
                CashBalance = 98764.6793,
                TotalAssets = 100000.4321
            });

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            TradeLogRecord record = Assert.Single(reopened.ReadTradeLogs());

            Assert.Equal("2026-06-13 10:00:00", record.Time);
            Assert.Equal("159941", record.StrategyCode);
            Assert.Equal("159941", record.ActualCode);
            Assert.Equal("买入", record.Action);
            Assert.Equal(1.2345, record.Price, 4);
            Assert.Equal(1000.5678, record.Quantity, 4);
            Assert.Equal(1235.2007, record.Amount, 4);
            Assert.Equal("狙击一档", record.Tier);
            Assert.Equal("场内ETF", record.Source);
            Assert.Equal(0.12, record.Fee, 2);
            Assert.Equal("人工验收", record.Memo);
            Assert.Equal(-1235.3207, record.NetCashImpact, 4);
            Assert.Equal(100000.55, record.Principal, 2);
            Assert.Equal(98764.6793, record.CashBalance, 4);
            Assert.Equal(100000.4321, record.TotalAssets, 4);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void StrategyConfig_PercentFieldsSaveAsNormalizedDecimals()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_strategy_percent_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveStrategyConfig(new StrategyConfigRecord
            {
                Code = "159941",
                Name = "纳指ETF",
                ExtraPrice = PercentValueParser.ParsePercentInput("15%"),
                SellRatio = PercentValueParser.ParsePercentInput("40%"),
                TakeProfitPrice = PercentValueParser.ParsePercentInput("8"),
                AddPremiumLimit = PercentValueParser.ParsePercentInput("0.02"),
                Enabled = true
            });

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            StrategyConfigRecord record = Assert.Single(reopened.ReadStrategyConfigs());

            Assert.Equal(0.15, record.ExtraPrice!.Value, 4);
            Assert.Equal(0.40, record.SellRatio!.Value, 4);
            Assert.Equal(0.08, record.TakeProfitPrice!.Value, 4);
            Assert.Equal(0.02, record.AddPremiumLimit!.Value, 4);
            Assert.Equal("15%", PercentValueParser.FormatPercent(record.ExtraPrice));
            Assert.Equal("40%", PercentValueParser.FormatPercent(record.SellRatio));
            Assert.Equal("8%", PercentValueParser.FormatPercent(record.TakeProfitPrice));
            Assert.Equal("2%", PercentValueParser.FormatPercent(record.AddPremiumLimit));

            using var connection = new LocalDatabase(databasePath).OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT extra_price, sell_ratio, take_profit_price, add_premium_limit FROM strategy_config WHERE code = '159941';";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(0.15, reader.GetDouble(0), 4);
            Assert.Equal(0.40, reader.GetDouble(1), 4);
            Assert.Equal(0.08, reader.GetDouble(2), 4);
            Assert.Equal(0.02, reader.GetDouble(3), 4);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AccountReplayHistory_ReadsRecentReplayStatesInTimeOrder()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_replay_history_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveAccountReplayResult(new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-06-14 10:00:00",
                    ReplayStatus = "正常",
                    TotalAssets = 100000,
                    TotalPnl = 100
                }
            });
            repository.SaveAccountReplayResult(new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-06-14 10:01:00",
                    ReplayStatus = "正常",
                    TotalAssets = 100500,
                    TotalPnl = 150
                }
            });

            IReadOnlyList<AccountReplayStateRecord> history = repository.ReadAccountReplayHistory();

            Assert.Equal(2, history.Count);
            Assert.Equal("2026-06-14 10:00:00", history[0].CalculatedAt);
            Assert.Equal("2026-06-14 10:01:00", history[1].CalculatedAt);
            Assert.Equal(100000, history[0].TotalAssets);
            Assert.Equal(150, history[1].TotalPnl);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AccountReplaySnapshots_SaveAndReadInTimeOrder()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_replay_snapshot_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveAccountReplayResult(new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-06-14 10:00:00",
                    ReplayStatus = "正常",
                    TotalAssets = 100000,
                    TotalPnl = 100,
                    TotalUnrealizedPnl = 80,
                    CashBalance = 20000,
                    Principal = 100000,
                    MarketValueComplete = true
                }
            });
            repository.SaveAccountReplayResult(new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-06-14 10:01:00",
                    ReplayStatus = "正常",
                    TotalAssets = 100700,
                    TotalPnl = 250,
                    TotalUnrealizedPnl = 200,
                    CashBalance = 20500,
                    Principal = 100000,
                    MarketValueComplete = true
                }
            });

            IReadOnlyList<AccountReplaySnapshotRecord> snapshots = repository.ReadAccountReplaySnapshots();

            Assert.Equal(2, snapshots.Count);
            Assert.Equal("2026-06-14 10:00:00", snapshots[0].CreatedAt);
            Assert.Equal("2026-06-14 10:01:00", snapshots[1].CreatedAt);
            Assert.Equal(100700, snapshots[1].TotalAssets);
            Assert.Equal(250, snapshots[1].TotalPnl);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AccountReplaySnapshots_UseSnapshotsWhenAvailableAndIgnoreStateHistory()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_replay_snapshot_priority_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            foreach ((string calculatedAt, double totalAssets) in new[]
                     {
                         ("2026-06-14 10:00:00", 100000d),
                         ("2026-06-14 10:10:00", 100500d),
                         ("2026-06-14 10:20:00", 101000d)
                     })
            {
                repository.SaveAccountReplayResult(new AccountReplayResult
                {
                    Account = new AccountReplayStateRecord
                    {
                        CalculatedAt = calculatedAt,
                        ReplayStatus = "OK",
                        TotalAssets = totalAssets,
                        TotalPnl = totalAssets - 100000,
                        TotalUnrealizedPnl = totalAssets - 100000,
                        CashBalance = 20000,
                        Principal = 100000,
                        MarketValueComplete = true
                    }
                });
            }

            foreach (string calculatedAt in new[] { "2026-06-14 10:30:00", "2026-06-14 10:40:00", "2026-06-14 10:50:00" })
            {
                repository.SaveAccountReplayResult(new AccountReplayResult
                {
                    Account = new AccountReplayStateRecord
                    {
                        CalculatedAt = calculatedAt,
                        ReplayStatus = "OK",
                        TotalAssets = 101000,
                        TotalPnl = 1000,
                        TotalUnrealizedPnl = 1000,
                        CashBalance = 20000,
                        Principal = 100000,
                        MarketValueComplete = true
                    }
                });
            }

            IReadOnlyList<AccountReplaySnapshotRecord> snapshots = repository.ReadAccountReplaySnapshots();

            Assert.Equal(new[] { 100000d, 100500d, 101000d }, snapshots.Select(snapshot => snapshot.TotalAssets!.Value).ToArray());
            Assert.DoesNotContain(snapshots, snapshot => snapshot.CreatedAt == "2026-06-14 10:30:00");
            Assert.DoesNotContain(snapshots, snapshot => snapshot.CreatedAt == "2026-06-14 10:40:00");
            Assert.DoesNotContain(snapshots, snapshot => snapshot.CreatedAt == "2026-06-14 10:50:00");
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AccountReplaySnapshots_FallBackToStateHistoryOnlyWhenSnapshotsAreEmpty()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_replay_snapshot_fallback_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            foreach ((string calculatedAt, double totalAssets) in new[]
                     {
                         ("2026-06-14 10:00:00", 100000d),
                         ("2026-06-14 10:10:00", 100700d)
                     })
            {
                repository.SaveAccountReplayResult(new AccountReplayResult
                {
                    Account = new AccountReplayStateRecord
                    {
                        CalculatedAt = calculatedAt,
                        ReplayStatus = "OK",
                        TotalAssets = totalAssets,
                        TotalPnl = totalAssets - 100000,
                        TotalUnrealizedPnl = totalAssets - 100000,
                        CashBalance = 20000,
                        Principal = 100000,
                        MarketValueComplete = true
                    }
                });
            }

            using (var connection = new LocalDatabase(databasePath).OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM account_replay_snapshot;";
                command.ExecuteNonQuery();
            }

            IReadOnlyList<AccountReplaySnapshotRecord> snapshots = repository.ReadAccountReplaySnapshots();

            Assert.Equal(2, snapshots.Count);
            Assert.Equal(new[] { 100000d, 100700d }, snapshots.Select(snapshot => snapshot.TotalAssets!.Value).ToArray());
            Assert.Equal("2026-06-14 10:00:00", snapshots[0].CreatedAt);
            Assert.Equal("2026-06-14 10:10:00", snapshots[1].CreatedAt);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AccountReplaySnapshots_SkipDuplicateFinancialValuesAcrossMinutes()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_replay_snapshot_dedup_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            var account = new AccountReplayStateRecord
            {
                CalculatedAt = "2026-06-14 10:00:00",
                ReplayStatus = "正常",
                TotalAssets = 100000,
                TotalPnl = 100,
                TotalUnrealizedPnl = 80,
                CashBalance = 20000,
                Principal = 100000,
                MarketValueComplete = true
            };
            repository.SaveAccountReplayResult(new AccountReplayResult { Account = account });
            account.CalculatedAt = "2026-06-14 10:05:30";
            repository.SaveAccountReplayResult(new AccountReplayResult { Account = account });

            using var connection = new LocalDatabase(databasePath).OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), MAX(total_assets) FROM account_replay_snapshot;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal(100000, reader.GetDouble(1));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AccountReplaySnapshots_InsertWhenMoneyChangesByOneCent()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_replay_snapshot_cent_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveAccountReplayResult(new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-06-14 10:00:00",
                    ReplayStatus = "正常",
                    TotalAssets = 100000,
                    TotalPnl = 100,
                    TotalUnrealizedPnl = 80,
                    CashBalance = 20000,
                    Principal = 100000,
                    MarketValueComplete = true
                }
            });
            repository.SaveAccountReplayResult(new AccountReplayResult
            {
                Account = new AccountReplayStateRecord
                {
                    CalculatedAt = "2026-06-14 10:01:00",
                    ReplayStatus = "正常",
                    TotalAssets = 100000.01,
                    TotalPnl = 100,
                    TotalUnrealizedPnl = 80,
                    CashBalance = 20000,
                    Principal = 100000,
                    MarketValueComplete = true
                }
            });

            using var connection = new LocalDatabase(databasePath).OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), MAX(total_assets) FROM account_replay_snapshot;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetInt64(0));
            Assert.Equal(100000.01, reader.GetDouble(1), 2);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AccountReplaySnapshots_SkipWhenOnlyCalculatedAtChanges()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_replay_snapshot_time_only_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            foreach (string calculatedAt in new[] { "2026-06-14 10:00:00", "2026-06-14 10:30:00" })
            {
                repository.SaveAccountReplayResult(new AccountReplayResult
                {
                    Account = new AccountReplayStateRecord
                    {
                        CalculatedAt = calculatedAt,
                        ReplayStatus = "正常",
                        TotalAssets = 100000,
                        TotalPnl = 100,
                        TotalUnrealizedPnl = 80,
                        CashBalance = 20000,
                        Principal = 100000,
                        MarketValueComplete = true
                    }
                });
            }

            using var connection = new LocalDatabase(databasePath).OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), MIN(created_at), MAX(created_at) FROM account_replay_snapshot;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal("2026-06-14 10:00:00", reader.GetString(1));
            Assert.Equal("2026-06-14 10:00:00", reader.GetString(2));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_SaveAndReadEtfDecisionVisibleColumns()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_app_settings_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string value = EtfDecisionColumnSettings.SerializeVisibleColumns(new[] { "code", "name", "price" });

            repository.SaveAppSetting(EtfDecisionColumnSettings.SettingKey, value);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            Assert.Equal(value, reopened.ReadAppSetting(EtfDecisionColumnSettings.SettingKey));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_SaveAndReadEtfDecisionPinnedSymbols()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_pinned_symbols_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string value = EtfDecisionColumnSettings.SerializePinnedSymbols(new[] { "159941", "159509" });

            repository.SaveAppSetting(EtfDecisionColumnSettings.PinnedSymbolsSettingKey, value);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            Assert.Equal(value, reopened.ReadAppSetting(EtfDecisionColumnSettings.PinnedSymbolsSettingKey));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_PinnedSymbolsDoNotAffectVisibleColumns()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_pinned_independent_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string columns = EtfDecisionColumnSettings.SerializeVisibleColumns(new[] { "code", "name", "price" });
            string pinned = EtfDecisionColumnSettings.SerializePinnedSymbols(new[] { "159941", "159509" });

            repository.SaveAppSetting(EtfDecisionColumnSettings.SettingKey, columns);
            repository.SaveAppSetting(EtfDecisionColumnSettings.PinnedSymbolsSettingKey, pinned);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            Assert.Equal(columns, reopened.ReadAppSetting(EtfDecisionColumnSettings.SettingKey));
            Assert.Equal(pinned, reopened.ReadAppSetting(EtfDecisionColumnSettings.PinnedSymbolsSettingKey));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_ManualEntryColumnLayoutsAreIndependentByTab()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_manual_entry_columns_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string otcKey = ManualEntryColumnLayoutService.BuildSettingKey("otc_map");
            string tradeLogKey = ManualEntryColumnLayoutService.BuildSettingKey("trade_log");
            string otcOrder = "StrategyCode,ClassType,Id,OtcCode,Enabled";
            string tradeLogOrder = "StrategyCode,Quantity,Price,Amount,Action";

            repository.SaveAppSetting(otcKey, otcOrder);
            repository.SaveAppSetting(tradeLogKey, tradeLogOrder);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            Assert.Equal(otcOrder, reopened.ReadAppSetting(otcKey));
            Assert.Equal(tradeLogOrder, reopened.ReadAppSetting(tradeLogKey));
            Assert.Null(reopened.ReadAppSetting(EtfDecisionColumnSettings.SettingKey));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_ReadsDefaultHotkeySettingsWhenMissing()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_hotkey_default_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));

            HotkeySettings settings = repository.ReadHotkeySettings();

            Assert.True(settings.Enabled);
            Assert.Equal(HotkeyModifierKeys.Alt, settings.Modifiers);
            Assert.Equal("D1", settings.Key);
            Assert.Equal("Alt+1", settings.DisplayText);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_SaveAndReadCustomHotkeySettings()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_hotkey_custom_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            var settings = new HotkeySettings(true, HotkeyModifierKeys.Ctrl | HotkeyModifierKeys.Shift, "F9");

            repository.SaveHotkeySettings(settings);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            Assert.Equal("true", reopened.ReadAppSetting(HotkeySettings.EnabledSettingKey));
            Assert.Equal("Ctrl,Shift", reopened.ReadAppSetting(HotkeySettings.ModifiersSettingKey));
            Assert.Equal("F9", reopened.ReadAppSetting(HotkeySettings.KeySettingKey));
            Assert.Equal(settings, reopened.ReadHotkeySettings());
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_SaveDefaultHotkeyUsesD1StorageAndAlt1Display()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_hotkey_default_save_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));

            repository.SaveHotkeySettings(HotkeySettings.Default);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            Assert.Equal("true", reopened.ReadAppSetting(HotkeySettings.EnabledSettingKey));
            Assert.Equal("Alt", reopened.ReadAppSetting(HotkeySettings.ModifiersSettingKey));
            Assert.Equal("D1", reopened.ReadAppSetting(HotkeySettings.KeySettingKey));
            Assert.Equal("Alt+1", reopened.ReadHotkeySettings().DisplayText);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_SaveDisabledHotkeySettings()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_hotkey_disabled_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveHotkeySettings(new HotkeySettings(false, HotkeyModifierKeys.None, "H"));

            HotkeySettings settings = new LocalDataRepository(new LocalDatabase(databasePath)).ReadHotkeySettings();

            Assert.False(settings.Enabled);
            Assert.Equal("false", repository.ReadAppSetting(HotkeySettings.EnabledSettingKey));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_InvalidEnabledHotkeyDoesNotOverwritePreviousSettings()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_hotkey_invalid_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            var valid = new HotkeySettings(true, HotkeyModifierKeys.Ctrl | HotkeyModifierKeys.Alt, "H");
            repository.SaveHotkeySettings(valid);

            Assert.Throws<InvalidOperationException>(() => repository.SaveHotkeySettings(new HotkeySettings(true, HotkeyModifierKeys.None, "H")));

            HotkeySettings settings = new LocalDataRepository(new LocalDatabase(databasePath)).ReadHotkeySettings();
            Assert.Equal(valid, settings);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AppSettings_SaveHotkeySettingsDoesNotChangeTradeLogRows()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_hotkey_tradelog_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveTradeLogsSnapshot(
                Array.Empty<long>(),
                new[]
                {
                    new TradeLogRecord
                    {
                        Time = "2026-06-17 09:30:00",
                        StrategyCode = "159941",
                        ActualCode = "159941",
                        Action = "买入",
                        Price = 1,
                        Quantity = 100,
                        Amount = 100,
                        Fee = 0
                    }
                });
            int before = repository.ReadTradeLogs().Count;

            repository.SaveHotkeySettings(new HotkeySettings(true, HotkeyModifierKeys.Ctrl | HotkeyModifierKeys.Shift, "F9"));

            int after = new LocalDataRepository(new LocalDatabase(databasePath)).ReadTradeLogs().Count;
            Assert.Equal(before, after);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void MarketHistoryCache_DailyCacheIsNotOverwrittenByMonthlyFallback()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_history_guard_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string dailyPayload = BuildDailyPayload(new DateTime(2020, 1, 1), 1807);
            string monthlyPayload = BuildMonthlyPayload(new DateTime(2019, 4, 30), 87);

            repository.SaveMarketHistory("100.NDX100", "INDEX", 30762.2, dailyPayload);
            MarketHistoryOverwriteDecision decision = repository.SaveMarketHistory("100.NDX100", "INDEX", 30762.2, monthlyPayload);

            Assert.False(decision.AllowOverwrite);
            Assert.Equal("SKIP_HISTORY_DOWNGRADE", decision.Code);
            MarketQuoteRecord? saved = repository.ReadLatestMarketHistory("100.NDX100", "INDEX");
            Assert.NotNull(saved);
            Assert.Equal(1807, EastMoneyHistoryParser.ParsePoints(saved!.RawPayload!).Count);
            Assert.Contains("SKIP_HISTORY_DOWNGRADE", ReadRuntimeLogDetails(databasePath));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void MarketHistoryCache_DailyCacheCanOverwriteOldMonthlyFallback()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_history_daily_replace_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string monthlyPayload = BuildMonthlyPayload(new DateTime(2019, 4, 30), 87);
            string dailyPayload = BuildDailyPayload(new DateTime(2020, 1, 1), 1807);

            repository.SaveMarketHistory("100.NDX100", "INDEX", 30762.2, monthlyPayload);
            MarketHistoryOverwriteDecision decision = repository.SaveMarketHistory("100.NDX100", "INDEX", 30762.2, dailyPayload);

            Assert.True(decision.AllowOverwrite);
            MarketQuoteRecord? saved = repository.ReadLatestMarketHistory("100.NDX100", "INDEX");
            Assert.NotNull(saved);
            Assert.Equal(1807, EastMoneyHistoryParser.ParsePoints(saved!.RawPayload!).Count);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void MarketHistoryCache_InvalidPayloadDoesNotOverwriteExistingDailyCache()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_history_invalid_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string dailyPayload = BuildDailyPayload(new DateTime(2020, 1, 1), 741);

            repository.SaveMarketHistory("251.NDXTMC", "INDEX", 3071.7, dailyPayload);
            MarketHistoryOverwriteDecision decision = repository.SaveMarketHistory("251.NDXTMC", "INDEX", 3071.7, """{"data":{"klines":[]}}""");

            Assert.False(decision.AllowOverwrite);
            Assert.Equal("SKIP_HISTORY_INVALID", decision.Code);
            MarketQuoteRecord? saved = repository.ReadLatestMarketHistory("251.NDXTMC", "INDEX");
            Assert.NotNull(saved);
            Assert.Equal(741, EastMoneyHistoryParser.ParsePoints(saved!.RawPayload!).Count);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void MarketHistoryCache_CoreIndexRejectsMonthlyFallbackAfterDailyCache()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_history_core_guard_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string dailyPayload = BuildDailyPayload(new DateTime(2023, 6, 30), 741);
            string monthlyPayload = BuildMonthlyPayload(new DateTime(2023, 6, 30), 37);

            repository.SaveMarketHistory("251.NDXTMC", "INDEX", 3071.7, dailyPayload);
            MarketHistoryOverwriteDecision decision = repository.SaveMarketHistory("251.NDXTMC", "INDEX", 3071.7, monthlyPayload);

            Assert.False(decision.AllowOverwrite);
            Assert.Equal("SKIP_HISTORY_DOWNGRADE", decision.Code);
            MarketQuoteRecord? saved = repository.ReadLatestMarketHistory("251.NDXTMC", "INDEX");
            Assert.NotNull(saved);
            Assert.Equal(741, EastMoneyHistoryParser.ParsePoints(saved!.RawPayload!).Count);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void MarketHistoryCache_AllowsMonthlyFallbackWhenNoPriorCacheExists()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_history_degraded_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string monthlyPayload = BuildMonthlyPayload(new DateTime(2019, 4, 30), 87);

            MarketHistoryOverwriteDecision decision = repository.SaveMarketHistory("100.NDX100", "INDEX", 30762.2, monthlyPayload);

            Assert.True(decision.AllowOverwrite);
            Assert.Equal("HISTORY_DEGRADED_MONTHLY_FALLBACK", decision.Code);
            MarketQuoteRecord? saved = repository.ReadLatestMarketHistory("100.NDX100", "INDEX");
            Assert.NotNull(saved);
            Assert.Equal(87, EastMoneyHistoryParser.ParsePoints(saved!.RawPayload!).Count);
            Assert.Contains("HISTORY_DEGRADED_MONTHLY_FALLBACK", ReadRuntimeLogDetails(databasePath));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AlertSettings_SaveAndReadUsesAppSettings()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_alert_settings_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveAlertSettings(new AlertSettings
            {
                PushPlusEnabled = true,
                PushPlusToken = "token-value",
                VoiceEnabled = true,
                RepeatIntervalMinutes = 30,
                SevereIntervalMinutes = 5,
                MarketIntervalMinutes = 10
            });

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            AlertSettings settings = reopened.ReadAlertSettings();

            Assert.True(settings.PushPlusEnabled);
            Assert.Equal("token-value", settings.PushPlusToken);
            Assert.True(settings.VoiceEnabled);
            Assert.Equal("true", reopened.ReadAppSetting(AlertSettings.PushPlusEnabledKey));
            Assert.Equal("token-value", reopened.ReadAppSetting(AlertSettings.PushPlusTokenKey));
            Assert.Equal("30", reopened.ReadAppSetting(AlertSettings.RepeatIntervalMinutesKey));
            Assert.Equal("5", reopened.ReadAppSetting(AlertSettings.SevereIntervalMinutesKey));
            Assert.Equal("10", reopened.ReadAppSetting(AlertSettings.MarketIntervalMinutesKey));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void RuntimeLogAlertCursor_SaveReadAndReadLogsAfterId()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_runtime_cursor_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.WriteRuntimeLog("WARN", "EASTMONEY_HISTORY", "first kline warning", "old");
            repository.WriteRuntimeLog("ERROR", "TENCENT_QT", "second quote error", "new");

            IReadOnlyList<RuntimeLogRecord> allLogs = repository.ReadRuntimeLogsAfterId(0, 100);
            Assert.Equal(2, allLogs.Count);

            repository.SaveRuntimeLogAlertCursor(allLogs[0].Id);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            Assert.Equal(allLogs[0].Id, reopened.ReadRuntimeLogAlertCursor());

            RuntimeLogRecord next = Assert.Single(reopened.ReadRuntimeLogsAfterId(allLogs[0].Id, 100));
            Assert.Equal(allLogs[1].Id, next.Id);
            Assert.Equal("TENCENT_QT", next.Module);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void RuntimeLogAlertCursor_FirstInitializationMovesToCurrentMax()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_runtime_cursor_init_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.WriteRuntimeLog("ERROR", "EASTMONEY_HISTORY", "old history error", "ResponseEnded");
            repository.WriteRuntimeLog("ERROR", "TENCENT_QT", "old quote error", "timeout");
            long maxBeforeInit = repository.GetMaxRuntimeLogId();

            bool initialized = repository.InitializeRuntimeLogAlertCursorIfMissing(out long cursor);

            Assert.True(initialized);
            Assert.Equal(maxBeforeInit, cursor);
            Assert.Equal(maxBeforeInit, repository.ReadRuntimeLogAlertCursor());
            Assert.Empty(repository.ReadRuntimeLogsAfterId(cursor, 100));

            repository.WriteRuntimeLog("ERROR", "TENCENT_QT", "new quote error", "timeout");
            RuntimeLogRecord newLog = Assert.Single(repository.ReadRuntimeLogsAfterId(cursor, 100));
            Assert.True(newLog.Id > cursor);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void RuntimeLogAlertCursor_SecondInitializationKeepsExistingCursor()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_runtime_cursor_keep_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.WriteRuntimeLog("ERROR", "EASTMONEY_HISTORY", "old history error", "ResponseEnded");
            repository.SaveRuntimeLogAlertCursor(0);

            bool initialized = repository.InitializeRuntimeLogAlertCursorIfMissing(out long cursor);

            Assert.False(initialized);
            Assert.Equal(0, cursor);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AlertLogAndDeliveryState_SaveAndReadBack()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_alert_log_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveAlertLog(new AlertLogRecord
            {
                CreatedAt = "2026-06-18 09:30:00",
                AlertType = AlertTypes.StrategyDecision,
                Severity = AlertSeverity.Severe,
                StrategyCode = "159941",
                Title = "【作战指令】159941 全清换现金(留底)",
                Content = "content",
                DedupeKey = "strategy|159941|sell|extreme",
                WechatEnabled = true,
                WechatStatus = "成功",
                WechatSentAt = "2026-06-18 09:30:01",
                VoiceEnabled = true,
                VoiceStatus = "成功",
                VoicePlayedAt = "2026-06-18 09:30:01",
                Source = "strategy_decision_state",
                ContentHash = "abc"
            });
            repository.SaveAlertDeliveryState(new AlertDeliveryStateRecord
            {
                DedupeKey = "strategy|159941|sell|extreme",
                LastAlertType = AlertTypes.StrategyDecision,
                LastStrategyCode = "159941",
                LastAction = "全清换现金(留底)",
                LastReason = "极端溢价",
                LastContentHash = "abc",
                LastSentAt = "2026-06-18 09:30:01",
                LastStatus = "成功",
                LastTitle = "title",
                LastContent = "content"
            });

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            AlertLogRecord log = Assert.Single(reopened.ReadAlertLogs());
            AlertDeliveryStateRecord? state = reopened.ReadAlertDeliveryState("strategy|159941|sell|extreme");

            Assert.Equal("159941", log.StrategyCode);
            Assert.Equal("成功", log.WechatStatus);
            Assert.Equal("成功", log.VoiceStatus);
            Assert.NotNull(state);
            Assert.Equal("全清换现金(留底)", state!.LastAction);
            Assert.Equal("abc", state.LastContentHash);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AlertLog_ReadRecentOrdersByNewestWithoutModifyingRows()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_alert_recent_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveAlertLog(new AlertLogRecord
            {
                CreatedAt = "2026-06-18 09:00:00",
                AlertType = AlertTypes.StrategyDecision,
                Severity = AlertSeverity.Normal,
                StrategyCode = "159509",
                Title = "old",
                Content = "old content",
                DedupeKey = "old",
                WechatEnabled = true,
                WechatStatus = "成功",
                VoiceEnabled = false,
                VoiceStatus = "未启用",
                Source = "strategy_decision_state",
                ContentHash = "old"
            });
            repository.SaveAlertLog(new AlertLogRecord
            {
                CreatedAt = "2026-06-18 10:00:00",
                AlertType = AlertTypes.MarketRuntime,
                Severity = AlertSeverity.Market,
                Title = "new",
                Content = "new content",
                DedupeKey = "new",
                WechatEnabled = false,
                WechatStatus = "未启用",
                VoiceEnabled = true,
                VoiceStatus = "成功",
                Source = "market_source_status",
                ContentHash = "new"
            });

            IReadOnlyList<AlertLogRecord> firstRead = repository.ReadAlertLogs(10);
            IReadOnlyList<AlertLogRecord> secondRead = repository.ReadAlertLogs(10);
            int tradeLogCount = repository.ReadTradeLogs().Count;

            Assert.Equal(2, firstRead.Count);
            Assert.Equal("new", firstRead[0].Title);
            Assert.Equal("old", firstRead[1].Title);
            Assert.Equal(2, secondRead.Count);
            Assert.Equal(firstRead.Select(record => record.Id), secondRead.Select(record => record.Id));
            Assert.Equal(0, tradeLogCount);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AlertLog_ClearOnlyDeletesAlertLogAndKeepsDeliveryState()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_alert_clear_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveAlertLog(new AlertLogRecord
            {
                CreatedAt = "2026-06-18 09:00:00",
                AlertType = AlertTypes.StrategyDecision,
                Severity = AlertSeverity.Normal,
                Title = "one",
                Content = "content",
                DedupeKey = "one",
                WechatEnabled = true,
                WechatStatus = "成功",
                VoiceEnabled = false,
                VoiceStatus = "未启用",
                Source = "strategy_decision_state",
                ContentHash = "one"
            });
            repository.SaveAlertLog(new AlertLogRecord
            {
                CreatedAt = "2026-06-18 09:01:00",
                AlertType = AlertTypes.MarketRuntime,
                Severity = AlertSeverity.Market,
                Title = "two",
                Content = "content",
                DedupeKey = "two",
                WechatEnabled = false,
                WechatStatus = "未启用",
                VoiceEnabled = true,
                VoiceStatus = "成功",
                Source = "market_source_status",
                ContentHash = "two"
            });
            repository.SaveAlertDeliveryState(new AlertDeliveryStateRecord
            {
                DedupeKey = "one",
                LastAlertType = AlertTypes.StrategyDecision,
                LastStrategyCode = "159941",
                LastAction = "全清换现金(留底)",
                LastReason = "极端溢价",
                LastContentHash = "one",
                LastSentAt = "2026-06-18 09:00:00",
                LastStatus = "成功",
                LastTitle = "one",
                LastContent = "content"
            });

            repository.ClearAlertLogs();

            Assert.Empty(repository.ReadAlertLogs(100));
            Assert.NotNull(repository.ReadAlertDeliveryState("one"));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void AlertLog_ClearDoesNotAffectTradeLog()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_alert_clear_trade_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveTradeLog(new TradeLogRecord
            {
                Time = "2026-06-18 09:00:00",
                StrategyCode = "159941",
                ActualCode = "159941",
                Action = "买入",
                Price = 1,
                Quantity = 1000,
                Amount = 1000,
                Fee = 0,
                NetCashImpact = -1000,
                Principal = 100000,
                CashBalance = 99000,
                TotalAssets = 100000
            });
            repository.SaveAlertLog(new AlertLogRecord
            {
                CreatedAt = "2026-06-18 09:00:00",
                AlertType = AlertTypes.StrategyDecision,
                Severity = AlertSeverity.Normal,
                Title = "alert",
                Content = "content",
                DedupeKey = "alert",
                WechatEnabled = true,
                WechatStatus = "成功",
                VoiceEnabled = false,
                VoiceStatus = "未启用",
                Source = "strategy_decision_state",
                ContentHash = "alert"
            });

            int before = repository.ReadTradeLogs().Count;
            repository.ClearAlertLogs();

            Assert.Equal(before, repository.ReadTradeLogs().Count);
            Assert.Empty(repository.ReadAlertLogs(100));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void ChartIntradayCache_SaveAndReadPersistsRealTrends2Payload()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_intraday_cache_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string payload = BuildIntradayPayload(new DateTime(2026, 6, 22), 3);

            repository.SaveChartIntradayCache("159941", "159941", payload, DateTimeOffset.Parse("2026-06-22T10:00:00+08:00", CultureInfo.InvariantCulture));

            ChartIntradayCacheEntry? entry = repository.ReadLatestChartIntradayCache("159941");
            Assert.NotNull(entry);
            Assert.Equal(3, entry.Points.Count);
            Assert.Equal(1.60, entry.Points[0].Price, 6);
            Assert.Equal(100, entry.Points[0].Volume);
            Assert.True(entry.Status.IsReady);
            Assert.True(entry.Status.IsUsingCache);
            Assert.Equal("使用最近真实分时缓存", entry.Status.Message);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void ChartIntradayCache_DoesNotPersistInvalidOrEmptyPayload()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_intraday_invalid_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));

            repository.SaveChartIntradayCache("159941", "159941", "{}", DateTimeOffset.Parse("2026-06-22T10:00:00+08:00", CultureInfo.InvariantCulture));

            Assert.Null(repository.ReadLatestChartIntradayCache("159941"));
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void ChartIntradayCache_SaveAndReadPersistsRealTencentPayload()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_tencent_intraday_cache_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            string payload = BuildTencentIntradayPayload();

            repository.SaveChartIntradayCache(
                "159941",
                "159941",
                payload,
                DateTimeOffset.Parse("2026-06-22T10:00:00+08:00", CultureInfo.InvariantCulture),
                MarketSources.TencentIntraday,
                MarketSources.TencentIntradayQuality);

            ChartIntradayCacheEntry? entry = repository.ReadLatestChartIntradayCache("159941");
            Assert.NotNull(entry);
            Assert.Equal(2, entry.Points.Count);
            Assert.Equal(1.620, entry.Points[0].Price, 3);
            Assert.Equal(158339, entry.Points[0].Volume);
            Assert.Equal(470040, entry.Points[1].Volume);
            Assert.Equal(76154754.50, entry.Points[1].Amount);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void ChartIntradayCache_ReadsLatestTradeDate()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_intraday_latest_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveChartIntradayCache("159941", "159941", BuildIntradayPayload(new DateTime(2026, 6, 21), 2), DateTimeOffset.Parse("2026-06-21T10:00:00+08:00", CultureInfo.InvariantCulture));
            repository.SaveChartIntradayCache("159941", "159941", BuildIntradayPayload(new DateTime(2026, 6, 22), 4), DateTimeOffset.Parse("2026-06-22T10:00:00+08:00", CultureInfo.InvariantCulture));

            ChartIntradayCacheEntry? entry = repository.ReadLatestChartIntradayCache("159941");

            Assert.NotNull(entry);
            Assert.Equal(4, entry.Points.Count);
            Assert.Equal(new DateTime(2026, 6, 22), entry.Points[0].Time.Date);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    private static string BuildDailyPayload(DateTime startDate, int count)
    {
        string[] lines = Enumerable.Range(0, count)
            .Select(index => BuildKline(startDate.AddDays(index), 1000 + index))
            .ToArray();
        return BuildPayload(lines);
    }

    private static string BuildMonthlyPayload(DateTime startDate, int count)
    {
        string[] lines = Enumerable.Range(0, count)
            .Select(index => BuildKline(startDate.AddMonths(index), 1000 + index))
            .ToArray();
        return BuildPayload(lines);
    }

    private static string BuildPayload(IEnumerable<string> lines)
        => "{\"data\":{\"klines\":[" + string.Join(",", lines.Select(line => "\"" + line + "\"")) + "]}}";

    private static string BuildKline(DateTime date, double value)
        => string.Join(",",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            value.ToString("F2", CultureInfo.InvariantCulture),
            (value + 1).ToString("F2", CultureInfo.InvariantCulture),
            (value + 2).ToString("F2", CultureInfo.InvariantCulture),
            (value - 1).ToString("F2", CultureInfo.InvariantCulture),
            "100",
            "1000",
            "0",
            "0",
            "0",
            "0");

    private static string BuildIntradayPayload(DateTime date, int count)
    {
        string[] lines = Enumerable.Range(0, count)
            .Select(index =>
            {
                DateTime time = date.Date.AddHours(9).AddMinutes(30 + index);
                double price = 1.60 + index * 0.001;
                return string.Join(",",
                    time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    price.ToString("F3", CultureInfo.InvariantCulture),
                    price.ToString("F3", CultureInfo.InvariantCulture),
                    (price + 0.001).ToString("F3", CultureInfo.InvariantCulture),
                    (price - 0.001).ToString("F3", CultureInfo.InvariantCulture),
                    (100 + index).ToString(CultureInfo.InvariantCulture),
                    (1000 + index).ToString(CultureInfo.InvariantCulture),
                    price.ToString("F3", CultureInfo.InvariantCulture));
            })
            .ToArray();
        return "{\"data\":{\"trends\":[" + string.Join(",", lines.Select(line => "\"" + line + "\"")) + "]}}";
    }

    private static string BuildTencentIntradayPayload()
        => """
           {
             "code": 0,
             "data": {
               "sz159941": {
                 "data": {
                   "date": "20260622",
                   "data": [
                     "0930 1.620 158339 25650918.00",
                     "0931 1.621 628379 101805672.50"
                   ]
                 }
               }
             }
           }
           """;

    private static string ReadRuntimeLogDetails(string databasePath)
    {
        using var connection = new LocalDatabase(databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT group_concat(COALESCE(message, '') || ':' || COALESCE(detail, ''), '\n') FROM runtime_log;";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
