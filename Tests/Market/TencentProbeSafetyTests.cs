namespace CrossETF.Terminal.UiShell.Reference.Tests.Market;

public sealed class TencentProbeSafetyTests
{
    [Fact]
    public void TencentProbe_DefaultPathIsDryRun()
    {
        string script = File.ReadAllText(FindWorkspaceFile("Tools", "TencentProbe", "ProbeTencentEtfChart.ps1"));

        Assert.Contains("if (-not $Live)", script, StringComparison.Ordinal);
        Assert.Contains("dry_run = $true", script, StringComparison.Ordinal);
        Assert.Contains("live_required = $true", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TencentProbe_LiveBatchRequiresExplicitFlags()
    {
        string script = File.ReadAllText(FindWorkspaceFile("Tools", "TencentProbe", "ProbeTencentEtfChart.ps1"));

        Assert.Contains("-not $AllowBatch", script, StringComparison.Ordinal);
        Assert.Contains("Add -AllowBatch -Yes", script, StringComparison.Ordinal);
        Assert.Contains("Read-Host", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TencentProbe_LiveRequestsUseNoProxyAndDelay()
    {
        string script = File.ReadAllText(FindWorkspaceFile("Tools", "TencentProbe", "ProbeTencentEtfChart.ps1"));

        Assert.Contains("$handler.UseProxy = $false", script, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds $DelaySeconds", script, StringComparison.Ordinal);
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Workspace file not found: " + Path.Combine(parts));
    }
}
