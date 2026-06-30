using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Strategy;

public class BasePositionSettingsCloseoutTests
{
    private readonly StrategyDecisionService _service = new();

    [Fact]
    public void DefaultMissingSettingsUseTwentyPercentRatio()
    {
        StrategyDecisionStateRecord decision = Calculate(cost: 15000, cash: 100000).Single();

        Assert.Equal(BasePositionSettings.RatioMode, decision.BaseMode);
        Assert.Equal(0.20, decision.BaseRatio!.Value, 4);
        Assert.Equal(20000, decision.BaseTargetAmount);
        Assert.Equal(5000, decision.BaseGapAmount);
        Assert.Equal(0.75, decision.BaseCompletionRate!.Value, 4);
    }

    [Fact]
    public void RatioThirtyPercentChangesBaseTarget()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 25000,
            cash: 100000,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal(30000, decision.BaseTargetAmount);
        Assert.Equal(5000, decision.BaseGapAmount);
        Assert.Equal(0.30, decision.BaseRatio!.Value, 4);
    }

    [Theory]
    [InlineData("20%", 0.20)]
    [InlineData("20", 0.20)]
    [InlineData("0.20", 0.20)]
    public void RatioInputAcceptsPercentWholeNumberAndDecimal(string input, double expected)
    {
        Assert.True(BasePositionSettingsService.TryParseRatio(input, out double ratio, out string? error), error);

        Assert.Equal(expected, ratio, 4);
    }

    [Fact]
    public void FixedAmountChangesBaseTarget()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 25000,
            cash: 100000,
            settings: BasePositionSettingsService.CreateAmount(30000)).Single();

        Assert.Equal(BasePositionSettings.AmountMode, decision.BaseMode);
        Assert.Equal(30000, decision.BaseFixedAmount);
        Assert.Equal(30000, decision.BaseTargetAmount);
        Assert.Equal(5000, decision.BaseGapAmount);
    }

    [Fact]
    public void FixedAmountGreaterThanPrincipalIsCapped()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 0,
            cash: 100000,
            settings: BasePositionSettingsService.CreateAmount(120000)).Single();

        Assert.Equal(100000, decision.BaseTargetAmount);
        Assert.True(decision.BaseTargetCapped);
        Assert.Equal(0, decision.RealSniperPool);
    }

    [Fact]
    public void CompletionUsesCurrentCostDividedByBaseTarget()
    {
        double completion = BasePositionSettingsService.CalculateCompletionRate(27550.78, 25715.64);

        Assert.Equal(27550.78 / 25715.64, completion, 6);
    }

    [Fact]
    public void RatioChangeAffectsStrategicBaseDecision()
    {
        StrategyDecisionStateRecord twentyPercent = Calculate(
            cost: 25000,
            cash: 100000,
            settings: BasePositionSettingsService.CreateRatio(0.20)).Single();
        StrategyDecisionStateRecord thirtyPercent = Calculate(
            cost: 25000,
            cash: 100000,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal(0, twentyPercent.BaseGapAmount);
        Assert.Equal(5000, thirtyPercent.BaseGapAmount);
        Assert.Equal(5000, thirtyPercent.TargetAmount);
    }

    [Fact]
    public void FixedAmountCanCreateStrategicBaseGap()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 10000,
            cash: 100000,
            settings: BasePositionSettingsService.CreateAmount(15000)).Single();

        Assert.Equal(15000, decision.BaseTargetAmount);
        Assert.Equal(5000, decision.BaseGapAmount);
        Assert.Equal(5000, decision.TargetAmount);
    }

    [Fact]
    public void SellProtectionUsesDynamicBaseTarget()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 30000,
            marketValue: 50000,
            cash: 100000,
            sellRatio: 0.10,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Null(decision.TargetAmount);
        Assert.False(decision.IsActionable);
        Assert.Equal(1.0, decision.BaseCompletionRate!.Value, 4);
    }

    [Fact]
    public void MainSniperPoolUsesCashBalanceWhenDynamicBaseIsComplete()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 30000,
            cash: 100000,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal(0, decision.BaseGapAmount);
        Assert.Equal(100000, decision.RealSniperPool);
    }

    [Fact]
    public void MainSniperPoolSubtractsDynamicBaseGap()
    {
        StrategyDecisionStateRecord decision = Calculate(
            cost: 25000,
            cash: 100000,
            settings: BasePositionSettingsService.CreateRatio(0.30)).Single();

        Assert.Equal(5000, decision.BaseGapAmount);
        Assert.Equal(95000, decision.RealSniperPool);
    }

    [Fact]
    public void RepositoryRoundTripsBasePositionSettings()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_base_position_{Guid.NewGuid():N}.db");

        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            repository.SaveBasePositionSettings(BasePositionSettingsService.CreateAmount(30000, 0.25));

            var reopened = new LocalDataRepository(new LocalDatabase(databasePath));
            BasePositionSettings settings = reopened.ReadBasePositionSettings();

            Assert.Equal(BasePositionSettings.AmountMode, settings.Mode);
            Assert.Equal(0.25, settings.Ratio, 4);
            Assert.Equal(30000, settings.FixedAmount);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void RepositoryReadsRawPercentTextAndNormalizes()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_base_position_raw_{Guid.NewGuid():N}.db");

        try
        {
            var database = new LocalDatabase(databasePath);
            database.Initialize();
            using (var connection = database.OpenConnection())
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO app_settings(key, value, updated_at)
                    VALUES('base_position_ratio', '20%', '2026-06-14 12:00:00');
                    """;
                command.ExecuteNonQuery();
            }

            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            BasePositionSettings settings = repository.ReadBasePositionSettings();

            Assert.Equal(0.20, settings.Ratio, 4);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void InvalidRatioInputReturnsFalseWithoutThrowing()
    {
        bool parsed = BasePositionSettingsService.TryParseRatio("abc%", out _, out string? error);

        Assert.False(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private StrategyDecisionStateRecord[] Calculate(
        double cost,
        double cash,
        double price = 1.0,
        double iopv = 1.0,
        double? marketValue = null,
        double? sellRatio = null,
        BasePositionSettings? settings = null)
    {
        var strategy = new StrategyConfigRecord
        {
            Code = "159941",
            Name = "Strategy",
            IndexSecId = "100.NDX100",
            SellRatio = sellRatio,
            Enabled = true
        };

        var input = new StrategyDecisionCalculationInput
        {
            Strategies = new[] { strategy },
            AccountReplayState = new AccountReplayStateRecord
            {
                ReplayStatus = "正常",
                CashBalance = cash,
                Principal = 100000,
                TotalPositionCost = cost
            },
            PositionReplayStates = new[]
            {
                new PositionReplayStateRecord
                {
                    StrategyCode = "159941",
                    ActualCode = "159941",
                    Source = "场内ETF",
                    Quantity = cost > 0 ? cost / price : 0,
                    CostAmount = cost,
                    AverageCost = price,
                    MarketPrice = price,
                    MarketValue = marketValue
                }
            },
            TradeLogs = Array.Empty<TradeLogRecord>(),
            MarketQuotes = new[]
            {
                Quote("159941", "ETF", price, iopv),
                Quote("100.NDX100", "INDEX", 100, null),
                Quote("251.NDXTMC", "INDEX", 100, null)
            },
            MarketHistory = Array.Empty<MarketQuoteRecord>(),
            BasePositionSettings = settings ?? BasePositionSettings.Default()
        };

        return _service.Calculate(input).Decisions.ToArray();
    }

    private static MarketQuoteRecord Quote(string symbol, string marketType, double price, double? iopv)
    {
        return new MarketQuoteRecord
        {
            Symbol = symbol,
            MarketType = marketType,
            Source = "TEST",
            Price = price,
            Iopv = iopv,
            ReceivedAt = "2026-06-14 12:00:00"
        };
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
