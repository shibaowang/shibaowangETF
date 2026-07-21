using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Tests.AccountReplay;

public sealed class PositionDailyPnlCostBasisTests
{
    private static readonly DateTime Today = new(2026, 7, 21, 9, 31, 0);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, -5)]
    public void TodayBuyAtCurrentPrice_UsesTradeCostAndFee(double fee, double expectedDailyPnl)
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.559, lastClose: 1.548, quoteTime: "2026-07-21 09:30:30"),
            Log("2026-07-21 09:30:00", "买入", price: 1.559, quantity: 32000, amount: 49888, fee: fee));

        PositionReplayStateRecord position = Assert.Single(result.Positions);
        Assert.Equal(expectedDailyPnl, position.DailyPnl!.Value, 2);
        Assert.NotEqual((1.559 - 1.548) * 32000, position.DailyPnl.Value, 2);
        Assert.Equal(49888, position.MarketValue!.Value, 2);
        Assert.Equal(0, position.UnrealizedPnl!.Value, 2);
        Assert.Equal(position.UnrealizedPnl, position.TotalPnl);
    }

    [Fact]
    public void OvernightHoldingWithoutTrade_UsesLastCloseOpeningValue()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.559, lastClose: 1.549),
            Log("2026-07-20 10:00:00", "买入", price: 1.500, quantity: 32000, amount: 48000));

        PositionReplayStateRecord position = Assert.Single(result.Positions);
        Assert.Equal(320, position.DailyPnl!.Value, 2);
        Assert.Equal(1888, position.UnrealizedPnl!.Value, 2);
    }

    [Fact]
    public void OvernightHoldingWithTodayAdd_SeparatesOpeningAndTradeCost()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.57, lastClose: 1.55),
            Log("2026-07-20 10:00:00", "买入", quantity: 1000, amount: 1400),
            Log("2026-07-21 09:30:00", "买入", quantity: 500, amount: 780, fee: 1));

        PositionReplayStateRecord position = Assert.Single(result.Positions);
        Assert.Equal(24, position.DailyPnl!.Value, 2);
        Assert.Equal(175, position.UnrealizedPnl!.Value, 2);
        Assert.Equal(500, position.TodayBuyQuantity, 4);
        Assert.Equal(780, position.TodayBuyAmount, 2);
    }

    [Fact]
    public void TodayPartialSell_IncludesNetSellCashImpact()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.58, lastClose: 1.55),
            Log("2026-07-20 10:00:00", "买入", quantity: 1000, amount: 1400),
            Log("2026-07-21 10:00:00", "卖出", quantity: 400, amount: 628, fee: 1));

        Assert.Equal(25, Assert.Single(result.Positions).DailyPnl!.Value, 2);
    }

    [Fact]
    public void TodayFullSell_KeepsDailyPnlWhenEndingQuantityIsZero()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.58, lastClose: 1.55),
            Log("2026-07-20 10:00:00", "买入", quantity: 1000, amount: 1400),
            Log("2026-07-21 10:00:00", "卖出", quantity: 1000, amount: 1570, fee: 1));

        PositionReplayStateRecord position = Assert.Single(result.Positions);
        Assert.Equal(0, position.Quantity, 4);
        Assert.Equal(19, position.DailyPnl!.Value, 2);
    }

    [Fact]
    public void TodayRoundTrip_UsesActualBuyAndSellCashFlows()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.57, lastClose: 1.54),
            Log("2026-07-21 09:30:00", "买入", quantity: 1000, amount: 1559, fee: 1),
            Log("2026-07-21 10:30:00", "卖出", quantity: 1000, amount: 1570, fee: 1));

        Assert.Equal(9, Assert.Single(result.Positions).DailyPnl!.Value, 2);
    }

    [Fact]
    public void MultipleTodayTrades_PreserveEveryCashFlowAndFee()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.58, lastClose: 1.54),
            Log("2026-07-21 09:30:00", "买入", quantity: 500, amount: 750, fee: 1),
            Log("2026-07-21 09:45:00", "买入", quantity: 500, amount: 775, fee: 1),
            Log("2026-07-21 10:30:00", "卖出", quantity: 200, amount: 320, fee: 0.5),
            Log("2026-07-21 10:45:00", "卖出", quantity: 200, amount: 320, fee: 0.5));

        Assert.Equal(60, Assert.Single(result.Positions).DailyPnl!.Value, 2);
    }

    [Fact]
    public void TodayDividendIsIncludedButFundingIsNotPositionPnl()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.55, lastClose: 1.55),
            Log("2026-07-20 10:00:00", "买入", quantity: 1000, amount: 1400),
            Log("2026-07-21 08:00:00", "分红", amount: 50),
            Funding("2026-07-21 08:30:00", "入金", amount: 10000),
            Funding("2026-07-21 08:45:00", "出金", amount: 2000));

        Assert.Equal(50, Assert.Single(result.Positions).DailyPnl!.Value, 2);
    }

    [Fact]
    public void ExplicitNetCashImpactTakesPriority()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.559, lastClose: 1.548),
            Log("2026-07-21 09:30:00", "买入", quantity: 32000, amount: 49888, fee: 5, netCashImpact: -49890));

        Assert.Equal(-2, Assert.Single(result.Positions).DailyPnl!.Value, 2);
    }

    [Fact]
    public void PreviousDayQuoteIsUnavailableEvenWhenReceivedToday()
    {
        MarketQuoteRecord quote = Quote(price: 1.559, lastClose: 1.548, quoteTime: "2026-07-20 15:00:00");
        quote.ReceivedAt = "2026-07-21 09:30:30";

        AccountReplayResult result = Replay(
            Today,
            quote,
            Log("2026-07-21 09:30:00", "买入", quantity: 32000, amount: 49888));

        Assert.Null(Assert.Single(result.Positions).DailyPnl);
    }

    [Theory]
    [InlineData(null, 1.548)]
    [InlineData(1.559, null)]
    public void MissingPriceOrLastCloseIsUnavailable(double? price, double? lastClose)
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price, lastClose),
            Log("2026-07-21 09:30:00", "买入", quantity: 32000, amount: 49888));

        Assert.Null(Assert.Single(result.Positions).DailyPnl);
    }

    [Fact]
    public void TodayCorporateActionReturnsUnavailableInsteadOfGuessing()
    {
        AccountReplayResult result = Replay(
            Today,
            Quote(price: 1.58, lastClose: 1.55),
            Log("2026-07-20 10:00:00", "买入", quantity: 1000, amount: 1400),
            Log("2026-07-21 08:00:00", "送股", quantity: 100));

        Assert.Null(Assert.Single(result.Positions).DailyPnl);
    }

    [Fact]
    public void CalculatedDailyPnlPersistsInExistingReplayStateColumn()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_daily_pnl_{Guid.NewGuid():N}.db");
        try
        {
            AccountReplayResult result = Replay(
                Today,
                Quote(price: 1.559, lastClose: 1.548),
                Log("2026-07-21 09:30:00", "买入", quantity: 32000, amount: 49888, fee: 3));
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));

            repository.SaveAccountReplayResult(result);

            Assert.Equal(-3, Assert.Single(repository.ReadPositionReplayStates()).DailyPnl!.Value, 2);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-shm");
            TryDelete(databasePath + "-wal");
        }
    }

    [Fact]
    public void TopAggregateAndStrategyRowUseTheSameReplayDailyPnl()
    {
        MarketQuoteRecord quote = Quote(price: 1.559, lastClose: 1.548);
        AccountReplayResult result = Replay(
            Today,
            quote,
            Log("2026-07-21 09:30:00", "买入", quantity: 32000, amount: 49888));

        double? top = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(result.Positions, new[] { quote }, Today);
        double? row = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            result.Positions.Where(position => position.StrategyCode == "159941"),
            new[] { quote },
            Today);

        Assert.Equal(0, top!.Value, 2);
        Assert.Equal(top, row);
    }

    private static AccountReplayResult Replay(
        DateTime now,
        MarketQuoteRecord quote,
        params TradeLogRecord[] records)
        => new AccountReplayService().Replay(records, new[] { quote }, now);

    private static TradeLogRecord Log(
        string time,
        string action,
        double price = 0,
        double quantity = 0,
        double amount = 0,
        double fee = 0,
        double netCashImpact = 0)
        => new()
        {
            Time = time,
            StrategyCode = "159941",
            ActualCode = "159941",
            Action = action,
            Source = "场内ETF",
            Price = price,
            Quantity = quantity,
            Amount = amount,
            Fee = fee,
            NetCashImpact = netCashImpact
        };

    private static TradeLogRecord Funding(string time, string action, double amount)
        => new()
        {
            Time = time,
            StrategyCode = "CASH",
            Action = action,
            Amount = amount
        };

    private static MarketQuoteRecord Quote(
        double? price,
        double? lastClose,
        string quoteTime = "2026-07-21 09:30:30")
        => new()
        {
            Symbol = "159941",
            MarketType = "ETF",
            Source = "TENCENT_QT",
            Price = price,
            LastClose = lastClose,
            QuoteTime = quoteTime,
            ReceivedAt = quoteTime
        };

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
