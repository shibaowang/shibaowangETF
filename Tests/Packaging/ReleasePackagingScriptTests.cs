namespace CrossETF.Terminal.UiShell.Reference.Tests.Release;

public class ReleasePackagingScriptTests
{
    [Fact]
    public void Script_ExposesExplicitValidationModeAtEndWithLaunchDefault()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        int expectedCommit = script.IndexOf("[string]$ExpectedCommit,", StringComparison.Ordinal);
        int validationMode = script.IndexOf("[string]$ValidationMode = \"Launch\"", StringComparison.Ordinal);

        Assert.True(expectedCommit >= 0 && validationMode > expectedCommit);
        Assert.Contains("[ValidateSet(\"Launch\", \"Static\")]", script, StringComparison.Ordinal);
        Assert.Contains("Launch runs all static checks", script, StringComparison.Ordinal);
        Assert.Contains("Static performs the complete publish", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SkipLaunchValidation", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_RequiresDetachedCleanAnnotatedTagAndMatchingOriginTag()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string sourceValidation = Slice(script, "function Assert-ReleaseSource", "function Get-ReleasePublishArguments");

        Assert.Contains("--is-inside-work-tree", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("--abbrev-ref", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("detached HEAD", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("--porcelain=v1", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("--exact-match", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("cat-file", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("annotated tag", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("ls-remote", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("refs/tags/$tagName^{}", sourceValidation, StringComparison.Ordinal);
        Assert.Contains("ExpectedCommit 必须是完整 40 位", sourceValidation, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesOneNoPdbPublishArgumentSetForBothModes()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string publishArguments = Slice(script, "function Get-ReleasePublishArguments", "function Invoke-ReleasePublish");

        Assert.Equal(1, Count(script, "& dotnet @arguments"));
        Assert.Contains("-c\", \"Release", publishArguments, StringComparison.Ordinal);
        Assert.Contains("-r\", \"win-x64", publishArguments, StringComparison.Ordinal);
        Assert.Contains("--self-contained\", \"false", publishArguments, StringComparison.Ordinal);
        Assert.Contains("-p:InformationalVersion=$Version+$ExpectedCommit", publishArguments, StringComparison.Ordinal);
        Assert.Contains("-p:IncludeSourceRevisionInInformationalVersion=false", publishArguments, StringComparison.Ordinal);
        Assert.Contains("-p:DebugType=None", publishArguments, StringComparison.Ordinal);
        Assert.Contains("-p:DebugSymbols=false", publishArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("if ($ValidationMode", publishArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_PublishesToSameVolumeStagingAndPromotesAtomically()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string promotion = Slice(script, "function Promote-ReleaseDirectory", "function Get-ProcessByExecutablePath");

        Assert.Contains(".__staging_v$Version", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::GetPathRoot", promotion, StringComparison.Ordinal);
        Assert.Contains("Get-ReleaseManifestHash $staging", promotion, StringComparison.Ordinal);
        Assert.Contains("ExistingIdentical", promotion, StringComparison.Ordinal);
        Assert.Contains("同版本正式目录已存在但内容不同", promotion, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Directory]::Move($staging, $target)", promotion, StringComparison.Ordinal);
        Assert.DoesNotContain("targetBackupDirectory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish $projectPath", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ValidatesVersionAssemblyCoreFilesPollutionAndHashes()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string packageValidation = Slice(script, "function Assert-PublishedPackage", "function Promote-ReleaseDirectory");
        string pollution = Slice(script, "function Assert-PackagePollution", "function Assert-PublishedPackage");

        Assert.Contains("$script:ReleaseExeName", packageValidation, StringComparison.Ordinal);
        Assert.Contains("$script:ReleaseExeName = \"跨境ETF.exe\"", script, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Data.Sqlite.dll", packageValidation, StringComparison.Ordinal);
        Assert.Contains("SQLitePCLRaw.core.dll", packageValidation, StringComparison.Ordinal);
        Assert.Contains("e_sqlite3.dll", packageValidation, StringComparison.Ordinal);
        Assert.Contains("FileVersion 不匹配", packageValidation, StringComparison.Ordinal);
        Assert.Contains("ProductVersion 不匹配", packageValidation, StringComparison.Ordinal);
        Assert.Contains("Managed Assembly InformationalVersion 不匹配", packageValidation, StringComparison.Ordinal);
        Assert.Contains("[System.Reflection.AssemblyName]::GetAssemblyName", packageValidation, StringComparison.Ordinal);
        Assert.Contains("ExecutableSha256", packageValidation, StringComparison.Ordinal);
        Assert.Contains("ManifestHash", packageValidation, StringComparison.Ordinal);
        Assert.Contains("pdb", pollution, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("testresults", pollution, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("发布包包含禁止文件", pollution, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ManifestUsesRelativeOrdinalContentHashWithoutTimestamps()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string manifest = Slice(script, "function Get-ReleaseManifest", "function Assert-PackagePollution");

        Assert.Contains("Get-NormalizedRelativePath", manifest, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", manifest, StringComparison.Ordinal);
        Assert.Contains("$file.Length", manifest, StringComparison.Ordinal);
        Assert.Contains("[System.StringComparer]::Ordinal", manifest, StringComparison.Ordinal);
        Assert.Contains("UTF8Encoding", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("LastWriteTime", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("CreationTime", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_StaticModeMakesApplicationLaunchActionUnreachable()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string modeGate = Slice(script, "function Invoke-ApplicationLaunchForMode", "function Get-CurrentUserDesktopDirectory");

        Assert.Contains("if ($ValidationMode -eq \"Static\")", modeGate, StringComparison.Ordinal);
        Assert.Contains("return $null", modeGate, StringComparison.Ordinal);
        Assert.True(modeGate.IndexOf("return $null", StringComparison.Ordinal) < modeGate.IndexOf("& $LaunchAction", StringComparison.Ordinal));
        Assert.Equal(1, Count(script, "Start-Process -FilePath"));
        Assert.DoesNotContain("Stop-Process", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Kill(", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("自动降级", script, StringComparison.Ordinal);
        Assert.Contains("ApplicationLaunch=Skipped", script, StringComparison.Ordinal);
        Assert.Contains("ShortcutLaunch=Skipped", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_LaunchModeRetainsExistingTimeoutAndCloseBehavior()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string launch = Slice(script, "function Test-ApplicationLaunch", "function Invoke-ApplicationLaunchForMode");

        Assert.Contains("AddSeconds(15)", launch, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds 8", launch, StringComparison.Ordinal);
        Assert.Contains("MainWindowHandle", launch, StringComparison.Ordinal);
        Assert.Contains("CloseMainWindow()", launch, StringComparison.Ordinal);
        Assert.Contains("WaitForExit(15000)", launch, StringComparison.Ordinal);
        Assert.Equal(2, Count(script, "Invoke-ApplicationLaunchForMode $ValidationMode"));
        Assert.Contains("-Label \"正式 EXE\"", script, StringComparison.Ordinal);
        Assert.Contains("-Label \"桌面快捷方式\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_CreatesValidatesAndAtomicallyInstallsShortcutWithoutCopyRollback()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string shortcutInstall = Slice(script, "function Install-ReleaseShortcut", "function Invoke-CrossEtfRelease");

        Assert.Contains("New-Object -ComObject WScript.Shell", script, StringComparison.Ordinal);
        Assert.Contains("FinalReleaseComObject", script, StringComparison.Ordinal);
        Assert.Contains("TargetPath", shortcutInstall, StringComparison.Ordinal);
        Assert.Contains("WorkingDirectory", shortcutInstall, StringComparison.Ordinal);
        Assert.Contains("IconLocation", shortcutInstall, StringComparison.Ordinal);
        Assert.Contains("Description", shortcutInstall, StringComparison.Ordinal);
        Assert.Contains("Invoke-AtomicFileReplace", shortcutInstall, StringComparison.Ordinal);
        Assert.Contains("Restore", shortcutInstall, StringComparison.Ordinal);
        Assert.Contains("恢复后的快捷方式 SHA-256", shortcutInstall, StringComparison.Ordinal);
        Assert.Contains("Backup=$backupPath", shortcutInstall, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item", shortcutInstall, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_DoesNotReferenceApplicationDatabaseOrRuntimeServices()
    {
        string script = ReadRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");

        Assert.DoesNotContain("LocalDatabase", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cross_etf_terminal.db", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TradeLog", script, StringComparison.Ordinal);
        Assert.DoesNotContain("MarketDataRefresh", script, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeHealth", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SmokeMode", script, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Slice(string source, string start, string end)
    {
        int startIndex = source.IndexOf(start, StringComparison.Ordinal);
        int endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source[startIndex..endIndex];
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        return File.ReadAllText(FindRepositoryFile(segments));
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }
}
