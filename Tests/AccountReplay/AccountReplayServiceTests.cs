using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Tests.AccountReplay;

public class AccountReplayServiceTests
{
    [Fact]
    public void Deposit_ReplaysCashAndPrincipal()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 1000, fee: 1, netCashImpact: 999, cashBalance: 999));

        Assert.Equal("正常", snapshot.Account!.ReplayStatus);
        Assert.Equal(999, snapshot.Account.CashBalance!.Value, 2);
        Assert.Equal(999, snapshot.Account.Principal!.Value, 2);
    }

    [Fact]
    public void Withdrawal_ReplaysNegativeCashImpact()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 1000, netCashImpact: 1000, cashBalance: 1000),
            Log("2026-06-14 10:00:00", "CASH", "出金", amount: 100, fee: 1, netCashImpact: -101, cashBalance: 899));

        Assert.Equal("正常", snapshot.Account!.ReplayStatus);
        Assert.Equal(899, snapshot.Account.CashBalance!.Value, 2);
        Assert.Equal(899, snapshot.Account.Principal!.Value, 2);
    }

    [Fact]
    public void MarketEtfBuy_ReplaysQuantityAndCost()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", quantity: 1000, amount: 1000, source: "场内ETF"));

        PositionReplayStateRecord position = Assert.Single(snapshot.Positions);
        Assert.Equal("估值不完整", snapshot.Account!.ReplayStatus);
        Assert.Equal(1000, position.Quantity, 4);
        Assert.Equal(1000, position.CostAmount, 2);
        Assert.Equal("未连接", position.QuoteStatus);
    }

    [Fact]
    public void MarketEtfSell_ReplaysCostReductionAndRealizedPnl()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 2000, netCashImpact: 2000, cashBalance: 2000),
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", quantity: 1000, amount: 1000, source: "场内ETF", cashBalance: 1000),
            Log("2026-06-14 10:30:00", "159941", "卖出", actualCode: "159941", quantity: 200, amount: 400, source: "场内ETF", cashBalance: 1400));

        PositionReplayStateRecord position = Assert.Single(snapshot.Positions);
        Assert.Equal(800, position.Quantity, 4);
        Assert.Equal(800, position.CostAmount, 2);
        Assert.Equal(200, position.RealizedPnl, 2);
        Assert.Equal(1400, snapshot.Account!.CashBalance!.Value, 2);
    }

    [Fact]
    public void OtcSubstituteBuy_ReplaysOtcDetail()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "017091", quantity: 500, amount: 600, source: "场外替代"));

        OtcPositionReplayStateRecord otc = Assert.Single(snapshot.OtcPositions);
        Assert.Equal("159941", otc.StrategyCode);
        Assert.Equal("017091", otc.ActualCode);
        Assert.Equal(500, otc.Quantity, 4);
        Assert.Equal(600, otc.CostAmount, 2);
    }

    [Fact]
    public void Dividend_ReplaysCashWithoutChangingPosition()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 1000, netCashImpact: 1000, cashBalance: 1000),
            Log("2026-06-14 10:00:00", "159941", "分红", actualCode: "159941", amount: 50, source: "场内ETF", cashBalance: 1050));

        Assert.Empty(snapshot.Positions);
        Assert.Equal(1050, snapshot.Account!.CashBalance!.Value, 2);
    }

    [Fact]
    public void BonusAndSplit_ReplayQuantityWithoutCost()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:30:00", "159941", "送股", actualCode: "159941", quantity: 100, source: "场内ETF"),
            Log("2026-06-14 10:30:00", "159941", "拆分", actualCode: "159941", quantity: 50, source: "场内ETF"));

        PositionReplayStateRecord position = Assert.Single(snapshot.Positions);
        Assert.Equal(150, position.Quantity, 4);
        Assert.Equal(0, position.CostAmount, 2);
    }

    [Fact]
    public void Merge_ReplaysQuantityAndOverMergeReportsError()
    {
        ReplaySnapshot success = ReplayThroughTempDb(
            Log("2026-06-14 09:30:00", "159941", "送股", actualCode: "159941", quantity: 100, source: "场内ETF"),
            Log("2026-06-14 10:30:00", "159941", "合并", actualCode: "159941", quantity: 40, source: "场内ETF"));
        Assert.Equal(60, Assert.Single(success.Positions).Quantity, 4);

        ReplaySnapshot failure = ReplayThroughTempDb(
            Log("2026-06-14 09:30:00", "159941", "送股", actualCode: "159941", quantity: 100, source: "场内ETF"),
            Log("2026-06-14 10:30:00", "159941", "合并", actualCode: "159941", quantity: 200, source: "场内ETF"));
        Assert.Equal("财务异常", failure.Account!.ReplayStatus);
        Assert.Contains("合并数量", failure.Account.ReplayError);
    }

    [Fact]
    public void Adjustment_ReplaysAdjFactor()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", quantity: 100, amount: 100, source: "场内ETF"),
            Log("2026-06-14 10:30:00", "159941", "除权校准", actualCode: "159941", quantity: 1.05, source: "场内ETF"));

        Assert.Equal(1.05, Assert.Single(snapshot.Positions).AdjFactor, 3);
    }

    [Fact]
    public void CashAuditFailure_BlocksReplay()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 1000, netCashImpact: 1000, cashBalance: 999));

        Assert.Equal("财务异常", snapshot.Account!.ReplayStatus);
        Assert.Contains("现金余额", snapshot.Account.ReplayError);
    }

    [Fact]
    public void DepositNetCashImpactMismatch_BlocksReplay()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 1000, fee: 1, netCashImpact: 900));

        Assert.Equal("财务异常", snapshot.Account!.ReplayStatus);
        Assert.Contains("净现金流", snapshot.Account.ReplayError);
    }

    [Fact]
    public void WithdrawalNetCashImpactMismatch_BlocksReplay()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:00:00", "CASH", "出金", amount: 100, fee: 1, netCashImpact: -90));

        Assert.Equal("财务异常", snapshot.Account!.ReplayStatus);
        Assert.Contains("净现金流", snapshot.Account.ReplayError);
    }

    [Fact]
    public void SellOverPosition_BlocksReplay()
    {
        ReplaySnapshot snapshot = ReplayThroughTempDb(
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", quantity: 100, amount: 100, source: "场内ETF"),
            Log("2026-06-14 10:30:00", "159941", "卖出", actualCode: "159941", quantity: 200, amount: 300, source: "场内ETF"));

        Assert.Equal("财务异常", snapshot.Account!.ReplayStatus);
        Assert.Contains("卖出数量", snapshot.Account.ReplayError);
    }

    [Fact]
    public void CashAuditError_CanRecoverAfterTradeLogFix()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_account_replay_recover_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveTradeLog(Log("2026-06-14 09:00:00", "CASH", "入金", amount: 1000, netCashImpact: 1000, cashBalance: 999));

            var service = new AccountReplayService();
            AccountReplayResult failure = service.Replay(repository.ReadTradeLogs(), repository.ReadMarketQuoteCache(), new DateTime(2026, 6, 14));
            repository.SaveAccountReplayResult(failure);
            Assert.Equal("财务异常", repository.ReadLatestAccountReplayState()!.ReplayStatus);

            TradeLogRecord fixedRecord = Assert.Single(repository.ReadTradeLogs());
            fixedRecord.CashBalance = 1000;
            repository.SaveTradeLog(fixedRecord);

            AccountReplayResult recovered = service.Replay(repository.ReadTradeLogs(), repository.ReadMarketQuoteCache(), new DateTime(2026, 6, 14));
            repository.SaveAccountReplayResult(recovered);
            Assert.Equal("正常", repository.ReadLatestAccountReplayState()!.ReplayStatus);
            Assert.Equal(1000, repository.ReadLatestAccountReplayState()!.CashBalance!.Value, 2);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void LedgerNormalizer_AutoFillsCashBalanceBeforeSave()
    {
        TradeLogRecord[] records =
        {
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 100000, cashBalance: 10000),
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", price: 1, quantity: 1000, amount: 1000, tier: "战略底仓", source: "场内ETF"),
            Log("2026-06-14 10:00:00", "CASH", "入金", amount: 5000, cashBalance: 10000)
        };

        ReplaySnapshot snapshot = NormalizeSaveAndReplay(records);

        TradeLogRecord[] saved = snapshot.TradeLogs.OrderBy(record => record.Time).ToArray();
        Assert.Equal(100000, saved[0].CashBalance, 2);
        Assert.Equal(99000, saved[1].CashBalance, 2);
        Assert.Equal(104000, saved[2].CashBalance, 2);
        Assert.Equal(104000, snapshot.Account!.CashBalance!.Value, 2);
        Assert.NotEqual("财务异常", snapshot.Account.ReplayStatus);
    }

    [Fact]
    public void LedgerNormalizer_AutoCalculatesBuyAmount()
    {
        var record = Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", price: 1.2345, quantity: 1000, source: "场内ETF");

        TradeLogLedgerNormalizer.AutoCalculateTradeAmount(record);

        Assert.Equal(1234.50, record.Amount, 2);
    }

    [Fact]
    public void LedgerNormalizer_PreservesDecimalQuantityAndRoundsAmountToCents()
    {
        var record = Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", price: 1.2345, quantity: 1000.5678, source: "场内ETF");

        ReplaySnapshot snapshot = NormalizeSaveAndReplay(new[] { record });
        TradeLogRecord saved = Assert.Single(snapshot.TradeLogs);

        Assert.Equal(1000.5678, saved.Quantity, 4);
        Assert.Equal(Math.Round(1.2345 * 1000.5678, 2, MidpointRounding.AwayFromZero), saved.Amount, 2);
    }

    [Theory]
    [InlineData("", "1000")]
    [InlineData(".", "1000")]
    [InlineData("0.", "1000")]
    [InlineData("1.", "1000")]
    [InlineData("1.2345", "")]
    [InlineData("1.2345", ".")]
    [InlineData("1.2345", "1000.")]
    public void LedgerNormalizer_AmountCalculationAllowsIntermediateEditorStates(string priceText, string quantityText)
    {
        bool ok = TradeLogLedgerNormalizer.TryCalculateTradeAmount("买入", priceText, quantityText, out double? amount, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(amount);
    }

    [Fact]
    public void LedgerNormalizer_AmountCalculationReturnsErrorForInvalidEditorNumber()
    {
        bool ok = TradeLogLedgerNormalizer.TryCalculateTradeAmount("买入", "abc", "1000", out double? amount, out string? error);

        Assert.False(ok);
        Assert.Null(amount);
        Assert.Contains("数值格式无效", error);
    }

    [Fact]
    public void LedgerNormalizer_TryNormalizeReturnsErrorForHalfFilledRowWithoutThrowing()
    {
        var records = new[]
        {
            new TradeLogRecord
            {
                Time = "2026-06-14 09:30:00",
                StrategyCode = "159941",
                Action = "买入",
                Price = 1.2345
            }
        };

        bool ok = TradeLogLedgerNormalizer.TryNormalizeLedgerFieldsBeforeSave(records, Array.Empty<MarketQuoteRecord>(), out string? error);

        Assert.False(ok);
        Assert.Contains("数量必须大于 0", error);
    }

    [Fact]
    public void TradeLogSnapshotDelete_RemovesDeletedDatabaseRecord()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_delete_sync_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            var records = Enumerable.Range(1, 20)
                .Select(index => Log($"2026-06-14 09:{index:00}:00", "CASH", "入金", amount: index))
                .ToArray();
            TradeLogLedgerNormalizer.NormalizeLedgerFieldsBeforeSave(records);
            repository.SaveTradeLogsSnapshot(Array.Empty<long>(), records);

            TradeLogRecord[] saved = repository.ReadTradeLogs().OrderBy(record => record.Id).ToArray();
            long deletedId = saved[18].Id;
            TradeLogRecord[] remaining = saved.Where(record => record.Id != deletedId).ToArray();
            TradeLogLedgerNormalizer.NormalizeLedgerFieldsBeforeSave(remaining);
            repository.SaveTradeLogsSnapshot(new[] { deletedId }, remaining);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            TradeLogRecord[] afterDelete = reopened.ReadTradeLogs().OrderBy(record => record.Id).ToArray();
            Assert.Equal(19, afterDelete.Length);
            Assert.DoesNotContain(afterDelete, record => record.Id == deletedId);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void TradeLogDelete_RecalculatesReplayCash()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_delete_replay_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            TradeLogRecord[] records =
            {
                Log("2026-06-14 09:00:00", "CASH", "入金", amount: 100000),
                Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", price: 1, quantity: 1000, amount: 1000, source: "场内ETF"),
                Log("2026-06-14 10:00:00", "CASH", "入金", amount: 5000)
            };
            TradeLogLedgerNormalizer.NormalizeLedgerFieldsBeforeSave(records);
            repository.SaveTradeLogsSnapshot(Array.Empty<long>(), records);
            AccountReplayStateRecord initial = ReplayAndPersist(repository);
            Assert.Equal(104000, initial.CashBalance!.Value, 2);

            long buyId = repository.ReadTradeLogs().Single(record => record.Action == "买入").Id;
            TradeLogRecord[] remaining = repository.ReadTradeLogs().Where(record => record.Id != buyId).ToArray();
            TradeLogLedgerNormalizer.NormalizeLedgerFieldsBeforeSave(remaining);
            repository.SaveTradeLogsSnapshot(new[] { buyId }, remaining);
            AccountReplayStateRecord afterDelete = ReplayAndPersist(repository);

            Assert.Equal(2, repository.ReadTradeLogs().Count);
            Assert.Equal(105000, afterDelete.CashBalance!.Value, 2);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void LedgerNormalizer_DoesNotHideSellOverPosition()
    {
        TradeLogRecord[] records =
        {
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", price: 1, quantity: 1000, source: "场内ETF"),
            Log("2026-06-14 10:30:00", "159941", "卖出", actualCode: "159941", price: 1, quantity: 2000, source: "场内ETF")
        };

        ReplaySnapshot snapshot = NormalizeSaveAndReplay(records);

        Assert.Equal("财务异常", snapshot.Account!.ReplayStatus);
        Assert.Contains("卖出数量", snapshot.Account.ReplayError);
    }

    [Fact]
    public void ReplayPositionCost_DeductsAverageCostAfterSell()
    {
        TradeLogRecord[] records =
        {
            Log("2026-06-14 09:00:00", "CASH", "入金", amount: 100000),
            Log("2026-06-14 09:30:00", "159941", "买入", actualCode: "159941", price: 1, quantity: 1000, amount: 1000, source: "场内ETF"),
            Log("2026-06-14 10:00:00", "159941", "买入", actualCode: "159941", price: 1.573, quantity: 600, amount: 943.80, source: "场内ETF"),
            Log("2026-06-14 10:30:00", "159941", "卖出", actualCode: "159941", price: 1.639, quantity: 620, amount: 1016.18, source: "场内ETF")
        };

        ReplaySnapshot snapshot = NormalizeSaveAndReplay(records);
        PositionReplayStateRecord position = Assert.Single(snapshot.Positions);
        double expectedCost = 1943.80 - (1943.80 / 1600.0 * 620.0);
        double expectedAverageCost = expectedCost / 980.0;
        EtfPositionCostMetrics metrics = EtfDecisionTableMetrics.CalculatePositionCostMetrics(
            snapshot.Positions,
            snapshot.OtcPositions);

        Assert.Equal(980, position.Quantity, 4);
        Assert.Equal(expectedCost, position.CostAmount, 4);
        Assert.Equal(expectedAverageCost, position.AverageCost, 4);
        Assert.Equal(position.AverageCost, metrics.AverageCost, 4);
        Assert.NotEqual(1943.80, position.CostAmount, 2);
    }

    private static ReplaySnapshot ReplayThroughTempDb(params TradeLogRecord[] records)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_account_replay_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            foreach (TradeLogRecord record in records)
            {
                repository.SaveTradeLog(record);
            }

            var service = new AccountReplayService();
            AccountReplayResult result = service.Replay(repository.ReadTradeLogs(), repository.ReadMarketQuoteCache(), new DateTime(2026, 6, 14));
            repository.SaveAccountReplayResult(result);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            return new ReplaySnapshot(
                result,
                reopened.ReadLatestAccountReplayState(),
                reopened.ReadTradeLogs(),
                reopened.ReadPositionReplayStates(),
                reopened.ReadOtcPositionReplayStates());
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    private static ReplaySnapshot NormalizeSaveAndReplay(IReadOnlyList<TradeLogRecord> records, IReadOnlyList<long>? idsToDelete = null)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_normalized_replay_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            TradeLogLedgerNormalizer.AutoCalculateTradeAmounts(records);
            TradeLogLedgerNormalizer.NormalizeLedgerFieldsBeforeSave(records, repository.ReadMarketQuoteCache());
            repository.SaveTradeLogsSnapshot(idsToDelete ?? Array.Empty<long>(), records);

            var service = new AccountReplayService();
            AccountReplayResult result = service.Replay(repository.ReadTradeLogs(), repository.ReadMarketQuoteCache(), new DateTime(2026, 6, 14));
            repository.SaveAccountReplayResult(result);

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            return new ReplaySnapshot(
                result,
                reopened.ReadLatestAccountReplayState(),
                reopened.ReadTradeLogs(),
                reopened.ReadPositionReplayStates(),
                reopened.ReadOtcPositionReplayStates());
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    private static AccountReplayStateRecord ReplayAndPersist(LocalDataRepository repository)
    {
        var service = new AccountReplayService();
        AccountReplayResult result = service.Replay(repository.ReadTradeLogs(), repository.ReadMarketQuoteCache(), new DateTime(2026, 6, 14));
        repository.SaveAccountReplayResult(result);
        return repository.ReadLatestAccountReplayState()!;
    }

    private static TradeLogRecord Log(
        string time,
        string strategyCode,
        string action,
        string? actualCode = null,
        double price = 0,
        double quantity = 0,
        double amount = 0,
        string? tier = null,
        string? source = null,
        double fee = 0,
        double netCashImpact = 0,
        double principal = 0,
        double cashBalance = 0,
        double totalAssets = 0)
    {
        return new TradeLogRecord
        {
            Time = time,
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Action = action,
            Price = price,
            Quantity = quantity,
            Amount = amount,
            Tier = tier,
            Source = source,
            Fee = fee,
            NetCashImpact = netCashImpact,
            Principal = principal,
            CashBalance = cashBalance,
            TotalAssets = totalAssets
        };
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ReplaySnapshot(
        AccountReplayResult Result,
        AccountReplayStateRecord? Account,
        IReadOnlyList<TradeLogRecord> TradeLogs,
        IReadOnlyList<PositionReplayStateRecord> Positions,
        IReadOnlyList<OtcPositionReplayStateRecord> OtcPositions);
}
