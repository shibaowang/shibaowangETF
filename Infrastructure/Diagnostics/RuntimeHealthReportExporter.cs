using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;

public sealed class RuntimeHealthReportExporter
{
    private readonly RuntimeHealthFileStore _fileStore;
    private readonly Func<DateTimeOffset> _clock;

    public RuntimeHealthReportExporter(RuntimeHealthFileStore fileStore, Func<DateTimeOffset>? clock = null)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public async Task<RuntimeHealthReportExportResult> ExportLast24HoursAsync(
        string applicationVersion,
        int currentProcessId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset generatedAt = _clock();
        DateTimeOffset rangeStart = generatedAt - TimeSpan.FromHours(24);
        try
        {
            IReadOnlyList<RuntimeHealthSnapshot> snapshots = await _fileStore
                .ReadSnapshotsSinceAsync(rangeStart, cancellationToken)
                .ConfigureAwait(false);
            RuntimeHealthReport report = BuildReport(
                applicationVersion,
                generatedAt,
                rangeStart,
                snapshots,
                currentProcessId);
            Directory.CreateDirectory(_fileStore.ReportsDirectory);
            string stem = ResolveUniqueReportStem(generatedAt);
            string jsonPath = Path.Combine(_fileStore.ReportsDirectory, stem + ".json");
            string textPath = Path.Combine(_fileStore.ReportsDirectory, stem + ".txt");
            string temporaryJsonPath = jsonPath + ".tmp";
            string temporaryTextPath = textPath + ".tmp";
            try
            {
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                    report,
                    new JsonSerializerOptions(RuntimeHealthFileStore.JsonOptions) { WriteIndented = true });
                byte[] text = Encoding.UTF8.GetBytes(BuildTextSummary(report));
                await WriteDurablyAsync(temporaryJsonPath, json, cancellationToken).ConfigureAwait(false);
                await WriteDurablyAsync(temporaryTextPath, text, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryJsonPath, jsonPath, overwrite: false);
                File.Move(temporaryTextPath, textPath, overwrite: false);
            }
            catch
            {
                SafeDelete(temporaryJsonPath);
                SafeDelete(temporaryTextPath);
                SafeDelete(jsonPath);
                SafeDelete(textPath);
                throw;
            }

            return new RuntimeHealthReportExportResult(
                true,
                snapshots.Count == 0 ? "已生成空数据运行健康报告。" : "运行健康报告导出成功。",
                jsonPath,
                textPath,
                report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _fileStore.TryWriteError("REPORT_EXPORT", ex.Message, generatedAt);
            return new RuntimeHealthReportExportResult(false, "运行健康报告导出失败：" + ex.Message, null, null, null);
        }
    }

    public static RuntimeHealthReport BuildReport(
        string applicationVersion,
        DateTimeOffset generatedAt,
        DateTimeOffset rangeStart,
        IReadOnlyList<RuntimeHealthSnapshot> snapshots,
        int currentProcessId)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        RuntimeHealthSnapshot[] ordered = snapshots
            .Where(snapshot => snapshot.Timestamp >= rangeStart && snapshot.Timestamp <= generatedAt)
            .OrderBy(snapshot => snapshot.Timestamp)
            .ToArray();
        RuntimeHealthSnapshot? latest = ordered.LastOrDefault();
        RuntimeHealthMemoryTrend trend = RuntimeHealthEvaluator.CalculateMemoryTrend(
            ordered,
            generatedAt,
            latest?.PrivateMemoryBytes ?? 0);
        bool abnormalExit = ordered
            .GroupBy(snapshot => snapshot.ProcessId)
            .Where(group => group.Key != currentProcessId)
            .Any(group => !group.OrderBy(snapshot => snapshot.Timestamp).Last().ApplicationShutdownRequested);

