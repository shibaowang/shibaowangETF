using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class AccountReplayService
{
    private const double CashEpsilon = 0.01;
    private const double QuantityEpsilon = 0.00000001;
    private const double CostEpsilon = 0.01;

    private static readonly HashSet<string> AllowedActions = new(StringComparer.Ordinal)
    {
        "CASH", "入金", "出金", "买入", "卖出", "分红", "送股", "拆分", "合并", "除权校准"
    };

    private static readonly HashSet<string> CorporateActions = new(StringComparer.Ordinal)
    {
        "送股", "拆分", "合并", "除权校准"
    };

    public AccountReplayResult Replay(
        IEnumerable<TradeLogRecord> tradeLogs,
        IEnumerable<MarketQuoteRecord> marketQuotes,
        DateTime? today = null)
    {
        var result = new AccountReplayResult();
        string calculatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        DateTime localToday = (today ?? DateTime.Now).Date;

        List<ReplayRow> rows = BuildRows(tradeLogs, result.Errors);
        result.Account.CalculatedAt = calculatedAt;

        if (result.Errors.Count > 0)
        {
            MarkFinancialError(result, calculatedAt);
            return result;
        }

        if (rows.Count == 0)
        {
            result.Account = new AccountReplayStateRecord
            {
                CalculatedAt = calculatedAt,
                ReplayStatus = "未回放",
                ReplayError = "暂无 TradeLog 记录。",
                CashBalance = 0,
                Principal = 0,
                TotalPositionCost = 0,
                KnownMarketValue = 0,
                TotalAssets = 0,
                TotalRealizedPnl = 0,
                TotalUnrealizedPnl = 0,
                TotalPnl = 0,
                TotalReturnRate = 0,
                CashRatio = 0,
                PositionRatio = 0,
                BasePositionRatio = null,
                MarketValueComplete = true,
                LastTradeLogId = null
            };
            return result;
        }

        rows = rows
            .OrderBy(row => row.Time)
            .ThenBy(row => row.Record.Id)
            .ThenBy(row => row.RowIndex)
            .ToList();

        var positions = new Dictionary<string, PositionAccumulator>(StringComparer.OrdinalIgnoreCase);
        double cash = 0;
        double principal = 0;
        bool hasFunding = false;
        long lastTradeLogId = rows.Max(row => row.Record.Id);

        foreach (ReplayRow row in rows)
        {
            TradeLogRecord record = row.Record;
            string action = record.Action.Trim();

            if (action == "CASH")
            {
                cash = record.CashBalance;
                if (record.Principal > 0)
                {
                    principal = record.Principal;
                }

                continue;
            }

            if (action is "入金" or "出金")
            {
                double net = GetFundingNetImpact(row);
                cash += net;
                principal += net;
                hasFunding = true;
                AuditCashBalance(result, row, cash);
                continue;
            }

            if (CorporateActions.Contains(action) && Math.Abs(record.NetCashImpact) > CashEpsilon)
            {
                result.Errors.Add(FormatError(row, "净现金流", $"{action} 不应产生非零净现金流。"));
                continue;
            }

            PositionAccumulator position = GetPosition(positions, row);
            switch (action)
            {
                case "买入":
                    cash += Math.Abs(record.NetCashImpact) > CashEpsilon
                        ? record.NetCashImpact
                        : -(record.Amount + record.Fee);
                    position.Quantity += record.Quantity;
                    position.CostAmount += record.Amount;
                    if (row.Time.Date == localToday)
                    {
                        position.TodayBuyQuantity += record.Quantity;
                        position.TodayBuyAmount += record.Amount;
                    }
                    break;

                case "卖出":
                    cash += Math.Abs(record.NetCashImpact) > CashEpsilon
                        ? record.NetCashImpact
                        : record.Amount - record.Fee;
                    ApplySell(result, row, position);
                    break;

                case "分红":
                    cash += Math.Abs(record.NetCashImpact) > CashEpsilon
                        ? record.NetCashImpact
                        : record.Amount;
                    break;

                case "送股":
                case "拆分":
                    position.Quantity += record.Quantity;
                    break;

                case "合并":
                    ApplyMerge(result, row, position);
                    break;

                case "除权校准":
                    position.AdjFactor = Math.Abs(record.Quantity) > QuantityEpsilon ? record.Quantity : 1;
                    break;
            }

            AuditCashBalance(result, row, cash);
        }

        if (result.Errors.Count > 0)
        {
            MarkFinancialError(result, calculatedAt, cash, principal, lastTradeLogId);
            return result;
        }

        if (!hasFunding)
        {
            principal = rows
                .Where(row => row.Record.Principal > 0)
                .OrderBy(row => row.Time)
                .ThenBy(row => row.Record.Id)
                .Select(row => row.Record.Principal)
                .LastOrDefault();
        }

        BuildValuation(result, positions.Values, marketQuotes.ToList(), calculatedAt);

        double totalPositionCost = result.Positions.Sum(position => position.CostAmount);
        double knownMarketValue = result.Positions.Sum(position => position.MarketValue ?? 0);
        double totalRealizedPnl = result.Positions.Sum(position => position.RealizedPnl);
        double totalUnrealizedPnl = result.Positions.Sum(position => position.UnrealizedPnl ?? 0);
        double totalPnl = totalRealizedPnl + totalUnrealizedPnl;
        double totalAssets = cash + knownMarketValue;

        result.Account = new AccountReplayStateRecord
        {
            CalculatedAt = calculatedAt,
            ReplayStatus = result.Warnings.Count > 0 ? "估值不完整" : "正常",
            ReplayError = result.Warnings.Count > 0 ? string.Join("；", result.Warnings) : null,
            CashBalance = CleanMoney(cash),
            Principal = CleanMoney(principal),
            TotalPositionCost = CleanMoney(totalPositionCost),
            KnownMarketValue = CleanMoney(knownMarketValue),
            TotalAssets = CleanMoney(totalAssets),
            TotalRealizedPnl = CleanMoney(totalRealizedPnl),
            TotalUnrealizedPnl = result.Warnings.Count > 0 ? null : CleanMoney(totalUnrealizedPnl),
            TotalPnl = result.Warnings.Count > 0 ? null : CleanMoney(totalPnl),
            TotalReturnRate = result.Warnings.Count > 0 || totalPositionCost <= CostEpsilon ? null : totalPnl / totalPositionCost,
            CashRatio = totalAssets > CostEpsilon ? cash / totalAssets : null,
            PositionRatio = totalAssets > CostEpsilon ? knownMarketValue / totalAssets : null,
            BasePositionRatio = principal > CostEpsilon ? totalPositionCost / principal : null,
            MarketValueComplete = result.Warnings.Count == 0,
            LastTradeLogId = lastTradeLogId > 0 ? lastTradeLogId : null
        };

        return result;
    }

    private static List<ReplayRow> BuildRows(IEnumerable<TradeLogRecord> tradeLogs, List<string> errors)
    {
        var rows = new List<ReplayRow>();
        int rowIndex = 0;

        foreach (TradeLogRecord record in tradeLogs)
        {
            rowIndex++;
            if (IsBlankRow(record))
            {
                continue;
            }

            ValidateRecord(record, rowIndex, errors);
            if (DateTime.TryParse(record.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime time)
                || DateTime.TryParse(record.Time, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out time))
            {
                rows.Add(new ReplayRow(record, rowIndex, time));
            }
        }

        return rows;
    }

    private static void ValidateRecord(TradeLogRecord record, int rowIndex, List<string> errors)
    {
        var row = new ReplayRow(record, rowIndex, DateTime.MinValue);
        if (string.IsNullOrWhiteSpace(record.Time))
        {
            errors.Add(FormatError(row, "时间", "不能为空。"));
        }
        else if (!DateTime.TryParse(record.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _)
                 && !DateTime.TryParse(record.Time, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out _))
        {
            errors.Add(FormatError(row, "时间", "必须是可解析时间。"));
        }

        if (string.IsNullOrWhiteSpace(record.StrategyCode))
        {
            errors.Add(FormatError(row, "策略代码", "不能为空。"));
        }

        if (string.IsNullOrWhiteSpace(record.Action))
        {
            errors.Add(FormatError(row, "动作", "不能为空。"));
        }
        else if (!AllowedActions.Contains(record.Action.Trim()))
        {
            errors.Add(FormatError(row, "动作", "不在允许范围内。"));
        }

        CheckNumber(row, "价格", record.Price, errors);
        CheckNumber(row, "数量", record.Quantity, errors);
        CheckNumber(row, "金额", record.Amount, errors);
        CheckNumber(row, "手续费", record.Fee, errors);
        CheckNumber(row, "净现金流", record.NetCashImpact, errors);
        CheckNumber(row, "本金", record.Principal, errors);
        CheckNumber(row, "现金余额", record.CashBalance, errors);
        CheckNumber(row, "总资产", record.TotalAssets, errors);

        if (record.Fee < 0)
        {
            errors.Add(FormatError(row, "手续费", "不能小于 0。"));
        }

        if (record.Amount < 0)
        {
            errors.Add(FormatError(row, "金额", "不能小于 0。"));
        }

        if (record.Quantity < 0)
        {
            errors.Add(FormatError(row, "数量", "不能小于 0。"));
        }

        string action = record.Action?.Trim() ?? string.Empty;
        if (action is "CASH" or "入金" or "出金"
            && !string.Equals(record.StrategyCode?.Trim(), "CASH", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(FormatError(row, "策略代码", $"{action} 记录必须填写 CASH。"));
        }

        if (action is "入金" or "出金")
        {
            if (Math.Abs(record.Amount) <= CashEpsilon && Math.Abs(record.NetCashImpact) <= CashEpsilon)
            {
                errors.Add(FormatError(row, "金额", $"{action} 记录必须填写金额或净现金流。"));
            }

            try
            {
                TradeLogPreCheckService.GetFundingNetImpact(
                    action,
                    record.Amount,
                    record.Fee,
                    Math.Abs(record.NetCashImpact) > CashEpsilon ? record.NetCashImpact : null);
            }
            catch (ArgumentException ex)
            {
                errors.Add(FormatError(row, "净现金流", ex.Message));
            }
        }

        if (action is "买入" or "卖出")
        {
            if (record.Quantity <= QuantityEpsilon)
            {
                errors.Add(FormatError(row, "数量", $"{action} 数量必须大于 0。"));
            }

            if (record.Amount <= CashEpsilon)
            {
                errors.Add(FormatError(row, "金额", $"{action} 金额必须大于 0。"));
            }
        }
    }

    private static void CheckNumber(ReplayRow row, string field, double value, List<string> errors)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            errors.Add(FormatError(row, field, "必须是有效数字。"));
        }
    }

    private static double GetFundingNetImpact(ReplayRow row)
        => TradeLogPreCheckService.GetFundingNetImpact(
            row.Record.Action.Trim(),
            row.Record.Amount,
            row.Record.Fee,
            Math.Abs(row.Record.NetCashImpact) > CashEpsilon ? row.Record.NetCashImpact : null);

    private static PositionAccumulator GetPosition(Dictionary<string, PositionAccumulator> positions, ReplayRow row)
    {
        string source = string.Equals(row.Record.Source, "场外替代", StringComparison.Ordinal)
            ? "场外替代"
            : "场内ETF";
        string strategyCode = row.Record.StrategyCode.Trim();
        string actualCode = string.IsNullOrWhiteSpace(row.Record.ActualCode)
            ? strategyCode
            : row.Record.ActualCode.Trim();
        string key = strategyCode + "|" + actualCode + "|" + source;

        if (!positions.TryGetValue(key, out PositionAccumulator? position))
        {
            position = new PositionAccumulator
            {
                StrategyCode = strategyCode,
                ActualCode = actualCode,
                Source = source
            };
            positions[key] = position;
        }

        return position;
    }

    private static void ApplySell(AccountReplayResult result, ReplayRow row, PositionAccumulator position)
    {
        if (row.Record.Quantity > position.Quantity + QuantityEpsilon)
        {
            result.Errors.Add(FormatError(row, "数量", $"卖出数量 {row.Record.Quantity:0.####} 超过当前持仓 {position.Quantity:0.####}。"));
            return;
        }

        double averageCost = position.Quantity > QuantityEpsilon ? position.CostAmount / position.Quantity : 0;
        double costDeduction = averageCost * row.Record.Quantity;
        position.Quantity -= row.Record.Quantity;
        position.CostAmount -= costDeduction;
        position.RealizedPnl += row.Record.Amount - row.Record.Fee - costDeduction;
        CleanPosition(position);
    }

    private static void ApplyMerge(AccountReplayResult result, ReplayRow row, PositionAccumulator position)
    {
        if (row.Record.Quantity > position.Quantity + QuantityEpsilon)
        {
            result.Errors.Add(FormatError(row, "数量", $"合并数量 {row.Record.Quantity:0.####} 超过当前持仓 {position.Quantity:0.####}。"));
            return;
        }

        position.Quantity -= row.Record.Quantity;
        CleanPosition(position);
    }

    private static void AuditCashBalance(AccountReplayResult result, ReplayRow row, double cash)
    {
        if (Math.Abs(row.Record.CashBalance) <= CashEpsilon)
        {
            return;
        }

        double diff = Math.Abs(cash - row.Record.CashBalance);
        if (diff > CashEpsilon)
        {
            result.Errors.Add(FormatError(row, "现金余额", $"回放现金 {cash:0.00} 与 TradeLog 现金余额 {row.Record.CashBalance:0.00} 不一致，偏差 {diff:0.00}。"));
        }
    }

    private static void BuildValuation(
        AccountReplayResult result,
        IEnumerable<PositionAccumulator> positions,
        IReadOnlyList<MarketQuoteRecord> quotes,
        string calculatedAt)
    {
        foreach (PositionAccumulator position in positions
                     .Where(position => position.Quantity > QuantityEpsilon
                                        || position.CostAmount > CostEpsilon
                                        || Math.Abs(position.RealizedPnl) > CostEpsilon)
                     .OrderBy(position => position.StrategyCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(position => position.ActualCode, StringComparer.OrdinalIgnoreCase))
        {
            MarketQuoteRecord? quote = position.Source == "场外替代"
                ? FindQuote(quotes, position.ActualCode, "OTC")
                : FindQuote(quotes, position.ActualCode, "ETF")
                  ?? FindQuote(quotes, position.StrategyCode, "ETF");

            double averageCost = position.Quantity > QuantityEpsilon ? position.CostAmount / position.Quantity : 0;
            double? marketPrice = quote?.Price;
            double? marketValue = marketPrice.HasValue ? position.Quantity * marketPrice.Value : null;
            double? dailyPnl = marketPrice.HasValue && quote?.LastClose is double lastClose
                ? (marketPrice.Value - lastClose) * position.Quantity
                : null;
            double? unrealized = marketValue.HasValue ? marketValue.Value - position.CostAmount : null;
            double? totalPnl = unrealized.HasValue ? unrealized.Value + position.RealizedPnl : null;
            double? returnRate = unrealized.HasValue && position.CostAmount > CostEpsilon
                ? unrealized.Value / position.CostAmount
                : null;
            string quoteStatus = marketPrice.HasValue ? "OK" : "未连接";

            var replayPosition = new PositionReplayStateRecord
            {
                CalculatedAt = calculatedAt,
                StrategyCode = position.StrategyCode,
                ActualCode = position.ActualCode,
                Source = position.Source,
                Quantity = CleanQuantity(position.Quantity),
                CostAmount = CleanMoney(position.CostAmount),
                AverageCost = CleanMoney(averageCost),
                AdjFactor = position.AdjFactor,
                TodayBuyQuantity = CleanQuantity(position.TodayBuyQuantity),
                TodayBuyAmount = CleanMoney(position.TodayBuyAmount),
                MarketPrice = marketPrice,
                MarketValue = marketValue,
                DailyPnl = dailyPnl,
                RealizedPnl = CleanMoney(position.RealizedPnl),
                UnrealizedPnl = unrealized,
                TotalPnl = totalPnl,
                ReturnRate = returnRate,
                QuoteStatus = quoteStatus
            };

            result.Positions.Add(replayPosition);
            if (position.Source == "场外替代")
            {
                result.OtcPositions.Add(new OtcPositionReplayStateRecord
                {
                    CalculatedAt = calculatedAt,
                    StrategyCode = position.StrategyCode,
                    ActualCode = position.ActualCode,
                    Quantity = replayPosition.Quantity,
                    CostAmount = replayPosition.CostAmount,
                    AverageCost = replayPosition.AverageCost,
                    Nav = replayPosition.MarketPrice,
                    MarketValue = replayPosition.MarketValue,
                    DailyPnl = replayPosition.DailyPnl,
                    UnrealizedPnl = replayPosition.UnrealizedPnl,
                    ReturnRate = replayPosition.ReturnRate,
                    QuoteStatus = replayPosition.QuoteStatus
                });
            }

            if (!marketPrice.HasValue)
            {
                result.Warnings.Add($"{position.StrategyCode}/{position.ActualCode} 缺少真实行情，估值不完整。");
            }
        }
    }

    private static MarketQuoteRecord? FindQuote(IReadOnlyList<MarketQuoteRecord> quotes, string code, string marketType)
    {
        string digits = DigitsOnly(code);
        return quotes
            .Where(quote => string.Equals(quote.MarketType, marketType, StringComparison.OrdinalIgnoreCase))
            .Where(quote =>
                CodeEquals(quote.Symbol, code, digits)
                || CodeEquals(quote.RawCode, code, digits))
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

    private static void MarkFinancialError(
        AccountReplayResult result,
        string calculatedAt,
        double? cash = null,
        double? principal = null,
        long? lastTradeLogId = null)
    {
        result.Account = new AccountReplayStateRecord
        {
            CalculatedAt = calculatedAt,
            ReplayStatus = "财务异常",
            ReplayError = string.Join("；", result.Errors),
            CashBalance = cash,
            Principal = principal,
            MarketValueComplete = false,
            LastTradeLogId = lastTradeLogId
        };
    }

    private static bool IsBlankRow(TradeLogRecord record)
        => string.IsNullOrWhiteSpace(record.Time)
           && string.IsNullOrWhiteSpace(record.StrategyCode)
           && string.IsNullOrWhiteSpace(record.ActualCode)
           && string.IsNullOrWhiteSpace(record.Action)
           && string.IsNullOrWhiteSpace(record.Tier)
           && string.IsNullOrWhiteSpace(record.Source)
           && string.IsNullOrWhiteSpace(record.Memo)
           && record.Price == 0
           && record.Quantity == 0
           && record.Amount == 0
           && record.Fee == 0
           && record.NetCashImpact == 0
           && record.Principal == 0
           && record.CashBalance == 0
           && record.TotalAssets == 0;

    private static void CleanPosition(PositionAccumulator position)
    {
        if (Math.Abs(position.Quantity) <= QuantityEpsilon)
        {
            position.Quantity = 0;
        }

        if (Math.Abs(position.CostAmount) <= CostEpsilon)
        {
            position.CostAmount = 0;
        }
    }

    private static double CleanQuantity(double value)
        => Math.Abs(value) <= QuantityEpsilon ? 0 : value;

    private static double CleanMoney(double value)
        => Math.Abs(value) <= CostEpsilon ? 0 : value;

    private static string FormatError(ReplayRow row, string field, string reason)
        => $"第 {row.RowIndex} 行(ID {row.Record.Id}) 字段[{field}]：{reason}";

    private sealed record ReplayRow(TradeLogRecord Record, int RowIndex, DateTime Time);

    private sealed class PositionAccumulator
    {
        public string StrategyCode { get; init; } = string.Empty;
        public string ActualCode { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public double Quantity { get; set; }
        public double CostAmount { get; set; }
        public double AdjFactor { get; set; } = 1;
        public double TodayBuyQuantity { get; set; }
        public double TodayBuyAmount { get; set; }
        public double RealizedPnl { get; set; }
    }
}
