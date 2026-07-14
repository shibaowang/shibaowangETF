using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Runtime;

public sealed class RuntimeHealthFileStoreTests
{
    [Fact]
    public async Task Append_WritesOneCompleteJsonObjectPerLine()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);

        RuntimeHealthFileWriteResult result = await store.AppendSnapshotAsync(Snapshot(At(14), processId: 321));

        Assert.True(result.Success);
        string[] lines = await File.ReadAllLinesAsync(result.FilePath!);
        string line = Assert.Single(lines);
        using JsonDocument document = JsonDocument.Parse(line);
        Assert.Equal(321, document.RootElement.GetProperty("processId").GetInt32());
    }

    [Fact]
    public async Task Append_UsesUtf8WithoutSensitiveBusinessFields()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);

        RuntimeHealthFileWriteResult result = await store.AppendSnapshotAsync(Snapshot(At(14), processId: 322));
        string content = await File.ReadAllTextAsync(result.FilePath!);

        Assert.Contains("applicationVersion", content, StringComparison.Ordinal);
        Assert.DoesNotContain("TradeLog", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountBalance", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PositionQuantity", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Append_FileNameContainsDateAndProcessId()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);

        RuntimeHealthFileWriteResult result = await store.AppendSnapshotAsync(Snapshot(At(14), processId: 9876));

        Assert.Equal("runtime-health-20260714-pid9876.jsonl", Path.GetFileName(result.FilePath));
    }

    [Fact]
    public async Task Append_SwitchesFileOnNaturalDayBoundary()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);

        RuntimeHealthFileWriteResult first = await store.AppendSnapshotAsync(Snapshot(At(14), 10));
        RuntimeHealthFileWriteResult second = await store.AppendSnapshotAsync(Snapshot(At(15), 10));

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Contains("20260714", first.FilePath, StringComparison.Ordinal);
        Assert.Contains("20260715", second.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Append_RollsToPartTwoAtConfiguredSize()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path, maximumFileBytes: 1);

        await store.AppendSnapshotAsync(Snapshot(At(14), 11));
        RuntimeHealthFileWriteResult second = await store.AppendSnapshotAsync(Snapshot(At(14).AddSeconds(30), 11));

        Assert.EndsWith("-part2.jsonl", second.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Append_AllowsConcurrentReaders()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        RuntimeHealthFileWriteResult result = await store.AppendSnapshotAsync(Snapshot(At(14), 12));

        using var reader = new FileStream(result.FilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        RuntimeHealthFileWriteResult second = await store.AppendSnapshotAsync(Snapshot(At(14).AddSeconds(30), 12));

        Assert.True(second.Success);
        Assert.True(reader.CanRead);
    }

    [Fact]
    public async Task ConcurrentAppends_RemainCompleteLines()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            store.AppendSnapshotAsync(Snapshot(At(14).AddSeconds(index), 13))));

        string path = Assert.Single(Directory.GetFiles(directory.Path, "*.jsonl"));
        string[] lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(12, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public async Task Retention_KeepsMostRecentSevenNaturalDays()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path, retentionDays: 7);
        string old = Path.Combine(directory.Path, "runtime-health-20260707-pid1.jsonl");
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(old, "{}\n");

        await store.AppendSnapshotAsync(Snapshot(At(14), 14));

        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task Retention_DoesNotDeleteBoundaryDay()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path, retentionDays: 7);
        string boundary = Path.Combine(directory.Path, "runtime-health-20260708-pid1.jsonl");
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(boundary, "{}\n");

        await store.AppendSnapshotAsync(Snapshot(At(14), 15));

        Assert.True(File.Exists(boundary));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("market_quote_cache.db")]
    [InlineData("runtime-health-custom.jsonl")]
    public async Task Retention_DoesNotDeleteUncontrolledFiles(string fileName)
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        string path = Path.Combine(directory.Path, fileName);
        await File.WriteAllTextAsync(path, "keep");

        await store.AppendSnapshotAsync(Snapshot(At(14), 16));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Retention_DoesNotDeleteReportsDirectory()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        Directory.CreateDirectory(store.ReportsDirectory);
        string report = Path.Combine(store.ReportsDirectory, "runtime-health-report-20260701-120000.json");
        await File.WriteAllTextAsync(report, "{}");

        await store.AppendSnapshotAsync(Snapshot(At(14), 17));

        Assert.True(File.Exists(report));
    }

    [Fact]
    public async Task RetentionDeleteFailure_DoesNotFailNewSample()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path, retentionDays: 7);
        string readOnlyOld = Path.Combine(directory.Path, "runtime-health-20260707-pid1.jsonl");
        await File.WriteAllTextAsync(readOnlyOld, "{}\n");
        File.SetAttributes(readOnlyOld, FileAttributes.ReadOnly);

        RuntimeHealthFileWriteResult result = await store.AppendSnapshotAsync(Snapshot(At(14), 171));

        Assert.True(result.Success);
        Assert.True(File.Exists(result.FilePath));
        Assert.True(File.Exists(readOnlyOld));
        File.SetAttributes(readOnlyOld, FileAttributes.Normal);
    }

    [Fact]
    public async Task FailedAppend_DoesNotThrowOrRunRetention()
    {
        using var directory = new TemporaryDirectory();
        string fileInsteadOfDirectory = Path.Combine(directory.Path, "occupied");
        await File.WriteAllTextAsync(fileInsteadOfDirectory, "x");
        var store = new RuntimeHealthFileStore(fileInsteadOfDirectory);

        RuntimeHealthFileWriteResult result = await store.AppendSnapshotAsync(Snapshot(At(14), 18));

        Assert.False(result.Success);
        Assert.True(File.Exists(fileInsteadOfDirectory));
    }

    [Fact]
    public async Task ReadSnapshotsSince_FiltersByTimestampAndSorts()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        await store.AppendSnapshotAsync(Snapshot(At(14).AddHours(2), 19));
        await store.AppendSnapshotAsync(Snapshot(At(14), 19));

        IReadOnlyList<RuntimeHealthSnapshot> result = await store.ReadSnapshotsSinceAsync(At(14).AddHours(1));

        RuntimeHealthSnapshot snapshot = Assert.Single(result);
        Assert.Equal(At(14).AddHours(2), snapshot.Timestamp);
    }

    [Fact]
    public async Task ReadSnapshotsSince_IgnoresMalformedTrailingLine()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        RuntimeHealthFileWriteResult write = await store.AppendSnapshotAsync(Snapshot(At(14), 20));
        await File.AppendAllTextAsync(write.FilePath!, "{truncated\n");

        IReadOnlyList<RuntimeHealthSnapshot> result = await store.ReadSnapshotsSinceAsync(At(14).AddMinutes(-1));

        Assert.Single(result);
    }

    [Fact]
    public void TryWriteError_DeduplicatesSameFailureWithinFiveMinutes()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);

        store.TryWriteError("WRITE", "same", At(14));
        store.TryWriteError("WRITE", "same", At(14).AddMinutes(1));

        string path = Assert.Single(Directory.GetFiles(directory.Path, "*.log"));
        Assert.Single(File.ReadAllLines(path));
    }

    private static RuntimeHealthSnapshot Snapshot(DateTimeOffset timestamp, int processId)
        => new()
        {
            Timestamp = timestamp,
            ApplicationVersion = "V8.4.0",
            ProcessId = processId,
            ProcessStartTime = timestamp.AddHours(-1),
            HealthStatus = RuntimeHealthStatus.Normal,
            WorkingSetBytes = 100,
            PrivateMemoryBytes = 90,
            ManagedHeapBytes = 30,
            ThreadCount = 20,
            HandleCount = 200
        };

    private static DateTimeOffset At(int day)
        => new(2026, 7, day, 12, 0, 0, TimeSpan.FromHours(8));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "crossetf-runtime-health-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
