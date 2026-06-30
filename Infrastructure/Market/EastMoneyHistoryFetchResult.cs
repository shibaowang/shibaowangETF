namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public sealed class EastMoneyHistoryFetchResult
{
    public string SecId { get; init; } = string.Empty;
    public int Fqt { get; init; }
    public int Klt { get; init; }
    public string Url { get; init; } = string.Empty;
    public string RawPayload { get; init; } = string.Empty;
    public double High { get; init; }
    public int PointCount { get; init; }
    public double? LatestDrawdown { get; init; }
}
