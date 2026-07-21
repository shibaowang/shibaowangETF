using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CrossETF.Terminal.UiShell.Reference;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Release;

public sealed class ReleasePackagingScriptFunctionTests
{
    private const string Version = "8.10.10";
    private const string DummyCommit = "0000000000000000000000000000000000000000";

    [Fact]
    public void ValidationModeGate_SkipsStaticAndRunsLaunchActionOnlyForLaunch()
    {
        string output = RunPowerShell(
            "$script:count = 0; " +
            "Invoke-ApplicationLaunchForMode Static { $script:count++ }; " +
            "$staticCount = $script:count; " +
            "Invoke-ApplicationLaunchForMode Launch { $script:count++ }; " +
            "Write-Output \"$staticCount|$script:count\"");

        Assert.Contains("0|1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationMode_InvalidValueIsRejectedByParameterBindingBeforeMainFlow()
    {
        string script = FindRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        using var temp = new TempDirectory();
        ProcessResult result = RunProcess(
            "powershell.exe",
            [
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", script,
                "-Version", Version,
                "-SourcePath", temp.Path,
                "-ExpectedCommit", DummyCommit,
                "-ValidationMode", "Unsafe"
            ],
            temp.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ValidationMode", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet publish", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseSource_AcceptsDetachedCleanAnnotatedTagWithMatchingLocalOrigin()
    {
        using ReleaseRepository repository = ReleaseRepository.Create(annotatedTag: true, detach: true);

        string output = RunPowerShell(
            $"$result = Assert-ReleaseSource {Ps(repository.WorkPath)} {Ps(Version)} {Ps(repository.TaggedCommit)}; " +
            "Write-Output \"$($result.TagName)|$($result.Commit)\"");

        Assert.Contains($"v{Version}|{repository.TaggedCommit}", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseSource_RejectsMainDirtyAndStagedWorktrees()
    {
        using ReleaseRepository mainRepository = ReleaseRepository.Create(annotatedTag: true, detach: false);
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(mainRepository.WorkPath)} {Ps(Version)} {Ps(mainRepository.TaggedCommit)}",
            "detached HEAD");

        using ReleaseRepository dirtyRepository = ReleaseRepository.Create(annotatedTag: true, detach: true);
        File.WriteAllText(Path.Combine(dirtyRepository.WorkPath, "dirty.txt"), "dirty");
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(dirtyRepository.WorkPath)} {Ps(Version)} {Ps(dirtyRepository.TaggedCommit)}",
            "worktree");

        using ReleaseRepository stagedRepository = ReleaseRepository.Create(annotatedTag: true, detach: true);
        File.WriteAllText(Path.Combine(stagedRepository.WorkPath, "staged.txt"), "staged");
        RunGit(stagedRepository.WorkPath, "add", "staged.txt");
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(stagedRepository.WorkPath)} {Ps(Version)} {Ps(stagedRepository.TaggedCommit)}",
            "worktree");
    }

