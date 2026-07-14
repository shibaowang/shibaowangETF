using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Runtime;

public sealed class RuntimeHealthReportExporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 14, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void BuildReport_UsesOnlyRequestedTwentyFourHourRange()
    {
        RuntimeHealthSnapshot[] samples =
        {
            Snapshot(Now.AddHours(-25), 1, 10),
            Snapshot(Now.AddHours(-23), 1, 20),
            Snapshot(Now, 1, 30)
        };

        RuntimeHealthReport report = RuntimeHealthReportExporter.BuildReport("V8.4.0", Now, Now.AddHours(-24), samples, 1);

        Assert.Equal(2, report.SampleCount);
        Assert.Equal(Now.AddHours(-23), report.SampleStartTime);
        Assert.Equal(Now, report.SampleEndTime);
    }

    [Fact]
    public void BuildReport_CountsEachHealthStatus()
    {
        RuntimeHealthSnapshot[] samples =
        {
            Snapshot(Now.AddMinutes(-3), 1, 10, RuntimeHealthStatus.Normal),
            Snapshot(Now.AddMinutes(-2), 1, 20, RuntimeHealthStatus.Warning),
            Snapshot(Now.AddMinutes(-1), 1, 30, RuntimeHealthStatus.Critical)
        };

        RuntimeHealthReport report = Build(samples);

        Assert.Equal(1, report.NormalSampleCount);
        Assert.Equal(1, report.WarningSampleCount);
        Assert.Equal(1, report.CriticalSampleCount);
    }

    [Fact]
    public void BuildReport_CalculatesMaximumAndAverageMetrics()
    {
        RuntimeHealthSnapshot[] samples =
        {
            Snapshot(Now.AddMinutes(-2), 1, 100) with
            {
                WorkingSetBytes = 150,
                ManagedHeapBytes = 40,
                ThreadCount = 20,
                HandleCount = 200,
                DispatcherLagMilliseconds = 10,
                MaximumDispatcherLagSinceLastSample = 30,
                LastUiRefreshDurationMilliseconds = 40
            },
            Snapshot(Now.AddMinutes(-1), 1, 200) with
            {
                WorkingSetBytes = 250,
                ManagedHeapBytes = 60,
                ThreadCount = 30,
                HandleCount = 300,
                DispatcherLagMilliseconds = 20,
                MaximumDispatcherLagSinceLastSample = 50,
                LastUiRefreshDurationMilliseconds = 80
            }
        };

        RuntimeHealthReport report = Build(samples);

        Assert.Equal(250, report.MaximumWorkingSetBytes);
        Assert.Equal(200, report.MaximumPrivateMemoryBytes);
        Assert.Equal(60, report.MaximumManagedHeapBytes);
        Assert.Equal(30, report.MaximumThreadCount);
        Assert.Equal(300, report.MaximumHandleCount);
        Assert.Equal(50, report.MaximumDispatcherLagMilliseconds);
        Assert.Equal(15, report.AverageDispatcherLagMilliseconds);
        Assert.Equal(80, report.MaximumUiRefreshDurationMilliseconds);
    }

    [Fact]
    public void BuildReport_IncludesStartCurrentMinimumAndMaximumPrivateMemory()
    {
        RuntimeHealthSnapshot[] samples =
        {
            Snapshot(Now.AddMinutes(-70), 1, 300),
            Snapshot(Now.AddMinutes(-40), 1, 100),
            Snapshot(Now, 1, 200)
        };

        RuntimeHealthReport report = Build(samples);

        Assert.Equal(300, report.StartingPrivateMemoryBytes);
        Assert.Equal(200, report.CurrentPrivateMemoryBytes);
        Assert.Equal(100, report.MinimumPrivateMemoryBytes);
        Assert.Equal(300, report.MaximumPrivateMemoryBytes);
        Assert.Equal(100, report.PrivateMemoryChange30MinutesBytes);
        Assert.Equal(-100, report.PrivateMemoryChange60MinutesBytes);
    }

    [Fact]
    public void BuildReport_IncludesStatusTransitionsOnlyWhenPresent()
    {
        RuntimeHealthSnapshot[] samples =
        {
            Snapshot(Now.AddMinutes(-2), 1, 100),
            Snapshot(Now.AddMinutes(-1), 1, 110) with { StatusTransition = "Normal -> Warning" }
        };

        RuntimeHealthReport report = Build(samples);

        string transition = Assert.Single(report.StatusTransitions);
        Assert.Contains("Normal -> Warning", transition, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_UsesLatestWindowCountsAndUptime()
    {
        RuntimeHealthSnapshot latest = Snapshot(Now, 1, 100) with
        {
            OpenChartWindowCount = 3,
            OpenManualEntryWindowCount = 2,
            OpenRiskCenterWindowCount = 1,
            UptimeSeconds = 7_200
        };

        RuntimeHealthReport report = Build(new[] { latest });

        Assert.Equal(3, report.OpenChartWindowCount);
        Assert.Equal(2, report.OpenManualEntryWindowCount);
        Assert.Equal(1, report.OpenRiskCenterWindowCount);
        Assert.Equal(7_200, report.UptimeSeconds);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void BuildReport_DetectsAbnormalExitEvidenceFromOldProcess(bool shutdownRequested, bool expected)
    {
        RuntimeHealthSnapshot oldProcess = Snapshot(Now.AddHours(-1), 99, 100) with
        {
            ApplicationShutdownRequested = shutdownRequested
        };

        RuntimeHealthReport report = RuntimeHealthReportExporter.BuildReport(
            "V8.4.0",
            Now,
            Now.AddHours(-24),
            new[] { oldProcess },
            currentProcessId: 1);

        Assert.Equal(expected, report.DetectedAbnormalExitEvidence);
    }

    [Fact]
    public void BuildReport_CurrentProcessWithoutShutdownMarker_IsNotFalseAbnormalExit()
    {
        RuntimeHealthReport report = Build(new[] { Snapshot(Now, 1, 100) });

        Assert.False(report.DetectedAbnormalExitEvidence);
    }

    [Fact]
    public async Task Export_WritesJsonAndTextReports()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        await store.AppendSnapshotAsync(Snapshot(Now.AddMinutes(-1), 1, 100));
        var exporter = new RuntimeHealthReportExporter(store, () => Now);

        RuntimeHealthReportExportResult result = await exporter.ExportLast24HoursAsync("V8.4.0", 1);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.TextPath));
        Assert.StartsWith(store.ReportsDirectory, result.JsonPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_ProducesExplicitEmptyReportWhenNoSamplesExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        var exporter = new RuntimeHealthReportExporter(store, () => Now);

        RuntimeHealthReportExportResult result = await exporter.ExportLast24HoursAsync("V8.4.0", 1);

        Assert.True(result.Success);
        Assert.Equal(0, result.Report!.SampleCount);
        Assert.Contains("空数据", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportedFiles_DoNotContainSensitiveBusinessData()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        await store.AppendSnapshotAsync(Snapshot(Now, 1, 100));
        var exporter = new RuntimeHealthReportExporter(store, () => Now);

        RuntimeHealthReportExportResult result = await exporter.ExportLast24HoursAsync("V8.4.0", 1);
        string combined = await File.ReadAllTextAsync(result.JsonPath!) + await File.ReadAllTextAsync(result.TextPath!);

        Assert.DoesNotContain("TradeLog", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountBalance", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StrategyParameter", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportFailure_ReturnsFailureWithoutThrowing()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        await File.WriteAllTextAsync(store.ReportsDirectory, "blocks directory");
        var exporter = new RuntimeHealthReportExporter(store, () => Now);

        RuntimeHealthReportExportResult result = await exporter.ExportLast24HoursAsync("V8.4.0", 1);

        Assert.False(result.Success);
        Assert.Null(result.Report);
    }

    [Fact]
    public async Task JsonReport_IsValidAndContainsVersion()
    {
        using var directory = new TemporaryDirectory();
        var store = new RuntimeHealthFileStore(directory.Path);
        await store.AppendSnapshotAsync(Snapshot(Now, 1, 100));
        var exporter = new RuntimeHealthReportExporter(store, () => Now);

        RuntimeHealthReportExportResult result = await exporter.ExportLast24HoursAsync("V8.4.0", 1);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonPath!));

        Assert.Equal("V8.4.0", json.RootElement.GetProperty("applicationVersion").GetString());
    }

    private static RuntimeHealthReport Build(IReadOnlyList<RuntimeHealthSnapshot> snapshots)
        => RuntimeHealthReportExporter.BuildReport("V8.4.0", Now, Now.AddHours(-24), snapshots, 1);

    private static RuntimeHealthSnapshot Snapshot(
        DateTimeOffset timestamp,
        int processId,
        long privateMemory,
        RuntimeHealthStatus status = RuntimeHealthStatus.Normal)
        => new()
        {
            Timestamp = timestamp,
            ApplicationVersion = "V8.4.0",
            ProcessId = processId,
            ProcessStartTime = timestamp.AddHours(-1),
            HealthStatus = status,
            WorkingSetBytes = privateMemory,
            PrivateMemoryBytes = privateMemory,
            ManagedHeapBytes = privateMemory / 2,
            ThreadCount = 20,
            HandleCount = 200
        };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "crossetf-runtime-report-tests",
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
