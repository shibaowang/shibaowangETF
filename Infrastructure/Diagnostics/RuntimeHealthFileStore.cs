using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Diagnostics;

public sealed partial class RuntimeHealthFileStore
{
    public const long DefaultMaximumFileBytes = 20L * 1024 * 1024;
    public const int DefaultRetentionDays = 7;

    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly long _maximumFileBytes;
    private readonly int _retentionDays;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _errorGate = new();
    private string? _lastErrorSignature;
    private DateTimeOffset _lastErrorAt;

    public RuntimeHealthFileStore(
        string healthDirectory,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int retentionDays = DefaultRetentionDays)
    {
        if (maximumFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        if (retentionDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        }

        HealthDirectory = Path.GetFullPath(healthDirectory ?? throw new ArgumentNullException(nameof(healthDirectory)));
        ReportsDirectory = Path.Combine(HealthDirectory, "reports");
        _maximumFileBytes = maximumFileBytes;
        _retentionDays = retentionDays;
    }

    public string HealthDirectory { get; }

    public string ReportsDirectory { get; }

    public async Task<RuntimeHealthFileWriteResult> AppendSnapshotAsync(
        RuntimeHealthSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(HealthDirectory);
            byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions) + "\n");
            string path = ResolveWritableFile(snapshot.Timestamp.LocalDateTime.Date, snapshot.ProcessId, line.Length);
            using (var stream = new FileStream(
                       path,
                       FileMode.Append,
                       FileAccess.Write,
                       FileShare.Read,
                       bufferSize: 4096,
                       FileOptions.Asynchronous))
            {
                await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            CleanupExpiredControlledFiles(snapshot.Timestamp.LocalDateTime.Date);
            return new RuntimeHealthFileWriteResult(true, path, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryWriteError("JSONL_APPEND", ex.Message, snapshot.Timestamp);
            return new RuntimeHealthFileWriteResult(false, null, ex.Message);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<RuntimeHealthSnapshot>> ReadSnapshotsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(HealthDirectory))
        {
            return Array.Empty<RuntimeHealthSnapshot>();
        }

        var snapshots = new List<RuntimeHealthSnapshot>();
        foreach (string path in Directory
                     .EnumerateFiles(HealthDirectory, "runtime-health-*.jsonl", SearchOption.TopDirectoryOnly)
                     .Where(path => TryParseControlledLogName(Path.GetFileName(path), out _, out _, out _))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        RuntimeHealthSnapshot? snapshot = JsonSerializer.Deserialize<RuntimeHealthSnapshot>(line, JsonOptions);
                        if (snapshot is not null && snapshot.Timestamp >= since)
                        {
                            snapshots.Add(snapshot);
                        }
                    }
                    catch (JsonException)
                    {
                        // A truncated line from an abnormal process exit is ignored without hiding valid lines.
                    }
                }
            }
            catch (IOException ex)
            {
                TryWriteError("JSONL_READ", ex.Message, DateTimeOffset.Now);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryWriteError("JSONL_READ", ex.Message, DateTimeOffset.Now);
            }
        }

        return snapshots.OrderBy(snapshot => snapshot.Timestamp).ToArray();
    }

    public void TryWriteError(string category, string message, DateTimeOffset timestamp)
    {
        string safeCategory = SanitizeSingleLine(category);
        string safeMessage = SanitizeSingleLine(message);
        string signature = safeCategory + "|" + safeMessage;
        lock (_errorGate)
        {
            if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal)
                && timestamp - _lastErrorAt < TimeSpan.FromMinutes(5))
            {
                return;
            }

            _lastErrorSignature = signature;
            _lastErrorAt = timestamp;
        }

        try
        {
            Directory.CreateDirectory(HealthDirectory);
            string path = Path.Combine(
                HealthDirectory,
                $"runtime-health-errors-{timestamp.LocalDateTime:yyyyMMdd}-pid{Environment.ProcessId}.log");
            string line = $"{timestamp:O}\t{safeCategory}\t{safeMessage}{Environment.NewLine}";
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush();
        }
        catch
        {
            // Health monitoring errors must never terminate or disrupt the application.
        }
    }

    internal string ResolveWritableFile(DateTime date, int processId, int nextLineBytes)
    {
        Directory.CreateDirectory(HealthDirectory);
        var candidates = Directory
            .EnumerateFiles(HealthDirectory, $"runtime-health-{date:yyyyMMdd}-pid{processId}*.jsonl")
            .Select(path => new
            {
                Path = path,
                Parsed = TryParseControlledLogName(Path.GetFileName(path), out DateTime parsedDate, out int parsedPid, out int part),
                Date = parsedDate,
                Pid = parsedPid,
                Part = part
            })
            .Where(item => item.Parsed && item.Date == date && item.Pid == processId)
            .OrderBy(item => item.Part)
            .ToArray();
        if (candidates.Length == 0)
        {
            return BuildLogPath(date, processId, 1);
        }

        var latest = candidates[^1];
        long length = new FileInfo(latest.Path).Length;
        return length > 0 && length + nextLineBytes > _maximumFileBytes
            ? BuildLogPath(date, processId, latest.Part + 1)
            : latest.Path;
    }

    internal void CleanupExpiredControlledFiles(DateTime currentDate)
    {
        if (!Directory.Exists(HealthDirectory))
        {
            return;
        }

        DateTime oldestKeptDate = currentDate.Date.AddDays(-(_retentionDays - 1));
        string[] paths;
        try
        {
            paths = Directory
                .EnumerateFiles(HealthDirectory, "runtime-health-*", SearchOption.TopDirectoryOnly)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryWriteError("RETENTION_ENUMERATE", ex.Message, new DateTimeOffset(currentDate));
            return;
        }

        foreach (string path in paths)
        {
            string fileName = Path.GetFileName(path);
            DateTime fileDate;
            bool controlled = TryParseControlledLogName(fileName, out fileDate, out _, out _)
                              || TryParseControlledErrorName(fileName, out fileDate, out _);
            if (!controlled || fileDate >= oldestKeptDate)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryWriteError("RETENTION", ex.Message, new DateTimeOffset(currentDate));
            }
        }
    }

    internal static bool TryParseControlledLogName(
        string fileName,
        out DateTime date,
        out int processId,
        out int part)
    {
        date = default;
        processId = 0;
        part = 1;
        Match match = ControlledLogRegex().Match(fileName);
        if (!match.Success
            || !DateTime.TryParseExact(
                match.Groups["date"].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date)
            || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out processId))
        {
            return false;
        }

        if (match.Groups["part"].Success
            && !int.TryParse(match.Groups["part"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out part))
        {
            return false;
        }

        return processId > 0 && part > 0;
    }

    private string BuildLogPath(DateTime date, int processId, int part)
    {
        string suffix = part <= 1 ? string.Empty : $"-part{part}";
        return Path.Combine(HealthDirectory, $"runtime-health-{date:yyyyMMdd}-pid{processId}{suffix}.jsonl");
    }

    private static bool TryParseControlledErrorName(string fileName, out DateTime date, out int processId)
    {
        date = default;
        processId = 0;
        Match match = ControlledErrorRegex().Match(fileName);
        return match.Success
               && DateTime.TryParseExact(
                   match.Groups["date"].Value,
                   "yyyyMMdd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out date)
               && int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out processId)
               && processId > 0;
    }

    private static string SanitizeSingleLine(string? value)
        => (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [GeneratedRegex(
        "^runtime-health-(?<date>\\d{8})-pid(?<pid>\\d+)(?:-part(?<part>[2-9]\\d*))?\\.jsonl$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ControlledLogRegex();

    [GeneratedRegex(
        "^runtime-health-errors-(?<date>\\d{8})-pid(?<pid>\\d+)\\.log$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ControlledErrorRegex();
}
