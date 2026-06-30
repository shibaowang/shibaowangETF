using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public static class MarketSymbolNormalizer
{
    private static readonly HashSet<string> EastMoneyMarketPrefixes = new(StringComparer.Ordinal)
    {
        "0",
        "1",
        "100",
        "105",
        "106",
        "107",
        "116",
        "133",
        "142",
        "155",
        "156",
        "251"
    };

    public static MarketWatchItem NormalizeTencentEtf(string code, string? displayName = null)
    {
        string normalized = code.Trim().ToLowerInvariant();
        string digits = DigitsOnly(normalized);
        string rawCode = normalized.StartsWith("sh", StringComparison.Ordinal) || normalized.StartsWith("sz", StringComparison.Ordinal)
            ? normalized
            : (IsShanghaiEtf(digits) ? "sh" : "sz") + digits;
        return new MarketWatchItem
        {
            Symbol = digits,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? digits : displayName.Trim(),
            MarketType = "ETF",
            Source = MarketSources.Tencent,
            RawCode = rawCode
        };
    }

    public static string NormalizeEastMoneyEtfSecId(string code)
        => NormalizeEastMoneySecId(code, false);

    public static string NormalizeEastMoneySecId(string rawCode, bool preferIndex)
    {
        string value = CleanSymbol(rawCode);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (LooksLikeSecId(value))
        {
            string[] parts = value.Split('.', 2);
            string first = parts[0].Trim();
            string second = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            if (IsShanghaiPrefix(first))
            {
                return "1." + DigitsOnly(second);
            }

            if (IsShenzhenPrefix(first))
            {
                return "0." + DigitsOnly(second);
            }

            if (IsShanghaiPrefix(second))
            {
                return "1." + DigitsOnly(first);
            }

            if (IsShenzhenPrefix(second))
            {
                return "0." + DigitsOnly(first);
            }

            if (EastMoneyMarketPrefixes.Contains(first))
            {
                return value;
            }

            string marketPrefix = ExtractEastMoneyMarketPrefix(value);
            if (EastMoneyMarketPrefixes.Contains(marketPrefix))
            {
                return value;
            }

            return value;
        }

        if (value.StartsWith("sz", StringComparison.OrdinalIgnoreCase))
        {
            return "0." + DigitsOnly(value[2..]);
        }

        if (value.StartsWith("sh", StringComparison.OrdinalIgnoreCase))
        {
            return "1." + DigitsOnly(value[2..]);
        }

        if (value.StartsWith("bj", StringComparison.OrdinalIgnoreCase))
        {
            return "0." + DigitsOnly(value[2..]);
        }

        string digits = DigitsOnly(value);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return string.Empty;
        }

        if (preferIndex)
        {
            if (digits.StartsWith("399", StringComparison.Ordinal))
            {
                return "0." + digits;
            }

            if (digits.StartsWith("000", StringComparison.Ordinal)
                || digits.StartsWith("880", StringComparison.Ordinal)
                || IsShanghaiEtf(digits))
            {
                return "1." + digits;
            }

            return "0." + digits;
        }

        return (IsShanghaiEtf(digits) ? "1." : "0.") + digits;
    }

    public static MarketWatchItem NormalizeEastMoneySecId(string secId, string displayName, string marketType)
    {
        return new MarketWatchItem
        {
            Symbol = secId.Trim(),
            DisplayName = displayName.Trim(),
            MarketType = marketType,
            Source = MarketSources.EastMoney,
            RawCode = secId.Trim()
        };
    }

    public static string DigitsOnly(string value)
        => new(value.Where(char.IsDigit).ToArray());

    public static bool LooksLikeSecId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Contains('.', StringComparison.Ordinal);

    public static string ExtractEastMoneyTargetCode(string secId)
    {
        string value = secId.Trim();
        int dotIndex = value.IndexOf('.');
        return dotIndex >= 0 && dotIndex < value.Length - 1 ? value[(dotIndex + 1)..] : value;
    }

    private static string ExtractEastMoneyMarketPrefix(string secId)
    {
        int dotIndex = secId.IndexOf('.');
        return dotIndex > 0 ? secId[..dotIndex] : string.Empty;
    }

    private static string CleanSymbol(string value)
        => value.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("\"", string.Empty, StringComparison.Ordinal);

    private static bool IsShanghaiPrefix(string value)
        => string.Equals(value, "sh", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "ss", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "xshg", StringComparison.OrdinalIgnoreCase);

    private static bool IsShenzhenPrefix(string value)
        => string.Equals(value, "sz", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "xshe", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "bj", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "xsec", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<MarketWatchItem> DefaultTopBarItems()
        => new[]
        {
            NormalizeEastMoneySecId("1.000300", "沪深300", "INDEX"),
            NormalizeEastMoneySecId("1.000905", "中证500", "INDEX"),
            NormalizeEastMoneySecId("100.HSI", "恒生指数", "INDEX"),
            NormalizeEastMoneySecId("100.NDX100", "纳斯达克100", "INDEX"),
            NormalizeEastMoneySecId("251.NDXTMC", "纳斯达克科技指数", "INDEX"),
            NormalizeEastMoneySecId("133.USDCNH", "美元 / 人民币", "FX")
        };

    private static bool IsShanghaiEtf(string digits)
        => digits.StartsWith("5", StringComparison.Ordinal)
           || digits.StartsWith("6", StringComparison.Ordinal)
           || digits.StartsWith("9", StringComparison.Ordinal);
}
