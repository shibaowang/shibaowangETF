namespace CrossETF.Terminal.UiShell.Reference.Tests.Packaging;

public sealed class InstallerPackagingScriptTests
{
    private const string FixedAppId = "AppId={{C1935940-49E2-4F33-BAF2-70E991F37959}";

    [Fact]
    public void InnoSetup_UsesFixedAppId()
        => Assert.Contains(FixedAppId, ReadInstallerScript(), StringComparison.Ordinal);

    [Fact]
    public void InnoSetup_DefaultsToAcceptedApplicationVersion()
        => Assert.Contains("#define MyAppVersion \"8.10.5\"", ReadInstallerScript(), StringComparison.Ordinal);

    [Fact]
    public void InnoSetup_UsesRequiredInstallerFileName()
        => Assert.Contains("OutputBaseFilename=跨境ETF安装程序_v{#MyAppVersion}_win-x64", ReadInstallerScript(), StringComparison.Ordinal);

    [Fact]
    public void InnoSetup_UsesPerUserStableInstallDirectory()
        => Assert.Contains("DefaultDirName={localappdata}\\Programs\\CrossETF", ReadInstallerScript(), StringComparison.Ordinal);

    [Fact]
    public void InnoSetup_RequiresLowestPrivileges()
        => Assert.Contains("PrivilegesRequired=lowest", ReadInstallerScript(), StringComparison.Ordinal);

    [Fact]
    public void InnoSetup_UsesX64CompatibleInstallMode()
    {
        string script = ReadInstallerScript();
        Assert.Contains("ArchitecturesAllowed=x64compatible", script, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64compatible", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoSetup_UsesSimplifiedChineseAndApplicationIcon()
    {
        string script = ReadInstallerScript();
        Assert.Contains("{#SourcePath}\\Languages\\ChineseSimplified.isl", script, StringComparison.Ordinal);
        string language = ReadRepositoryFile("installer", "Languages", "ChineseSimplified.isl");
        Assert.Contains("LanguageName=简体中文", language, StringComparison.Ordinal);
        Assert.Contains("SetupIconFile={#IconFile}", script, StringComparison.Ordinal);
        Assert.Contains("UninstallDisplayIcon={app}\\{#MyAppExeName}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoSetup_DesktopShortcutIsOptionalAndCheckedByDefault()
    {
        string script = ReadInstallerScript();
        Assert.Contains("Name: \"desktopicon\"", script, StringComparison.Ordinal);
        Assert.Contains("Flags: checkedonce", script, StringComparison.Ordinal);
        Assert.Contains("{autodesktop}\\{#MyAppName}", script, StringComparison.Ordinal);
        Assert.Contains("Tasks: desktopicon", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoSetup_CreatesStartMenuShortcut()
        => Assert.Contains("{autoprograms}\\{#MyAppName}", ReadInstallerScript(), StringComparison.Ordinal);

    [Fact]
    public void InnoSetup_UninstallDoesNotDeleteUserDatabaseDirectory()
    {
        string script = ReadInstallerScript();
        Assert.DoesNotContain("[UninstallDelete]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CrossETF.Terminal.UiShell.Reference", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoSetup_FileListDoesNotNameDatabaseOrDeveloperArtifacts()
    {
        string script = ReadInstallerScript();
        string[] forbidden = { "*.db", "*.sqlite", "*.sqlite3", "*-wal", "*-shm", "*.pdb", "*.log", "TestResults", "Tests\\" };
        Assert.All(forbidden, value => Assert.DoesNotContain(value, script, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InnoSetup_UsesGracefulCloseWithoutKillCommands()
    {
        string script = ReadInstallerScript();
        Assert.Contains("CloseApplications=yes", script, StringComparison.Ordinal);
        Assert.Contains("CloseApplicationsFilter={#MyAppExeName}", script, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TerminateProcess", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InnoSetup_OffersPostInstallRunButSilentInstallCannotLaunch()
    {
        string script = ReadInstallerScript();
        Assert.Contains("postinstall", script, StringComparison.Ordinal);
        Assert.Contains("skipifsilent", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_UsesWinX64SelfContainedPublish()
    {
        string script = ReadBuildScript();
        Assert.Contains("\"-r\", \"win-x64\"", script, StringComparison.Ordinal);
        Assert.Contains("\"--self-contained\", \"true\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_DisablesSingleFileTrimmingAndDebugSymbols()
    {
        string script = ReadBuildScript();
        Assert.Contains("-p:PublishSingleFile=false", script, StringComparison.Ordinal);
        Assert.Contains("-p:PublishTrimmed=false", script, StringComparison.Ordinal);
        Assert.Contains("-p:DebugType=None", script, StringComparison.Ordinal);
        Assert.Contains("-p:DebugSymbols=false", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_RejectsDirtyWorktree()
    {
        string script = ReadBuildScript();
        Assert.Contains("status\", \"--porcelain=v1\", \"--untracked-files=all", script, StringComparison.Ordinal);
        Assert.Contains("worktree 不干净", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_RejectsNonDetachedHead()
    {
        string script = ReadBuildScript();
        Assert.Contains("symbolic-ref\", \"-q\", \"HEAD", script, StringComparison.Ordinal);
        Assert.Contains("必须是 detached HEAD", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_RequiresExactAnnotatedRemoteTag()
    {
        string script = ReadBuildScript();
        Assert.Contains("describe\", \"--tags\", \"--exact-match\", \"HEAD", script, StringComparison.Ordinal);
        Assert.Contains("cat-file\", \"-t\", $TagName", script, StringComparison.Ordinal);
        Assert.Contains("ls-remote\", \"--tags\", \"origin", script, StringComparison.Ordinal);
        Assert.Contains("不是 annotated tag", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_RejectsSameVersionDifferentContentWithoutOverwrite()
    {
        string script = ReadBuildScript();
        Assert.Contains("同版本安装程序内容不同，拒绝静默覆盖", script, StringComparison.Ordinal);
        Assert.Contains("$preserveStaging = $true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $finalDirectory", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_WritesInstallerSha256File()
    {
        string script = ReadBuildScript();
        Assert.Contains("Get-FileHash -LiteralPath $stagedInstallerPath -Algorithm SHA256", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", script, StringComparison.Ordinal);
        Assert.Contains("[System.Text.UTF8Encoding]::new($false)", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt 写入校验失败", script, StringComparison.Ordinal);
        Assert.Contains("InstallerSha256", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_ValidatesRequiredSelfContainedRuntimeFiles()
    {
        string script = ReadBuildScript();
        foreach (string file in new[] { "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "PresentationFramework.dll", "Microsoft.Data.Sqlite.dll", "e_sqlite3.dll" })
        {
            Assert.Contains($"\"{file}\"", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildScript_OnlyPackagesValidatedPublishPayload()
    {
        string script = ReadBuildScript();
        Assert.Contains("Assert-PublishPayload $publishDirectory", script, StringComparison.Ordinal);
        Assert.Contains("/DSourceDir=$publishDirectory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item -LiteralPath $source", script, StringComparison.Ordinal);
    }

    private static string ReadInstallerScript()
        => ReadRepositoryFile("installer", "CrossETF.iss");

    private static string ReadBuildScript()
        => ReadRepositoryFile("scripts", "Build-CrossEtfInstaller.ps1");

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(relativeParts));
    }
}
