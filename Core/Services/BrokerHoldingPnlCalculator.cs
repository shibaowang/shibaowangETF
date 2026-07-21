using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class BrokerHoldingPnlCalculator
{
    private const double QuantityEpsilon = 0.00000001;
    private const double MoneyEpsilon = 0.00000001;

    public static BrokerHoldingPnlMetrics? Calculate(
        string strategyCode,
        IEnumerable<TradeLogRecord> tradeLogs,
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions,
        IEnumerable<MarketQuoteRecord> quotes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyCode);
        ArgumentNullException.ThrowIfNull(tradeLogs);
        ArgumentNullException.ThrowIfNull(replayPositions);
        ArgumentNullException.ThrowIfNull(otcPositions);
        ArgumentNullException.ThrowIfNull(quotes);

        Dictionary<string, OpenCycleAccumulator> cycles = BuildOpenCycles(strategyCode, tradeLogs);
        OpenCycleAccumulator[] activeCycles = cycles.Values
            .Where(cycle => cycle.Quantity > QuantityEpsilon)
            .ToArray();
        if (activeCycles.Length == 0)
        {
            return null;
        }

        Dictionary<string, ValuationState> valuations = BuildValuations(
            strategyCode,
            replayPositions,
            otcPositions);
        IReadOnlyList<MarketQuoteRecord> quoteList = quotes as IReadOnlyList<MarketQuoteRecord> ?? quotes.ToArray();

        double totalQuantity = activeCycles.Sum(cycle => cycle.Quantity);
        double openCycleNetInvestment = activeCycles.Sum(cycle => cycle.NetInvestment);
        bool marketValueComplete = true;
        double marketValue = 0;

        foreach (OpenCycleAccumulator cycle in activeCycles)
        {
            if (valuations.TryGetValue(cycle.ActualCode, out ValuationState? valuation)
                && valuation.MarketValue.HasValue)
            {
                marketValue += valuation.MarketValue.Value;
                continue;
            }

            string marketType = cycle.IsOtc ? "OTC" : "ETF";
            MarketQuoteRecord? quote = MarketQuoteFreshnessSelector.SelectBest(quoteList, cycle.ActualCode, marketType);
            if (quote?.Price is double price && IsFinitePositive(price))
            {
                marketValue += cycle.Quantity * price;
                continue;
            }

            marketValueComplete = false;
        }

        double? knownMarketValue = marketValueComplete ? marketValue : null;
        double? holdingPnl = knownMarketValue.HasValue
            ? knownMarketValue.Value - openCycleNetInvestment
            : null;
        double? holdingReturnRate = holdingPnl.HasValue && openCycleNetInvestment > MoneyEpsilon
            ? holdingPnl.Value / openCycleNetInvestment
            : null;
        double? dilutedAverageCost = totalQuantity > QuantityEpsilon
            ? openCycleNetInvestment / totalQuantity
            : null;

        return new BrokerHoldingPnlMetrics(
            strategyCode,
            openCycleNetInvestment,
            openCycleNetInvestment,
            dilutedAverageCost,
            holdingPnl,
            holdingReturnRate,
            totalQuantity,
            knownMarketValue,
            activeCycles.Length);
    }

    private static Dictionary<string, OpenCycleAccumulator> BuildOpenCycles(
        string strategyCode,
        IEnumerable<TradeLogRecord> tradeLogs)
    {
        var cycles = new Dictionary<string, OpenCycleAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (TradeLogRecord record in tradeLogs
                     .Where(record => SameCode(record.StrategyCode, strategyCode))
                     .Where(record => !string.IsNullOrWhiteSpace(record.ActualCode))
                     .OrderBy(record => ParseTime(record.Time))
                     .ThenBy(record => record.Id))
        {
            string actualCode = NormalizeCode(record.ActualCode);
            if (!cycles.TryGetValue(actualCode, out OpenCycleAccumulator? cycle))
            {
                cycle = new OpenCycleAccumulator(actualCode);
                cycles.Add(actualCode, cycle);
            }

            cycle.IsOtc |= IsOtc(record);
            switch (record.Action.Trim())
            {
                case "买入":
                    if (cycle.Quantity <= QuantityEpsilon)
                    {
                        cycle.Reset();
                    }

                    cycle.Quantity += record.Quantity;
                    cycle.NetInvestment += ResolveBuyOutflow(record);
                    break;

                case "卖出":
                    cycle.Quantity -= record.Quantity;
                    cycle.NetInvestment -= ResolveCashInflow(record, record.Amount - record.Fee);
                    if (cycle.Quantity <= QuantityEpsilon)
                    {
                        cycle.Reset();
                    }
                    break;

                case "分红":
                    if (cycle.Quantity > QuantityEpsilon)
                    {
                        cycle.NetInvestment -= ResolveCashInflow(record, record.Amount);
                    }
                    break;

                case "送股":
                case "拆分":
                    cycle.Quantity += record.Quantity;
                    break;

                case "合并":
                    cycle.Quantity -= record.Quantity;
                    if (cycle.Quantity <= QuantityEpsilon)
                    {
                        cycle.Reset();
                    }
                    break;

                case "除权校准":
                    break;
            }
        }

        return cycles;
    }

    private static Dictionary<string, ValuationState> BuildValuations(
        string strategyCode,
        IEnumerable<PositionReplayStateRecord> replayPositions,
        IEnumerable<OtcPositionReplayStateRecord> otcPositions)
    {
        var valuations = new Dictionary<string, ValuationState>(StringComparer.OrdinalIgnoreCase);
        foreach (PositionReplayStateRecord position in replayPositions
                     .Where(position => SameCode(position.StrategyCode, strategyCode)))
        {
            valuations[NormalizeCode(position.ActualCode)] = new ValuationState(position.MarketValue);
        }

        foreach (OtcPositionReplayStateRecord position in otcPositions
                     .Where(position => SameCode(position.StrategyCode, strategyCode)))
        {
            string actualCode = NormalizeCode(position.ActualCode);
            valuations.TryAdd(actualCode, new ValuationState(position.MarketValue));
        }

        return valuations;
    }

    private static double ResolveBuyOutflow(TradeLogRecord record)
        => Math.Abs(record.NetCashImpact) > MoneyEpsilon
            ? -record.NetCashImpact
            : record.Amount + record.Fee;

    private static double ResolveCashInflow(TradeLogRecord record, double fallback)
        => Math.Abs(record.NetCashImpact) > MoneyEpsilon
            ? record.NetCashImpact
            : fallback;

    private static bool IsOtc(TradeLogRecord record)
        => record.Source?.Contains("场外", StringComparison.OrdinalIgnoreCase) == true;

    private static bool SameCode(string? left, string? right)
        => string.Equals(NormalizeCode(left), NormalizeCode(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string digits = new(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? value.Trim() : digits;
    }

    private static DateTime ParseTime(string? value)
        => DateTime.TryParse(
               value,
               CultureInfo.InvariantCulture,
               DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
               out DateTime parsed)
            ? parsed
            : DateTime.MinValue;

    private static bool IsFinitePositive(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

    private sealed class OpenCycleAccumulator
    {
        public OpenCycleAccumulator(string actualCode)
        {
            ActualCode = actualCode;
        }

        public string ActualCode { get; }
        public bool IsOtc { get; set; }
        public double Quantity { get; set; }
        public double NetInvestment { get; set; }

        public void Reset()
        {
            Quantity = 0;
            NetInvestment = 0;
        }
    }

    private sealed record ValuationState(double? MarketValue);
}