        return new RuntimeHealthReport
        {
            ApplicationVersion = applicationVersion,
            GeneratedAt = generatedAt,
            RangeStart = rangeStart,
            RangeEnd = generatedAt,
            SampleStartTime = ordered.FirstOrDefault()?.Timestamp,
            SampleEndTime = latest?.Timestamp,
            SampleCount = ordered.Length,
            NormalSampleCount = ordered.Count(snapshot => snapshot.HealthStatus == RuntimeHealthStatus.Normal),
            WarningSampleCount = ordered.Count(snapshot => snapshot.HealthStatus == RuntimeHealthStatus.Warning),
            CriticalSampleCount = ordered.Count(snapshot => snapshot.HealthStatus == RuntimeHealthStatus.Critical),
            MaximumWorkingSetBytes = MaxOrZero(ordered, snapshot => snapshot.WorkingSetBytes),
            MaximumPrivateMemoryBytes = MaxOrZero(ordered, snapshot => snapshot.PrivateMemoryBytes),
            StartingPrivateMemoryBytes = latest is null ? 0 : trend.StartPrivateMemoryBytes,
            CurrentPrivateMemoryBytes = latest is null ? 0 : trend.CurrentPrivateMemoryBytes,
            MinimumPrivateMemoryBytes = latest is null ? 0 : trend.MinimumPrivateMemoryBytes,
            MaximumManagedHeapBytes = MaxOrZero(ordered, snapshot => snapshot.ManagedHeapBytes),
            MaximumThreadCount = MaxOrZero(ordered, snapshot => snapshot.ThreadCount),
            MaximumHandleCount = MaxOrZero(ordered, snapshot => snapshot.HandleCount),
            MaximumDispatcherLagMilliseconds = MaxOrZero(ordered, snapshot => snapshot.MaximumDispatcherLagSinceLastSample),
            AverageDispatcherLagMilliseconds = ordered.Length == 0 ? 0 : ordered.Average(snapshot => snapshot.DispatcherLagMilliseconds),
            MaximumUiRefreshDurationMilliseconds = ordered
                .Where(snapshot => snapshot.LastUiRefreshDurationMilliseconds.HasValue)
                .Select(snapshot => snapshot.LastUiRefreshDurationMilliseconds!.Value)
                .DefaultIfEmpty(0)
                .Max(),
            PrivateMemoryChange30MinutesBytes = latest is null ? null : trend.Change30MinutesBytes,
            PrivateMemoryChange60MinutesBytes = latest is null ? null : trend.Change60MinutesBytes,
            StatusTransitions = ordered
                .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.StatusTransition))
                .Select(snapshot => $"{snapshot.Timestamp:O} {snapshot.StatusTransition}")
                .ToArray(),
            OpenChartWindowCount = latest?.OpenChartWindowCount ?? 0,
            OpenManualEntryWindowCount = latest?.OpenManualEntryWindowCount ?? 0,
            OpenRiskCenterWindowCount = latest?.OpenRiskCenterWindowCount ?? 0,
            UptimeSeconds = latest?.UptimeSeconds ?? 0,
            SamplingWriteErrorCount = ordered.Select(snapshot => snapshot.SamplingWriteErrorCount).DefaultIfEmpty(0).Max(),
            DetectedAbnormalExitEvidence = abnormalExit
        };
    }

    private string ResolveUniqueReportStem(DateTimeOffset generatedAt)
    {
        string baseStem = $"runtime-health-report-{generatedAt.LocalDateTime:yyyyMMdd-HHmmss}";
        string stem = baseStem;
        for (int suffix = 2; suffix < 10_000; suffix++)
        {
            if (!File.Exists(Path.Combine(_fileStore.ReportsDirectory, stem + ".json"))
                && !File.Exists(Path.Combine(_fileStore.ReportsDirectory, stem + ".txt")))
            {
                return stem;
            }

            stem = baseStem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
        }

        throw new IOException("无法生成唯一的运行健康报告文件名。");
    }

    private static string BuildTextSummary(RuntimeHealthReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("跨境ETF运行健康报告");
        text.AppendLine($"程序版本：{report.ApplicationVersion}");
        text.AppendLine($"生成时间：{report.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"统计范围：{report.RangeStart:yyyy-MM-dd HH:mm:ss} - {report.RangeEnd:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"采样时间：{FormatDateTime(report.SampleStartTime)} - {FormatDateTime(report.SampleEndTime)}");
        text.AppendLine($"样本数量：{report.SampleCount}");
        text.AppendLine($"正常/警告/严重：{report.NormalSampleCount}/{report.WarningSampleCount}/{report.CriticalSampleCount}");
        text.AppendLine($"最大工作集：{FormatBytes(report.MaximumWorkingSetBytes)}");
        text.AppendLine($"起始私有内存：{FormatBytes(report.StartingPrivateMemoryBytes)}");
        text.AppendLine($"当前私有内存：{FormatBytes(report.CurrentPrivateMemoryBytes)}");
        text.AppendLine($"最低私有内存：{FormatBytes(report.MinimumPrivateMemoryBytes)}");
        text.AppendLine($"最大私有内存：{FormatBytes(report.MaximumPrivateMemoryBytes)}");
        text.AppendLine($"最大托管堆：{FormatBytes(report.MaximumManagedHeapBytes)}");
        text.AppendLine($"最大线程数：{report.MaximumThreadCount}");
        text.AppendLine($"最大句柄数：{report.MaximumHandleCount}");
        text.AppendLine($"最大Dispatcher延迟：{report.MaximumDispatcherLagMilliseconds:F0} ms");
        text.AppendLine($"平均Dispatcher延迟：{report.AverageDispatcherLagMilliseconds:F0} ms");
        text.AppendLine($"最大主刷新耗时：{report.MaximumUiRefreshDurationMilliseconds:F0} ms");
        text.AppendLine($"30分钟私有内存变化：{FormatOptionalBytes(report.PrivateMemoryChange30MinutesBytes)}");
        text.AppendLine($"60分钟私有内存变化：{FormatOptionalBytes(report.PrivateMemoryChange60MinutesBytes)}");
        text.AppendLine($"当前走势图/手动录入/风险中心窗口：{report.OpenChartWindowCount}/{report.OpenManualEntryWindowCount}/{report.OpenRiskCenterWindowCount}");
        text.AppendLine($"已运行：{TimeSpan.FromSeconds(report.UptimeSeconds):c}");
        text.AppendLine($"采样写入错误：{report.SamplingWriteErrorCount}");
        text.AppendLine($"检测到异常退出证据：{(report.DetectedAbnormalExitEvidence ? "是" : "否")}");
        text.AppendLine("状态转换：");
        if (report.StatusTransitions.Length == 0)
        {
            text.AppendLine("- 无");
        }
        else
        {
            foreach (string transition in report.StatusTransitions)
            {
                text.AppendLine("- " + transition);
            }
        }

        return text.ToString();
    }

    private static async Task WriteDurablyAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static long MaxOrZero(IEnumerable<RuntimeHealthSnapshot> snapshots, Func<RuntimeHealthSnapshot, long> selector)
        => snapshots.Select(selector).DefaultIfEmpty(0).Max();

    private static int MaxOrZero(IEnumerable<RuntimeHealthSnapshot> snapshots, Func<RuntimeHealthSnapshot, int> selector)
        => snapshots.Select(selector).DefaultIfEmpty(0).Max();

    private static double MaxOrZero(IEnumerable<RuntimeHealthSnapshot> snapshots, Func<RuntimeHealthSnapshot, double> selector)
        => snapshots.Select(selector).DefaultIfEmpty(0).Max();

    private static string FormatDateTime(DateTimeOffset? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "无可用样本";

    private static string FormatOptionalBytes(long? value)
        => value.HasValue ? FormatBytes(value.Value) : "样本不足";

    private static string FormatBytes(long value)
        => $"{value / 1024d / 1024d:F2} MB";

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed export must not affect the running monitor.
        }
    }
}
