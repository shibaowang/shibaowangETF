using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class TradeLogLedgerNormalizer
{
    private const double CashEpsilon = 0.01;
    private const double QuantityEpsilon = 0.00000001;

    public static void AutoCalculateTradeAmounts(IEnumerable<TradeLogRecord> records)
    {
        foreach (TradeLogRecord record in records)
        {
            AutoCalculateTradeAmount(record);
        }
    }

    public static bool TryCalculateTradeAmount(string? action, string? priceText, string? quantityText, out double? amount, out string? error)
    {
        amount = null;
        error = null;
        string normalizedAction = action?.Trim() ?? string.Empty;
        if (normalizedAction is not ("买入" or "卖出"))
        {
            return true;
        }

        if (IsIntermediateNumber(priceText) || IsIntermediateNumber(quantityText))
        {
            return true;
        }

        if (!TryParseEditorNumber(priceText, out double price, out error)
            || !TryParseEditorNumber(quantityText, out double quantity, out error))
        {
            return false;
        }

        if (price > 0 && quantity > 0)
        {
            amount = Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero);
        }

        return true;
    }

    public static void AutoCalculateTradeAmount(TradeLogRecord record)
    {
        string action = record.Action?.Trim() ?? string.Empty;
        if (action is not ("买入" or "卖出"))
        {
            return;
        }

        if (record.Price > 0 && record.Quantity > 0)
        {
            record.Amount = Math.Round(record.Price * record.Quantity, 2, MidpointRounding.AwayFromZero);
        }
    }

    public static void NormalizeLedgerFieldsBeforeSave(
        IEnumerable<TradeLogRecord> records,
        IEnumerable<MarketQuoteRecord>? marketQuotes = null)
    {
        var rows = records
            .Where(record => !IsUntouched(record))
            .Select((record, index) => new LedgerRow(record, index + 1, ParseTime(record.Time, index + 1)))
            .OrderBy(row => row.Time)
            .ThenBy(row => row.Record.Id)
            .ThenBy(row => row.RowIndex)
            .ToList();
        var positions = new Dictionary<string, PositionState>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<MarketQuoteRecord> quotes = marketQuotes is null
            ? Array.Empty<MarketQuoteRecord>()
            : marketQuotes.ToList();
        double cash = 0;
        double principal = 0;

        foreach (LedgerRow row in rows)
        {
            TradeLogRecord record = row.Record;
            string action = RequireAction(record, row.RowIndex);
            ValidateCommonNumbers(record, row.RowIndex);
            AutoCalculateTradeAmount(record);

            switch (action)
            {
                case "CASH":
                    cash = Math.Abs(record.CashBalance) > CashEpsilon
                        ? record.CashBalance
                        : record.Amount > 0 ? record.Amount : cash;
                    if (record.Principal > 0)
                    {
                        principal = record.Principal;
                    }
                    record.NetCashImpact = 0;
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "入金":
                    RequireCashStrategy(record, row.RowIndex, action);
                    record.NetCashImpact = record.Amount - record.Fee;
                    if (record.NetCashImpact < -CashEpsilon)
                    {
                        throw new InvalidOperationException($"第 {row.RowIndex} 行入金净现金流不能为负。");
                    }
                    cash += record.NetCashImpact;
                    principal += record.NetCashImpact;
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "出金":
                    RequireCashStrategy(record, row.RowIndex, action);
                    record.NetCashImpact = -(record.Amount + record.Fee);
                    if (record.NetCashImpact > CashEpsilon)
                    {
                        throw new InvalidOperationException($"第 {row.RowIndex} 行出金净现金流不能为正。");
                    }
                    cash += record.NetCashImpact;
                    principal += record.NetCashImpact;
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "买入":
                    RequirePositiveQuantity(record, row.RowIndex, action);
                    RequirePositiveAmount(record, row.RowIndex, action);
                    record.NetCashImpact = -(record.Amount + record.Fee);
                    cash += record.NetCashImpact;
                    ApplyPositionBuy(positions, record);
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "卖出":
                    RequirePositiveQuantity(record, row.RowIndex, action);
                    RequirePositiveAmount(record, row.RowIndex, action);
                    record.NetCashImpact = record.Amount - record.Fee;
                    cash += record.NetCashImpact;
                    ApplyPositionSellForKnownValuation(positions, record);
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "分红":
                    record.NetCashImpact = record.Amount;
                    cash += record.NetCashImpact;
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "送股":
                case "拆分":
                    record.NetCashImpact = 0;
                    ApplyPositionQuantityIncrease(positions, record);
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "合并":
                    record.NetCashImpact = 0;
                    ApplyPositionQuantityDecreaseForKnownValuation(positions, record);
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                case "除权校准":
                    record.NetCashImpact = 0;
                    record.CashBalance = cash;
                    record.Principal = principal;
                    break;

                default:
                    throw new InvalidOperationException($"第 {row.RowIndex} 行动作不支持：{action}。");
            }

            record.TotalAssets = CalculateTotalAssets(cash, positions.Values, quotes);
        }
    }

    public static bool TryNormalizeLedgerFieldsBeforeSave(
        IEnumerable<TradeLogRecord> records,
        IEnumerable<MarketQuoteRecord>? marketQuotes,
        out string? error)
    {
        try
        {
            NormalizeLedgerFieldsBeforeSave(records, marketQuotes);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ValidateCommonNumbers(TradeLogRecord record, int rowIndex)
    {
        CheckFinite(record.Price, "价格", rowIndex);
        CheckFinite(record.Quantity, "数量", rowIndex);
        CheckFinite(record.Amount, "金额", rowIndex);
        CheckFinite(record.Fee, "手续费", rowIndex);

        if (record.Amount < 0)
        {
            throw new InvalidOperationException($"第 {rowIndex} 行金额不能小于 0。");
        }

        if (record.Fee < 0)
        {
            throw new InvalidOperationException($"第 {rowIndex} 行手续费不能小于 0。");
        }

        if (record.Quantity < 0)
        {
            throw new InvalidOperationException($"第 {rowIndex} 行数量不能小于 0。");
        }
    }

    private static void CheckFinite(double value, string fieldName, int rowIndex)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException($"第 {rowIndex} 行{fieldName}必须是有效数字。");
        }
    }

    private static string RequireAction(TradeLogRecord record, int rowIndex)
    {
        if (string.IsNullOrWhiteSpace(record.Action))
        {
            throw new InvalidOperationException($"第 {rowIndex} 行动作不能为空。");
        }

        return record.Action.Trim();
    }

    private static void RequireCashStrategy(TradeLogRecord record, int rowIndex, string action)
    {
        if (!string.Equals(record.StrategyCode?.Trim(), "CASH", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"第 {rowIndex} 行{action}记录的策略代码必须为 CASH。");
        }
    }

    private static void RequirePositiveQuantity(TradeLogRecord record, int rowIndex, string action)
    {
        if (record.Quantity <= QuantityEpsilon)
        {
            throw new InvalidOperationException($"第 {rowIndex} 行{action}数量必须大于 0。");
        }
    }

    private static void RequirePositiveAmount(TradeLogRecord record, int rowIndex, string action)
    {
        if (record.Amount <= CashEpsilon)
        {
            throw new InvalidOperationException($"第 {rowIndex} 行{action}金额必须大于 0。");
        }
    }

    private static DateTime ParseTime(string? time, int rowIndex)
    {
        if (string.IsNullOrWhiteSpace(time))
        {
            throw new InvalidOperationException($"第 {rowIndex} 行时间不能为空。");
        }

        if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime invariantTime)
            || DateTime.TryParse(time, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out invariantTime))
        {
            return invariantTime;
        }

        throw new InvalidOperationException($"第 {rowIndex} 行时间必须是可解析时间。");
    }

    private static void ApplyPositionBuy(Dictionary<string, PositionState> positions, TradeLogRecord record)
    {
        PositionState position = GetPosition(positions, record);
        position.Quantity += record.Quantity;
        position.CostAmount += record.Amount;
    }

    private static void ApplyPositionSellForKnownValuation(Dictionary<string, PositionState> positions, TradeLogRecord record)
    {
        PositionState position = GetPosition(positions, record);
        if (position.Quantity <= QuantityEpsilon)
        {
            return;
        }

        double sellQuantity = Math.Min(record.Quantity, position.Quantity);
        double averageCost = position.CostAmount / position.Quantity;
        position.Quantity -= sellQuantity;
        position.CostAmount -= averageCost * sellQuantity;
        CleanPosition(position);
    }

    private static void ApplyPositionQuantityIncrease(Dictionary<string, PositionState> positions, TradeLogRecord record)
    {
        if (record.Quantity <= 0)
        {
            return;
        }

        PositionState position = GetPosition(positions, record);
        position.Quantity += record.Quantity;
    }

    private static void ApplyPositionQuantityDecreaseForKnownValuation(Dictionary<string, PositionState> positions, TradeLogRecord record)
    {
        PositionState position = GetPosition(positions, record);
        position.Quantity -= Math.Min(record.Quantity, Math.Max(0, position.Quantity));
        CleanPosition(position);
    }

    private static PositionState GetPosition(Dictionary<string, PositionState> positions, TradeLogRecord record)
    {
        string source = string.Equals(record.Source, "场外替代", StringComparison.Ordinal) ? "场外替代" : "场内ETF";
        string strategyCode = record.StrategyCode.Trim();
        string actualCode = string.IsNullOrWhiteSpace(record.ActualCode) ? strategyCode : record.ActualCode.Trim();
        string key = strategyCode + "|" + actualCode + "|" + source;
        if (!positions.TryGetValue(key, out PositionState? position))
        {
            position = new PositionState(strategyCode, actualCode, source);
            positions[key] = position;
        }

        return position;
    }

    private static double CalculateTotalAssets(double cash, IEnumerable<PositionState> positions, IReadOnlyList<MarketQuoteRecord> quotes)
    {
        double knownMarketValue = 0;
        foreach (PositionState position in positions)
        {
            MarketQuoteRecord? quote = position.Source == "场外替代"
                ? FindQuote(quotes, position.ActualCode, "OTC")
                : FindQuote(quotes, position.ActualCode, "ETF") ?? FindQuote(quotes, position.StrategyCode, "ETF");
            if (quote?.Price is double price)
            {
                knownMarketValue += position.Quantity * price;
            }
        }

        return Math.Round(cash + knownMarketValue, 2, MidpointRounding.AwayFromZero);
    }

    private static MarketQuoteRecord? FindQuote(IReadOnlyList<MarketQuoteRecord> quotes, string code, string marketType)
    {
        string digits = DigitsOnly(code);
        return quotes
            .Where(quote => string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase))
            .Where(quote => CodeEquals(quote.Symbol, code, digits) || CodeEquals(quote.RawCode, code, digits))
            .OrderByDescending(quote => quote.ReceivedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool CodeEquals(string? candidate, string code, string digits)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string candidateDigits = DigitsOnly(candidate);
        return string.Equals(candidate, code, StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(digits)
                   && string.Equals(candidateDigits, digits, StringComparison.OrdinalIgnoreCase));
    }

    private static string DigitsOnly(string value)
        => new(value.Where(char.IsDigit).ToArray());

    private static bool TryParseEditorNumber(string? value, out double result, out string? error)
    {
        result = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result)
            || double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        error = "数值格式无效：" + value;
        return false;
    }

    private static bool IsIntermediateNumber(string? value)
    {
        string text = value?.Trim() ?? string.Empty;
        return text is "" or "." or "+" or "-" or "+." or "-."
               || text.EndsWith(".", StringComparison.Ordinal);
    }

    private static void CleanPosition(PositionState position)
    {
        if (Math.Abs(position.Quantity) <= QuantityEpsilon)
        {
            position.Quantity = 0;
        }

        if (Math.Abs(position.CostAmount) <= CashEpsilon)
        {
            position.CostAmount = 0;
        }
    }

    private static bool IsUntouched(TradeLogRecord record)
        => string.IsNullOrWhiteSpace(record.StrategyCode)
           && string.IsNullOrWhiteSpace(record.ActualCode)
           && string.IsNullOrWhiteSpace(record.Tier)
           && string.IsNullOrWhiteSpace(record.Source)
           && string.IsNullOrWhiteSpace(record.Memo)
           && string.IsNullOrWhiteSpace(record.Action)
           && string.IsNullOrWhiteSpace(record.Time)
           && record.Price == 0
           && record.Quantity == 0
           && record.Amount == 0
           && record.Fee == 0
           && record.NetCashImpact == 0
           && record.Principal == 0
           && record.CashBalance == 0
           && record.TotalAssets == 0;

    private sealed record LedgerRow(TradeLogRecord Record, int RowIndex, DateTime Time);

    private sealed class PositionState(string strategyCode, string actualCode, string source)
    {
        public string StrategyCode { get; } = strategyCode;
        public string ActualCode { get; } = actualCode;
        public string Source { get; } = source;
        public double Quantity { get; set; }
        public double CostAmount { get; set; }
    }
}
