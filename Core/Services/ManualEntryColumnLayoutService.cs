namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class ManualEntryColumnLayoutService
{
    public const string SettingKeyPrefix = "manual_entry_columns:";

    public static string BuildSettingKey(string tabKey)
        => SettingKeyPrefix + NormalizeKey(tabKey);

    public static string SerializeOrder(IEnumerable<string> orderedKeys, IEnumerable<string> defaultKeys)
        => string.Join(",", ResolveOrder(defaultKeys, string.Join(",", orderedKeys ?? Array.Empty<string>())));

    public static IReadOnlyList<string> ResolveOrder(IEnumerable<string> defaultKeys, string? savedValue)
    {
        string[] defaults = NormalizeKeys(defaultKeys).ToArray();
        if (defaults.Length == 0)
        {
            return Array.Empty<string>();
        }

        HashSet<string> known = defaults.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(savedValue))
        {
            foreach (string key in NormalizeKeys(savedValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                if (known.Contains(key) && seen.Add(key))
                {
                    result.Add(key);
                }
            }
        }

        foreach (string key in defaults)
        {
            if (seen.Add(key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    private static IEnumerable<string> NormalizeKeys(IEnumerable<string>? keys)
    {
        if (keys is null)
        {
            yield break;
        }

        foreach (string key in keys)
        {
            string normalized = NormalizeKey(key);
            if (normalized.Length > 0)
            {
                yield return normalized;
            }
        }
    }

    private static string NormalizeKey(string? key)
        => string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
}
