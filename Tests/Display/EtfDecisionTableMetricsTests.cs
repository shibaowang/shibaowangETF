using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class EtfDecisionTableMetricsTests
{
    [Fact]
    public void CalculatePremiumRate_UsesPriceAndIopv()
    {
        var quote = new MarketQuoteRecord
        {
            Symbol = "159941",
            MarketType = "ETF",
            Price = 1.05,
            Iopv = 1.00
        };

        Assert.Equal(0.05, EtfDecisionTableMetrics.CalculatePremiumRate(quote)!.Value, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalculatePremiumRate_ReturnsEmptyWhenIopvMissingOrInvalid(double iopv)
    {
        var quote = new MarketQuoteRecord
        {
            Symbol = "159941",
            MarketType = "ETF",
            Price = 1.05,
            Iopv = iopv
        };

        Assert.Null(EtfDecisionTableMetrics.CalculatePremiumRate(quote));
    }

    [Fact]
    public void CalculateCompositeCost_UsesMarketReplayAndOtcReplayCost()
    {
        var replayPositions = new[]
        {
            new PositionReplayStateRecord { StrategyCode = "159941", Source = "场内ETF", CostAmount = 1000 },
            new PositionReplayStateRecord { StrategyCode = "159941", Source = "场外替代", CostAmount = 5000 }
        };
        var otcPositions = new[]
        {
            new OtcPositionReplayStateRecord { StrategyCode = "159941", CostAmount = 600 }
        };

        Assert.Equal(1600, EtfDecisionTableMetrics.CalculateCompositeCost(replayPositions, otcPositions), 2);
    }

    [Fact]
    public void CalculatePositionCostMetrics_UsesAverageCostForCompositeCostDisplay()
    {
        var replayPositions = new[]
        {
            new PositionReplayStateRecord
            {
                StrategyCode = "159941",
                Source = "场内ETF",
                Quantity = 3900,
                CostAmount = 5809,
                AverageCost = 5809.0 / 3900.0
            }
        };

        EtfPositionCostMetrics metrics = EtfDecisionTableMetrics.CalculatePositionCostMetrics(
            replayPositions,
            Array.Empty<OtcPositionReplayStateRecord>());

        Assert.Equal(3900, metrics.TotalQuantity, 4);
        Assert.Equal(5809, metrics.TotalCostAmount, 2);
        Assert.Equal(5809.0 / 3900.0, metrics.AverageCost, 6);
        Assert.NotEqual(metrics.TotalCostAmount, metrics.AverageCost);
    }

    [Fact]
    public void CalculatePrincipalRatio_UsesTotalCostAmountInsteadOfAverageCost()
    {
        double? ratio = EtfDecisionTableMetrics.CalculatePrincipalRatio(5809, 100000);

        Assert.Equal(5809.0 / 100000.0, ratio!.Value, 8);
        Assert.NotEqual(1.489 / 100000.0, ratio.Value, 8);
    }

    [Fact]
    public void CalculateHoldingPnlAndReturn_UseTotalCostAmountInsteadOfAverageCost()
    {
        double? pnl = EtfDecisionTableMetrics.CalculateHoldingPnl(6500, 5809);
        double? returnRate = EtfDecisionTableMetrics.CalculateHoldingReturnRate(pnl, 5809);

        Assert.Equal(691, pnl!.Value, 4);
        Assert.Equal(691.0 / 5809.0, returnRate!.Value, 8);
        Assert.NotEqual(6500 - 1.489, pnl.Value, 4);
    }

    [Theory]
    [InlineData("2026-07-03 00:00:00", "2026-07-03 12:00:00", true)]
    [InlineData("2026-07-03 23:59:59", "2026-07-03 12:00:00", true)]
    [InlineData("2026-07-04 00:00:00", "2026-07-03 12:00:00", false)]
    [InlineData("2026-07-02 23:59:59", "2026-07-03 12:00:00", false)]
    public void IsPnLEventInNaturalDay_UsesHalfOpenBeijingNaturalDay(
        string eventTime,
        string now,
        bool expected)
    {
        Assert.Equal(
            expected,
            EtfDecisionTableMetrics.IsPnLEventInNaturalDay(DateTime.Parse(eventTime), DateTime.Parse(now)));
    }

    [Theory]
    [InlineData("2026-07-03 09:30:00", "2026-07-02", "2026-07-02 20:00:00", false)]
    [InlineData("2026-07-03 20:10:00", "2026-07-02", "2026-07-03 20:00:00", true)]
    [InlineData("2026-07-04 09:30:00", "2026-07-02", "2026-07-03 20:00:00", false)]
    [InlineData("2026-07-06 09:30:00", "2026-07-03", "2026-07-03 20:00:00", false)]
    [InlineData("2026-07-06 20:10:00", "2026-07-03", "2026-07-06 20:00:00", true)]
    [InlineData("2026-07-07 09:30:00", "2026-07-03", "2026-07-06 20:00:00", false)]
    public void CalculateNaturalDayValuationDailyPnl_HandlesSinaFundNavByNaturalDayEvent(
        string now,
        string quoteTime,
        string receivedAt,
        bool expectedIncluded)
    {
        var positions = new[]
        {
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", null, quantity: 777.31)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "000834",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 6.3084,
                LastClose = 6.4131,
                QuoteTime = quoteTime,
                ReceivedAt = receivedAt
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            DateTime.Parse(now));

        if (expectedIncluded)
        {
            Assert.Equal(-81.38, dailyPnl!.Value, 2);
        }
        else
        {
            Assert.Null(dailyPnl);
        }
    }

    [Theory]
    [InlineData("2026-07-03 09:30:00", "2026-07-03 09:30:00", "2026-07-03 09:30:00", true)]
    [InlineData("2026-07-04 07:34:00", "2026-07-03 15:00:00", "2026-07-04 07:34:00", false)]
    [InlineData("2026-07-04 07:34:00", null, "2026-07-04 07:34:00", false)]
    public void CalculateNaturalDayValuationDailyPnl_HandlesEtfQuoteBySameNaturalDay(
        string now,
        string? quoteTime,
        string receivedAt,
        bool expectedIncluded)
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.622,
                LastClose = 1.614,
                QuoteTime = quoteTime,
                ReceivedAt = receivedAt
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            DateTime.Parse(now));

        if (expectedIncluded)
        {
            Assert.Equal(31.20, dailyPnl!.Value, 2);
        }
        else
        {
            Assert.Null(dailyPnl);
        }
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ExcludesWeekendEtfQuotesWhenQuoteTimeIsPreviousTradingDay()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900),
            ReplayPosition("159513", "159513", "\u573a\u5185ETF", null, quantity: 3100)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.622,
                LastClose = 1.614,
                QuoteTime = "2026-07-03 16:14:51",
                ReceivedAt = "2026-07-04 07:34:00"
            },
            new MarketQuoteRecord
            {
                Symbol = "159513",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.776,
                LastClose = 1.770,
                QuoteTime = "2026-07-03 16:14:21",
                ReceivedAt = "2026-07-04 07:34:00"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 4, 7, 34, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_DoesNotFallbackEtfDailyPnlWhenOnlyReceivedAtIsToday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.622,
                LastClose = 1.614,
                QuoteTime = "2026-07-03 16:14:51",
                ReceivedAt = "2026-07-04 07:34:00"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 4, 7, 34, 0));

        Assert.Null(dailyPnl);
    }

    [Theory]
    [InlineData("2026-07-08 09:30:00", "2026-07-07 15:00:00", "2026-07-08 09:30:00", false)]
    [InlineData("2026-07-08 10:30:00", "2026-07-08 10:30:00", "2026-07-08 10:30:00", true)]
    [InlineData("2026-07-09 09:30:00", "2026-07-08 15:00:00", "2026-07-09 09:30:00", false)]
    [InlineData("2026-07-04 07:34:00", "2026-07-03 16:14:00", "2026-07-04 07:34:00", false)]
    [InlineData("2026-07-05 09:30:00", "2026-07-03 16:14:00", "2026-07-05 09:30:00", false)]
    [InlineData("2026-10-02 09:30:00", "2026-09-30 15:00:00", "2026-10-02 09:30:00", false)]
    [InlineData("2026-07-08 12:00:00", "2026-07-08 00:00:00", "2026-07-08 12:00:00", true)]
    [InlineData("2026-07-08 12:00:00", "2026-07-08 23:59:59", "2026-07-08 23:59:59", true)]
    [InlineData("2026-07-08 12:00:00", "2026-07-07 23:59:59", "2026-07-08 12:00:00", false)]
    [InlineData("2026-07-08 12:00:00", "2026-07-09 00:00:00", "2026-07-08 12:00:00", false)]
    public void CalculateNaturalDayValuationDailyPnl_FiltersEtfByQuoteTimeForAnyNaturalDay(
        string now,
        string quoteTime,
        string receivedAt,
        bool expectedIncluded)
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", 31.20, quantity: 3900)
        };
        var quotes = new[]
        {
            EtfQuote("159941", price: 1.622, lastClose: 1.614, quoteTime, receivedAt)
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            DateTime.Parse(now));

        if (expectedIncluded)
        {
            Assert.Equal(31.20, dailyPnl!.Value, 2);
        }
        else
        {
            Assert.Null(dailyPnl);
        }
    }

    [Theory]
    [InlineData("2026-07-08 10:30:00", "2026-07-08 10:30:00", "2026-07-08 10:30:00", true)]
    [InlineData("2026-07-08 09:30:00", "2026-07-07 15:00:00", "2026-07-08 09:30:00", false)]
    public void CalculateNaturalDayValuationDailyPnl_FallbackEtfDailyPnlStillRequiresQuoteTimeInNaturalDay(
        string now,
        string quoteTime,
        string receivedAt,
        bool expectedIncluded)
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900)
        };
        var quotes = new[]
        {
            EtfQuote("159941", price: 1.622, lastClose: 1.614, quoteTime, receivedAt)
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            DateTime.Parse(now));

        if (expectedIncluded)
        {
            Assert.Equal(31.20, dailyPnl!.Value, 2);
        }
        else
        {
            Assert.Null(dailyPnl);
        }
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_TopAndTableBothIgnorePreviousDayEtfQuote()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", 31.20, quantity: 3900),
            ReplayPosition("159513", "159513", "\u573a\u5185ETF", 18.60, quantity: 3100)
        };
        var quotes = new[]
        {
            EtfQuote("159941", price: 1.622, lastClose: 1.614, "2026-07-03 16:14:51", "2026-07-04 07:34:00"),
            EtfQuote("159513", price: 1.776, lastClose: 1.770, "2026-07-03 16:14:21", "2026-07-04 07:34:00")
        };

        double? topAggregate = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 4, 7, 34, 0));
        double? table159941 = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions.Where(position => position.StrategyCode == "159941"),
            quotes,
            new DateTime(2026, 7, 4, 7, 34, 0));
        double? table159513 = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions.Where(position => position.StrategyCode == "159513"),
            quotes,
            new DateTime(2026, 7, 4, 7, 34, 0));

        Assert.Null(topAggregate);
        Assert.Null(table159941);
        Assert.Null(table159513);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_TopAndTableBothIncludeCurrentDayEtfQuote()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900),
            ReplayPosition("159513", "159513", "\u573a\u5185ETF", null, quantity: 3100)
        };
        var quotes = new[]
        {
            EtfQuote("159941", price: 1.622, lastClose: 1.614, "2026-07-08 10:30:00", "2026-07-08 10:30:00"),
            EtfQuote("159513", price: 1.776, lastClose: 1.770, "2026-07-08 10:30:00", "2026-07-08 10:30:00")
        };

        double? topAggregate = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 8, 10, 31, 0));
        double? table159941 = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions.Where(position => position.StrategyCode == "159941"),
            quotes,
            new DateTime(2026, 7, 8, 10, 31, 0));
        double? table159513 = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions.Where(position => position.StrategyCode == "159513"),
            quotes,
            new DateTime(2026, 7, 8, 10, 31, 0));

        Assert.Equal(49.80, topAggregate!.Value, 2);
        Assert.Equal(31.20, table159941!.Value, 2);
        Assert.Equal(18.60, table159513!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesEtfUpdatedToday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", -297.00)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 15, 1, 0));

        Assert.Equal(-297.00, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ComputesEtfDailyPnlWhenStoredValueMissing()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.618,
                LastClose = 1.614,
                ReceivedAt = "2026-07-03 10:35:18",
                QuoteTime = "2026-07-03 10:35:18"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 10, 36, 0));

        Assert.Equal(15.60, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesEtfWhenOtcNavBelongsToYesterday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900),
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", -278.30)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.604,
                LastClose = 1.614,
                ReceivedAt = "2026-07-03 10:35:18",
                QuoteTime = "2026-07-03 10:35:18"
            },
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 2.8409,
                LastClose = 2.8755,
                ReceivedAt = "2026-07-03 10:35:07",
                QuoteTime = "2026-07-02"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 10, 36, 0));

        Assert.Equal(-39.00, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_TopAggregateMatchesValidTableItemsOnly()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", null, quantity: 3900),
            ReplayPosition("159513", "159513", "\u573a\u5185ETF", null, quantity: 3100),
            ReplayPosition("159660", "018966", "\u573a\u5916\u66ff\u4ee3", -121.08, quantity: 4711.38)
        };
        var otcPositions = new[]
        {
            OtcPosition("159660", "018966", -121.08)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "159941",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.623,
                LastClose = 1.614,
                QuoteTime = "2026-07-03 10:55:00",
                ReceivedAt = "2026-07-03 10:55:00"
            },
            new MarketQuoteRecord
            {
                Symbol = "159513",
                MarketType = "ETF",
                Source = "TENCENT_QT",
                Price = 1.780,
                LastClose = 1.770,
                QuoteTime = "2026-07-03 10:55:00",
                ReceivedAt = "2026-07-03 10:55:00"
            },
            new MarketQuoteRecord
            {
                Symbol = "018966",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 1.6688,
                LastClose = 1.6945,
                ReceivedAt = "2026-07-03 10:55:00",
                QuoteTime = "2026-07-02"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            otcPositions,
            quotes,
            new DateTime(2026, 7, 3, 10, 56, 0));

        Assert.Equal(66.10, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ExcludesOtcUpdatedYesterday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("017091", "OTC", "2026-07-01 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 14, 22, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesOtcUpdatedToday()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", -213.80),
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00"),
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(55.32, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ComputesSinaFundDailyPnlOnlyWhenQuoteTimeIsToday()
    {
        var positions = new[]
        {
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", null, quantity: 777.31)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "000834",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 6.4131,
                LastClose = 6.5145,
                ReceivedAt = "2026-07-03 20:05:00",
                QuoteTime = "2026-07-03"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 20, 6, 0));

        Assert.Equal(-78.82, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_DoesNotCarryTodayOtcIntoTomorrow()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 9, 0, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_RequiresValuationUpdateTime()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "OTC",
                ReceivedAt = string.Empty,
                QuoteTime = null
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_UsesOtcQuoteTimeBeforeReceivedAt()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "OTC",
                ReceivedAt = "2026-07-03 09:30:00",
                QuoteTime = "2026-07-02 00:00:00"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(269.12, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ExcludesSinaFundWhenQuoteTimeYesterdayButReceivedToday()
    {
        var positions = new[]
        {
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", -78.82)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "000834",
                MarketType = "OTC",
                Source = "SINA_FUND",
                ReceivedAt = "2026-07-03 09:33:23",
                QuoteTime = "2026-07-02"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 9, 40, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesSinaFundEveningNavWhenQuoteDateLags()
    {
        var positions = new[]
        {
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", null, quantity: 777.31)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "000834",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 6.3084,
                LastClose = 6.4131,
                ReceivedAt = "2026-07-03 19:55:38",
                QuoteTime = "2026-07-02"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 20, 5, 0));

        Assert.Equal(-81.38, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ExcludesOlderSinaFundEveningQuoteWhenNewerNavBatchExists()
    {
        var positions = new[]
        {
            ReplayPosition("159509", "017091", "\u573a\u5916\u66ff\u4ee3", 128.88)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 2.8409,
                LastClose = 2.8755,
                ReceivedAt = "2026-07-03 19:55:38",
                QuoteTime = "2026-07-01"
            },
            new MarketQuoteRecord
            {
                Symbol = "000834",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 6.3084,
                LastClose = 6.4131,
                ReceivedAt = "2026-07-03 19:55:38",
                QuoteTime = "2026-07-02"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 20, 5, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_DoesNotCarryEveningSinaFundNavIntoTomorrow()
    {
        var positions = new[]
        {
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", -81.37)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "000834",
                MarketType = "OTC",
                Source = "SINA_FUND",
                Price = 6.3084,
                LastClose = 6.4131,
                ReceivedAt = "2026-07-03 19:55:38",
                QuoteTime = "2026-07-02"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 4, 9, 0, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_ExcludesSinaFundWhenQuoteTimeMissingEvenIfReceivedToday()
    {
        var positions = new[]
        {
            ReplayPosition("159513", "000834", "\u573a\u5916\u66ff\u4ee3", -78.82)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "000834",
                MarketType = "OTC",
                Source = "SINA_FUND",
                ReceivedAt = "2026-07-03 09:33:23",
                QuoteTime = null
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 3, 9, 40, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_UsesOtcActualFundCodeNotStrategyEtfQuote()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00"),
            Quote("017091", "OTC", "2026-07-01 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Null(dailyPnl);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_IncludesOtcReplayStateWhenAggregateReplayMissing()
    {
        var replayPositions = new[]
        {
            ReplayPosition("159941", "159941", "\u573a\u5185ETF", -213.80)
        };
        var otcPositions = new[]
        {
            OtcPosition("159941", "017091", 269.12)
        };
        var quotes = new[]
        {
            Quote("159941", "ETF", "2026-07-02 15:00:00"),
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            replayPositions,
            otcPositions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(55.32, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_DoesNotDoubleCountOtcReplayState()
    {
        var replayPositions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var otcPositions = new[]
        {
            OtcPosition("159941", "017091", 269.12)
        };
        var quotes = new[]
        {
            Quote("017091", "OTC", "2026-07-02 20:00:00")
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            replayPositions,
            otcPositions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(269.12, dailyPnl!.Value, 2);
    }

    [Fact]
    public void CalculateNaturalDayValuationDailyPnl_MatchesSinaFundQuoteBySource()
    {
        var positions = new[]
        {
            ReplayPosition("159941", "017091", "\u573a\u5916\u66ff\u4ee3", 269.12)
        };
        var quotes = new[]
        {
            new MarketQuoteRecord
            {
                Symbol = "017091",
                MarketType = "FUND",
                Source = "SINA_FUND",
                ReceivedAt = "2026-07-02 20:00:00",
                QuoteTime = "2026-07-02"
            }
        };

        double? dailyPnl = EtfDecisionTableMetrics.CalculateNaturalDayValuationDailyPnl(
            positions,
            quotes,
            new DateTime(2026, 7, 2, 20, 1, 0));

        Assert.Equal(269.12, dailyPnl!.Value, 2);
    }

    private static PositionReplayStateRecord ReplayPosition(
        string strategyCode,
        string actualCode,
        string source,
        double? dailyPnl,
        double quantity = 100)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Source = source,
            Quantity = quantity,
            DailyPnl = dailyPnl
        };

    private static OtcPositionReplayStateRecord OtcPosition(string strategyCode, string actualCode, double dailyPnl)
        => new()
        {
            StrategyCode = strategyCode,
            ActualCode = actualCode,
            Quantity = 100,
            DailyPnl = dailyPnl
        };

    private static MarketQuoteRecord Quote(string symbol, string marketType, string receivedAt)
        => new()
        {
            Symbol = symbol,
            MarketType = marketType,
            ReceivedAt = receivedAt,
            QuoteTime = receivedAt
        };

    private static MarketQuoteRecord EtfQuote(
        string symbol,
        double price,
        double lastClose,
        string quoteTime,
        string receivedAt)
        => new()
        {
            Symbol = symbol,
            MarketType = "ETF",
            Source = "TENCENT_QT",
            Price = price,
            LastClose = lastClose,
            QuoteTime = quoteTime,
            ReceivedAt = receivedAt
        };
}
