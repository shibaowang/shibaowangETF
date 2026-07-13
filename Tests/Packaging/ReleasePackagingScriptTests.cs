namespace CrossETF.Terminal.UiShell.Reference.Tests.Release;

public class ReleasePackagingScriptTests
{
    [Fact]
    public void Script_ExposesRequiredParametersAndValidatesCommitBeforeVersion()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        Assert.Contains("[string]$Version", script, StringComparison.Ordinal);
        Assert.Contains("[Alias(\"WorktreePath\")]", script, StringComparison.Ordinal);
        Assert.Contains("[string]$SourcePath", script, StringComparison.Ordinal);
        Assert.Contains("[string]$OutputRoot", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$CreateDesktopShortcut", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ExpectedCommit", script, StringComparison.Ordinal);
        Assert.Contains("git -C $sourceDirectory rev-parse HEAD", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("SourcePath commit 不匹配", StringComparison.Ordinal)
            < script.IndexOf("Version 格式无效", StringComparison.Ordinal));
    }

    [Fact]
    public void Script_RenamesOnlyAppHostAndValidatesPublishedVersion()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        Assert.Contains("CrossETF.Terminal.UiShell.Reference.exe", script, StringComparison.Ordinal);
        Assert.Contains("跨境ETF.exe", script, StringComparison.Ordinal);
        Assert.Contains("Rename-Item -LiteralPath $originalExePath -NewName $releaseExeName", script, StringComparison.Ordinal);
        Assert.Contains("正式目录仍保留旧英文名称 EXE", script, StringComparison.Ordinal);
        Assert.Contains("$versionInfo.FileVersion", script, StringComparison.Ordinal);
        Assert.Contains("$versionInfo.ProductVersion.IndexOf($ExpectedCommit", script, StringComparison.Ordinal);
        Assert.DoesNotContain("<AssemblyName>", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CrossETF.Terminal.UiShell.Reference.dll\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesNormalEightSecondStartupValidationWithoutKill()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        Assert.Contains("Start-Sleep -Seconds 8", script, StringComparison.Ordinal);
        Assert.Contains("CloseMainWindow()", script, StringComparison.Ordinal);
        Assert.Contains("WaitForExit(15000)", script, StringComparison.Ordinal);
        Assert.Contains("正式 EXE", script, StringComparison.Ordinal);
        Assert.Contains("桌面快捷方式", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Stop-Process", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Kill(", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_CreatesAndAtomicallyReplacesDesktopShortcut()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        Assert.Contains("New-Object -ComObject WScript.Shell", script, StringComparison.Ordinal);
        Assert.Contains("SpecialFolders.Item(\"Desktop\")", script, StringComparison.Ordinal);
        Assert.Contains("跨境ETF.lnk", script, StringComparison.Ordinal);
        Assert.Contains("$temporaryShortcut.TargetPath = $releaseExePath", script, StringComparison.Ordinal);
        Assert.Contains("$temporaryShortcut.WorkingDirectory = $targetDirectory", script, StringComparison.Ordinal);
        Assert.Contains("$temporaryShortcut.IconLocation = $iconLocation", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::Replace", script, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Hotkey", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_DoesNotUpdateShortcutBeforeExecutableValidation()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        int directLaunch = script.IndexOf("-Label \"正式 EXE\"", StringComparison.Ordinal);
        int shortcutCreation = script.IndexOf("New-Object -ComObject WScript.Shell", StringComparison.Ordinal);
        Assert.True(directLaunch >= 0 && directLaunch < shortcutCreation);
        Assert.Contains("恢复原桌面快捷方式", script, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $targetBackupDirectory -Destination $targetDirectory", script, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }
}
