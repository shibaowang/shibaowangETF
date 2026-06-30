namespace CrossETF.Terminal.UiShell.Reference.Core.Models;

public sealed class AccountReplayResult
{
    public AccountReplayStateRecord Account { get; set; } = new();
    public List<PositionReplayStateRecord> Positions { get; } = new();
    public List<OtcPositionReplayStateRecord> OtcPositions { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool HasFinancialError => Errors.Count > 0;
}
