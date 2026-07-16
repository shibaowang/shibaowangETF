namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

internal static class SourceTextTestHelper
{
    public static string NormalizeLineEndings(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string Slice(string source, string startMarker, string endMarker)
    {
        string normalizedSource = NormalizeLineEndings(source);
        string normalizedStart = NormalizeLineEndings(startMarker);
        string normalizedEnd = NormalizeLineEndings(endMarker);
        int start = normalizedSource.IndexOf(normalizedStart, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Start marker not found: " + normalizedStart);
        }

        int end = normalizedSource.IndexOf(
            normalizedEnd,
            start + normalizedStart.Length,
            StringComparison.Ordinal);
        if (end <= start)
        {
            throw new InvalidOperationException("End marker not found: " + normalizedEnd);
        }

        return normalizedSource[start..end];
    }

    public static void RequireContains(string source, params string[] requiredMarkers)
    {
        string normalizedSource = NormalizeLineEndings(source);
        foreach (string marker in requiredMarkers)
        {
            string normalizedMarker = NormalizeLineEndings(marker);
            if (normalizedSource.IndexOf(normalizedMarker, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Required marker not found: " + normalizedMarker);
            }
        }
    }

    public static void RequireMarkersInOrder(string source, params string[] orderedMarkers)
    {
        string normalizedSource = NormalizeLineEndings(source);
        int nextSearchIndex = 0;
        foreach (string marker in orderedMarkers)
        {
            string normalizedMarker = NormalizeLineEndings(marker);
            int markerIndex = normalizedSource.IndexOf(
                normalizedMarker,
                nextSearchIndex,
                StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Required marker is missing or out of order: {normalizedMarker}");
            }

            nextSearchIndex = markerIndex + normalizedMarker.Length;
        }
    }
}