    [Fact]
    public void ReleaseSource_RejectsLightweightWrongExactAndMismatchedHead()
    {
        using ReleaseRepository lightweight = ReleaseRepository.Create(annotatedTag: false, detach: true);
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(lightweight.WorkPath)} {Ps(Version)} {Ps(lightweight.TaggedCommit)}",
            "annotated tag");

        using ReleaseRepository wrongExact = ReleaseRepository.Create(annotatedTag: true, detach: true);
        RunGit(wrongExact.WorkPath, "checkout", "--detach", wrongExact.InitialCommit);
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(wrongExact.WorkPath)} {Ps(Version)} {Ps(wrongExact.InitialCommit)}",
            "exact tag");

        using ReleaseRepository wrongHead = ReleaseRepository.Create(annotatedTag: true, detach: true);
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(wrongHead.WorkPath)} {Ps(Version)} {Ps(wrongHead.InitialCommit)}",
            "commit 不匹配");
    }

    [Fact]
    public void ReleaseSource_RejectsMissingOrMismatchedOriginTag()
    {
        using ReleaseRepository missing = ReleaseRepository.Create(annotatedTag: true, detach: true);
        RunGit(missing.OriginPath, "update-ref", "-d", $"refs/tags/v{Version}");
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(missing.WorkPath)} {Ps(Version)} {Ps(missing.TaggedCommit)}",
            "origin");

        using ReleaseRepository mismatch = ReleaseRepository.Create(annotatedTag: true, detach: true);
        RunGit(mismatch.OriginPath, "update-ref", $"refs/tags/v{Version}", mismatch.InitialCommit);
        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(mismatch.WorkPath)} {Ps(Version)} {Ps(mismatch.TaggedCommit)}",
            "origin");
    }

    [Theory]
    [InlineData("8.10", DummyCommit, "Version 格式")]
    [InlineData(Version, "1234", "40 位")]
    public void ReleaseSource_RejectsInvalidVersionAndIncompleteCommit(
        string version,
        string expectedCommit,
        string expectedMessage)
    {
        using var temp = new TempDirectory();

        AssertPowerShellFails(
            $"Assert-ReleaseSource {Ps(temp.Path)} {Ps(version)} {Ps(expectedCommit)}",
            expectedMessage);
    }

    [Theory]
    [InlineData("state.db")]
    [InlineData("state.db-wal")]
    [InlineData("state.db-shm")]
    [InlineData("trace.pdb")]
    [InlineData("Tests/helper.dll")]
    [InlineData("TestResults/result.trx")]
    [InlineData("CrossETF.Tests.dll")]
    [InlineData("testhost.exe")]
    [InlineData("runtime.log")]
    [InlineData("desktop.lnk")]
    [InlineData("fault-injection.json")]
    public void PackagePollution_RejectsForbiddenFilesAndReportsRelativePath(string relativePath)
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "forbidden");

        AssertPowerShellFails($"Assert-PackagePollution {Ps(temp.Path)}", relativePath.Replace('\\', '/'));
    }

    [Fact]
    public void PackagePollution_AllowsCleanRuntimeFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(temp.Path, "application.dll"), "runtime");

        string output = RunPowerShell($"Assert-PackagePollution {Ps(temp.Path)}; Write-Output CLEAN");

        Assert.Contains("CLEAN", output, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedPackage_ValidatesCoreVersionCommitAssemblyAndHashes()
    {
        using PackageFixture fixture = PackageFixture.Create();

        string output = RunPowerShell(
            $"$result = Assert-PublishedPackage {Ps(fixture.Path)} {Ps(Version)} {Ps(fixture.Commit)}; " +
            "Write-Output \"$($result.FileVersion)|$($result.ProductVersion)|$($result.AssemblyName)|$($result.ExecutableSha256)|$($result.ManifestHash)\"");

        Assert.Contains($"8.10.10.0|8.10.10+{fixture.Commit}|CrossETF.Terminal.UiShell.Reference", output, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("[A-F0-9]{64}\\|[A-F0-9]{64}", output);
    }

    [Fact]
    public void PublishedPackage_RejectsMissingEnglishEmptyAndVersionFailures()
    {
        using PackageFixture missing = PackageFixture.Create();
        File.Delete(Path.Combine(missing.Path, "CrossETF.Terminal.UiShell.Reference.runtimeconfig.json"));
        AssertPowerShellFails(
            $"Assert-PublishedPackage {Ps(missing.Path)} {Ps(Version)} {Ps(missing.Commit)}",
            "缺少核心文件");

        using PackageFixture english = PackageFixture.Create();
        File.WriteAllText(Path.Combine(english.Path, "CrossETF.Terminal.UiShell.Reference.exe"), "old apphost");
        AssertPowerShellFails(
            $"Assert-PublishedPackage {Ps(english.Path)} {Ps(Version)} {Ps(english.Commit)}",
            "英文名称 EXE");

        using PackageFixture empty = PackageFixture.Create();
        File.WriteAllBytes(Path.Combine(empty.Path, "e_sqlite3.dll"), []);
        AssertPowerShellFails(
            $"Assert-PublishedPackage {Ps(empty.Path)} {Ps(Version)} {Ps(empty.Commit)}",
            "核心文件为空");

        using PackageFixture fileVersion = PackageFixture.Create();
        AssertPowerShellFails(
            $"Assert-PublishedPackage {Ps(fileVersion.Path)} '8.10.4' {Ps(fileVersion.Commit)}",
            "FileVersion");

        using PackageFixture productVersion = PackageFixture.Create();
        AssertPowerShellFails(
            $"Assert-PublishedPackage {Ps(productVersion.Path)} {Ps(Version)} {Ps(DummyCommit)}",
            "ProductVersion");
    }

    [Theory]
    [InlineData("CrossETF.Terminal.UiShell.Reference.dll")]
    [InlineData("CrossETF.Terminal.UiShell.Reference.deps.json")]
    [InlineData("CrossETF.Terminal.UiShell.Reference.runtimeconfig.json")]
    public void PublishedPackage_RejectsEachRequiredApplicationArtifact(string relativePath)
    {
        using PackageFixture fixture = PackageFixture.Create();
        File.Delete(Path.Combine(fixture.Path, relativePath));

        AssertPowerShellFails(
            $"Assert-PublishedPackage {Ps(fixture.Path)} {Ps(Version)} {Ps(fixture.Commit)}",
            relativePath);
    }

    [Fact]
    public void PublishedPackage_RejectsWrongAssemblyName()
    {
        using PackageFixture fixture = PackageFixture.Create();
        string testAssembly = typeof(ReleasePackagingScriptFunctionTests).Assembly.Location;
        File.Copy(testAssembly, Path.Combine(fixture.Path, "CrossETF.Terminal.UiShell.Reference.dll"), overwrite: true);

        AssertPowerShellFails(
            $"Assert-PublishedPackage {Ps(fixture.Path)} {Ps(Version)} {Ps(fixture.Commit)}",
            "AssemblyName");
    }

    [Fact]
    public void Manifest_IsDeterministicAcrossRootsOrderTimestampsAndCulture()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        WriteManifestFixture(first.Path, reverse: false);
        WriteManifestFixture(second.Path, reverse: true);
        File.SetLastWriteTimeUtc(Path.Combine(second.Path, "sub", "b.txt"), DateTime.UtcNow.AddYears(-3));

        string firstHash = RunPowerShell($"Write-Output (Get-ReleaseManifestHash {Ps(first.Path)})").Trim();
        string secondHash = RunPowerShell(
            $"[System.Globalization.CultureInfo]::CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'); " +
            $"Write-Output (Get-ReleaseManifestHash {Ps(second.Path)})").Trim();
        string manifest = RunPowerShell($"Get-ReleaseManifest {Ps(first.Path)}");

        Assert.Equal(firstHash, secondHash);
        Assert.Contains("sub/b.txt", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Path, manifest, StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(Path.Combine(second.Path, "sub", "b.txt"), "changed length and content");
        string changedHash = RunPowerShell($"Write-Output (Get-ReleaseManifestHash {Ps(second.Path)})").Trim();
        Assert.NotEqual(firstHash, changedHash);

        File.Move(Path.Combine(second.Path, "a.txt"), Path.Combine(second.Path, "renamed.txt"));
        string renamedHash = RunPowerShell($"Write-Output (Get-ReleaseManifestHash {Ps(second.Path)})").Trim();
        Assert.NotEqual(changedHash, renamedHash);
    }

    [Fact]
    public void PromoteDirectory_HandlesNewIdenticalAndConflictWithoutTouchingHistory()
    {
        using var outputRoot = new TempDirectory();
        string history = Path.Combine(outputRoot.Path, "v8.10.2");
        Directory.CreateDirectory(history);
        File.WriteAllText(Path.Combine(history, "history.txt"), "stable");
        string historyHash = HashDirectory(history);

        string firstStage = CreateStage(outputRoot.Path, "one");
        string target = Path.Combine(outputRoot.Path, "v8.10.3");
        string promoted = RunPowerShell(
            $"$result = Promote-ReleaseDirectory {Ps(firstStage)} {Ps(target)}; Write-Output $result.Status");
        Assert.Contains("Promoted", promoted, StringComparison.Ordinal);
        Assert.True(Directory.Exists(target));
        Assert.False(Directory.Exists(firstStage));

        string identicalStage = CreateStage(outputRoot.Path, "one");
        string identical = RunPowerShell(
            $"$result = Promote-ReleaseDirectory {Ps(identicalStage)} {Ps(target)}; Write-Output $result.Status");
        Assert.Contains("ExistingIdentical", identical, StringComparison.Ordinal);
        Assert.False(Directory.Exists(identicalStage));

        string targetHash = HashDirectory(target);
        string conflictingStage = CreateStage(outputRoot.Path, "different");
        AssertPowerShellFails(
            $"Promote-ReleaseDirectory {Ps(conflictingStage)} {Ps(target)}",
            "内容不同");
        Assert.True(Directory.Exists(conflictingStage));
        Assert.Equal(targetHash, HashDirectory(target));
        Assert.Equal(historyHash, HashDirectory(history));
    }

    [Fact]
    public void Shortcut_FunctionsCreateValidateAndAtomicallyReplaceInsideTemporaryDirectory()
    {
        using var temp = new TempDirectory();
        string oldTarget = CreateExecutablePlaceholder(temp.Path, "old.exe");
        string newTarget = CreateExecutablePlaceholder(temp.Path, "new.exe");
        string installed = Path.Combine(temp.Path, "跨境ETF.lnk");
        string temporary = Path.Combine(temp.Path, "跨境ETF.__new.tmp.lnk");

        string command =
            $"New-ReleaseShortcut {Ps(installed)} {Ps(oldTarget)} {Ps(temp.Path)} {Ps(oldTarget + ",0")} 'Old V'; " +
            $"New-ReleaseShortcut {Ps(temporary)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'New V8.10.3'; " +
            $"Install-ReleaseShortcut {Ps(temporary)} {Ps(installed)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'New V8.10.3'; " +
            $"$result = Assert-ReleaseShortcut {Ps(installed)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'New V8.10.3'; " +
            "Write-Output $result.Description";

        string output = RunPowerShell(command);

        Assert.Contains("New V8.10.3", output, StringComparison.Ordinal);
        Assert.True(File.Exists(installed));
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public void Shortcut_PostInstallFailureRestoresOriginalHashAndProperties()
    {
        using var temp = new TempDirectory();
        string oldTarget = CreateExecutablePlaceholder(temp.Path, "old.exe");
        string newTarget = CreateExecutablePlaceholder(temp.Path, "new.exe");
        string installed = Path.Combine(temp.Path, "跨境ETF.lnk");
        string temporary = Path.Combine(temp.Path, "跨境ETF.__new.tmp.lnk");

        string command =
            $"New-ReleaseShortcut {Ps(installed)} {Ps(oldTarget)} {Ps(temp.Path)} {Ps(oldTarget + ",0")} 'Original'; " +
            $"$oldHash = (Get-FileHash {Ps(installed)} -Algorithm SHA256).Hash; " +
            $"New-ReleaseShortcut {Ps(temporary)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'Replacement'; " +
            $"try {{ Install-ReleaseShortcut {Ps(temporary)} {Ps(installed)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'Replacement' -PostInstallValidator {{ throw 'forced validation failure' }} }} catch {{ }}; " +
            $"$restored = Get-ReleaseShortcutProperties {Ps(installed)}; " +
            $"Write-Output \"$oldHash|$((Get-FileHash {Ps(installed)} -Algorithm SHA256).Hash)|$($restored.Description)\"";

        string output = RunPowerShell(command);
        string[] values = output.Trim().Split('|');

        Assert.True(values.Length >= 3, output);
        Assert.Equal(values[0], values[1]);
        Assert.Equal("Original", values[2]);
    }

    [Fact]
    public void Shortcut_RestoreFailureKeepsBackupAndRemovesInvalidInstalledLink()
    {
        using var temp = new TempDirectory();
        string oldTarget = CreateExecutablePlaceholder(temp.Path, "old.exe");
        string newTarget = CreateExecutablePlaceholder(temp.Path, "new.exe");
        string installed = Path.Combine(temp.Path, "跨境ETF.lnk");
        string temporary = Path.Combine(temp.Path, "跨境ETF.__new.tmp.lnk");

        string command =
            $"New-ReleaseShortcut {Ps(installed)} {Ps(oldTarget)} {Ps(temp.Path)} {Ps(oldTarget + ",0")} 'Original'; " +
            $"New-ReleaseShortcut {Ps(temporary)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'Replacement'; " +
            "function Invoke-AtomicFileReplace { param($SourcePath,$DestinationPath,$BackupPath,$Phase) " +
            "if($Phase -eq 'Restore'){ throw 'forced restore failure' }; [IO.File]::Replace($SourcePath,$DestinationPath,$BackupPath,$true) }; " +
            $"try {{ Install-ReleaseShortcut {Ps(temporary)} {Ps(installed)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'Replacement' -PostInstallValidator {{ throw 'forced validation failure' }} }} catch {{ Write-Output $_.Exception.Message }}; " +
            $"$backupCount = @(Get-ChildItem {Ps(temp.Path)} -Filter '跨境ETF.__backup_*.lnk').Count; " +
            $"Write-Output \"BACKUPS=$backupCount;INSTALLED=$(Test-Path {Ps(installed)})\"";

        string output = RunPowerShell(command);

        Assert.Contains("恢复失败", output, StringComparison.Ordinal);
        Assert.Contains("BACKUPS=1;INSTALLED=False", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shortcut_InvalidTemporaryPropertiesDoNotReplaceExistingShortcut()
    {
        using var temp = new TempDirectory();
        string oldTarget = CreateExecutablePlaceholder(temp.Path, "old.exe");
        string newTarget = CreateExecutablePlaceholder(temp.Path, "new.exe");
        string installed = Path.Combine(temp.Path, "跨境ETF.lnk");
        string temporary = Path.Combine(temp.Path, "跨境ETF.__new.tmp.lnk");

        string command =
            $"New-ReleaseShortcut {Ps(installed)} {Ps(oldTarget)} {Ps(temp.Path)} {Ps(oldTarget + ",0")} 'Original'; " +
            $"$oldHash = (Get-FileHash {Ps(installed)} -Algorithm SHA256).Hash; " +
            $"New-ReleaseShortcut {Ps(temporary)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'Wrong'; " +
            $"try {{ Install-ReleaseShortcut {Ps(temporary)} {Ps(installed)} {Ps(newTarget)} {Ps(temp.Path)} {Ps(newTarget + ",0")} 'Expected' }} catch {{ }}; " +
            $"Write-Output \"$oldHash|$((Get-FileHash {Ps(installed)} -Algorithm SHA256).Hash)\"";

        string output = RunPowerShell(command);
        string[] values = output.Trim().Split('|');

        Assert.Equal(values[0], values[1]);
    }

    [Fact]
    public void Shortcut_RejectsShortcutWhoseTargetNoLongerExists()
    {
        using var temp = new TempDirectory();
        string target = CreateExecutablePlaceholder(temp.Path, "removed.exe");
        string shortcut = Path.Combine(temp.Path, "removed-target.lnk");
        string command =
            $"New-ReleaseShortcut {Ps(shortcut)} {Ps(target)} {Ps(temp.Path)} {Ps(target + ",0")} 'Removed'; " +
            $"Remove-Item -LiteralPath {Ps(target)}; " +
            $"Assert-ReleaseShortcut {Ps(shortcut)} {Ps(target)} {Ps(temp.Path)} {Ps(target + ",0")} 'Removed'";

        AssertPowerShellFails(command, "目标不存在");
    }

    private static void WriteManifestFixture(string root, bool reverse)
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        IEnumerable<(string Relative, string Content)> files =
        [
            ("a.txt", "alpha"),
            ("sub/b.txt", "beta")
        ];
        if (reverse)
        {
            files = files.Reverse();
        }

        foreach ((string relative, string content) in files)
        {
            File.WriteAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), content);
        }
    }

    private static string CreateStage(string outputRoot, string content)
    {
        string stage = Path.Combine(outputRoot, $".__staging_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "payload.txt"), content);
        return stage;
    }

    private static string CreateExecutablePlaceholder(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "not executed");
        return path;
    }

    private static string HashDirectory(string path)
    {
        return RunPowerShell($"Write-Output (Get-ReleaseManifestHash {Ps(path)})").Trim();
    }

    private static void AssertPowerShellFails(string command, string expectedMessage)
    {
        ProcessResult result = RunPowerShellProcess(command);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedMessage, result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static string RunPowerShell(string command)
    {
        ProcessResult result = RunPowerShellProcess(command);
        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        return result.StandardOutput;
    }

    private static ProcessResult RunPowerShellProcess(string command)
    {
        string script = FindRepositoryFile("scripts", "Publish-CrossEtfRelease.ps1");
        string load = $". {Ps(script)} -Version {Ps(Version)} -SourcePath {Ps(Path.GetTempPath())} -ExpectedCommit {Ps(DummyCommit)}; ";
        return RunProcess(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", load + command],
            Path.GetTempPath());
    }

    private static ProcessResult RunProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start {fileName}.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), $"Process timed out: {fileName}");
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        ProcessResult result = RunProcess("git", arguments, workingDirectory);
        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        return result.StandardOutput.Trim();
    }

    private static string Ps(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CrossETF.ReleaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                foreach (string directory in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(directory, FileAttributes.Normal);
                }

                File.SetAttributes(Path, FileAttributes.Normal);
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class ReleaseRepository : IDisposable
    {
        private readonly TempDirectory _root;

        private ReleaseRepository(TempDirectory root, string originPath, string workPath, string initialCommit, string taggedCommit)
        {
            _root = root;
            OriginPath = originPath;
            WorkPath = workPath;
            InitialCommit = initialCommit;
            TaggedCommit = taggedCommit;
        }

        public string OriginPath { get; }
        public string WorkPath { get; }
        public string InitialCommit { get; }
        public string TaggedCommit { get; }

        public static ReleaseRepository Create(bool annotatedTag, bool detach)
        {
            var root = new TempDirectory();
            string origin = Path.Combine(root.Path, "origin.git");
            string work = Path.Combine(root.Path, "work");
            Directory.CreateDirectory(work);
            RunGit(root.Path, "init", "--bare", origin);
            RunGit(work, "init");

            File.WriteAllText(Path.Combine(work, "release.txt"), "initial");
            RunGit(work, "add", "release.txt");
            RunGit(
                work,
                "-c", "user.name=Release Test",
                "-c", "user.email=release-test@example.invalid",
                "commit", "-m", "initial");
            string initial = RunGit(work, "rev-parse", "HEAD");

            File.AppendAllText(Path.Combine(work, "release.txt"), "\ntagged");
            RunGit(work, "add", "release.txt");
            RunGit(
                work,
                "-c", "user.name=Release Test",
                "-c", "user.email=release-test@example.invalid",
                "commit", "-m", "tagged");
            string tagged = RunGit(work, "rev-parse", "HEAD");

            if (annotatedTag)
            {
                RunGit(
                    work,
                    "-c", "user.name=Release Test",
                    "-c", "user.email=release-test@example.invalid",
                    "tag", "-a", $"v{Version}", "-m", "release test tag");
            }
            else
            {
                RunGit(work, "tag", $"v{Version}");
            }

            RunGit(work, "remote", "add", "origin", origin);
            RunGit(work, "push", "origin", "HEAD:refs/heads/main", "--tags");
            if (detach)
            {
                RunGit(work, "checkout", "--detach", $"v{Version}");
            }

            return new ReleaseRepository(root, origin, work, initial, tagged);
        }

        public void Dispose() => _root.Dispose();
    }

    private sealed class PackageFixture : IDisposable
    {
        private readonly TempDirectory _root;

        private PackageFixture(TempDirectory root, string commit)
        {
            _root = root;
            Commit = commit;
        }

        public string Path => _root.Path;
        public string Commit { get; }

        public static PackageFixture Create()
        {
            string applicationAssembly = typeof(MainWindow).Assembly.Location;
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(applicationAssembly);
            Match match = Regex.Match(
                versionInfo.ProductVersion ?? string.Empty,
                "^8\\.10\\.10\\+([0-9a-fA-F]{40})$",
                RegexOptions.CultureInvariant);
            Assert.True(match.Success, $"Unexpected application ProductVersion: {versionInfo.ProductVersion}");

            var root = new TempDirectory();
            File.Copy(applicationAssembly, System.IO.Path.Combine(root.Path, "CrossETF.Terminal.UiShell.Reference.dll"));
            File.Copy(applicationAssembly, System.IO.Path.Combine(root.Path, "跨境ETF.exe"));
            foreach (string file in new[]
                     {
                         "CrossETF.Terminal.UiShell.Reference.deps.json",
                         "CrossETF.Terminal.UiShell.Reference.runtimeconfig.json",
                         "Microsoft.Data.Sqlite.dll",
                         "SQLitePCLRaw.batteries_v2.dll",
                         "SQLitePCLRaw.core.dll",
                         "SQLitePCLRaw.provider.e_sqlite3.dll",
                         "e_sqlite3.dll"
                     })
            {
                File.WriteAllText(System.IO.Path.Combine(root.Path, file), "fixture");
            }

            return new PackageFixture(root, match.Groups[1].Value);
        }

        public void Dispose() => _root.Dispose();
    }
}
