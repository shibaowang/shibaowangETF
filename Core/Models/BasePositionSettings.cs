namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class BasePositionSettings
{
    public const string RatioMode = "ratio";
    public const string AmountMode = "amount";
    public const double DefaultRatio = 0.20;

    public string Mode { get; set; } = RatioMode;
    public double Ratio { get; set; } = DefaultRatio;
    public double FixedAmount { get; set; }

    public static BasePositionSettings Default()
        => new()
        {
            Mode = RatioMode,
            Ratio = DefaultRatio,
            FixedAmount = 0
        };
}

public sealed record BasePositionTargetResult(
    double TargetAmount,
    bool IsCappedToPrincipal,
    string Mode,
    double Ratio,
    double FixedAmount);
