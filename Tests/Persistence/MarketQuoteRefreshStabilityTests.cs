using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Persistence;

public sealed class MarketQuoteRefreshStabilityTests
{
    private static readonly DateTime Today = new(2026, 7, 21, 10, 0, 0);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, -5)]
    public void ValidTodayQuote_SurvivesNullOldAndIncompleteRefreshes(double fee, double expectedDailyPnl)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cross_etf_quote_stability_{Guid.NewGuid():N}.db");
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase(databasePath));
            MarketQuoteRecord[] sequence =
            {
                Quote(1.559, 1.548, "2026-07-21 09:30:30", "2026-07-21 09:30:31"),
                Quote(1.559, 1.548, null, "2026-07-21 09:31:00"),
                Quote(1.559, 1.548, "2026-07-20 15:00:00", "2026-07-21 09:32:00"),
                Quote(1.559, null, "2026-07-21 09:33:00", "2026-07-21 09:33:01"),
                Quote(1.559, 1.548, "2026-07-21 09:34:00", "2026-07-21 09:34:01")
            };

            foreach (MarketQuoteRecord incoming in sequence)
            {
                repository.SaveMarketQuote(incoming);
                MarketQuoteRecord cached = Assert.Single(repository.ReadMarketQuoteCache());
                AccountReplayResult replay = new AccountReplayService().Replay(
                    new[] { Buy(fee) },
                    new[] { cached },
                    Today);

                Assert.Equal(expectedDailyPnl, Assert.Single(replay.Positions).DailyPnl!.Value, 2);
            }

            MarketQuoteRecord final = Assert.Single(repository.ReadMarketQuoteCache());
            Assert.Equal("2026-07-21 09:34:00", final.QuoteTime);
            Assert.Equal(1.548, final.LastClose);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void Selector_PrefersValidQuoteTimeOverLaterReceivedInvalidQuote()
    {
        MarketQuoteRecord valid = Quote(1.559, 1.548, "2026-07-21 09:30:30", "2026-07-21 09:30:31");
        MarketQuoteRecord invalid = Quote(1.600, 1.548, null, "2026-07-21 09:35:00");
        invalid.Source = "SECONDARY";

        MarketQuoteRecord? selected = MarketQuoteFreshnessSelector.SelectBest(
            new[] { invalid, valid },
            "159941",
            "ETF");

        Assert.Same(valid, selected);
    }

    [Fact]
    public void ReplaySignature_IgnoresReceivedAtOnlyChanges()
    {
        MarketQuoteRecord first = Quote(1.559, 1.548, "2026-07-21 09:30:30", "2026-07-21 09:30:31");
        MarketQuoteRecord second = Quote(1.559, 1.548, "2026-07-21 09:30:30", "2026-07-21 09:35:00");

        string firstSignature = MarketQuoteFreshnessSelector.BuildReplayQuoteSignature(new[] { first }, Today);
        string secondSignature = MarketQuoteFreshnessSelector.BuildReplayQuoteSignature(new[] { second }, Today);

        Assert.Equal(firstSignature, secondSignature);
        Assert.DoesNotContain("09:35:00", secondSignature, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaySignature_ChangesForQuoteTimeAndBeijingNaturalDay()
    {
        MarketQuoteRecord first = Quote(1.559, 1.548, "2026-07-21 09:30:30", "2026-07-21 09:30:31");
        MarketQuoteRecord second = Quote(1.559, 1.548, "2026-07-21 09:31:30", "2026-07-21 09:31:31");

        string baseline = MarketQuoteFreshnessSelector.BuildReplayQuoteSignature(new[] { first }, Today);
        string quoteChanged = MarketQuoteFreshnessSelector.BuildReplayQuoteSignature(new[] { second }, Today);
        string dayChanged = MarketQuoteFreshnessSelector.BuildReplayQuoteSignature(new[] { first }, Today.AddDays(1));

        Assert.NotEqual(baseline, quoteChanged);
        Assert.NotEqual(baseline, dayChanged);
        Assert.Contains("2026-07-22", dayChanged, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviousNaturalDayQuote_ClearsDailyPnlOnReplay()
    {
        MarketQuoteRecord quote = Quote(1.559, 1.548, "2026-07-21 15:00:00", "2026-07-22 09:00:00");

        AccountReplayResult result = new AccountReplayService().Replay(
            new[] { Buy(0) },
            new[] { quote },
            Today.AddDays(1));

        Assert.Null(Assert.Single(result.Positions).DailyPnl);
    }

    private static MarketQuoteRecord Quote(
        double? price,
        double? lastClose,
        string? quoteTime,
        string receivedAt)
        => new()
        {
            Symbol = "159941",
            DisplayName = "NASDAQ ETF",
            MarketType = "ETF",
            Source = "TENCENT_QT",
            Price = price,
            LastClose = lastClose,
            ChangeValue = price.HasValue && lastClose.HasValue ? price - lastClose : null,
            ChangePercent = price.HasValue && lastClose > 0 ? (price - lastClose) / lastClose : null,
            QuoteTime = quoteTime,
            ReceivedAt = receivedAt
        };

    private static TradeLogRecord Buy(double fee)
        => new()
        {
            Time = "2026-07-21 09:30:00",
            StrategyCode = "159941",
            ActualCode = "159941",
            Action = "买入",
            Source = "场内ETF",
            Price = 1.559,
            Quantity = 32000,
            Amount = 49888,
            Fee = fee
        };

    private static void DeleteDatabase(string path)
    {
        foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
