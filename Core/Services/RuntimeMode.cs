namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public static class RuntimeMode
{
    public const string SmokeModeEnvironmentVariable = "CROSSETF_SMOKE_MODE";

    public static bool IsSmokeMode()
        => IsSmokeMode(Environment.GetEnvironmentVariable(SmokeModeEnvironmentVariable));

    public static bool IsSmokeMode(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
}
