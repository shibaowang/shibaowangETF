using System.IO;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Logging;

public static class AppExceptionLogger
{
    private static readonly object FileLock = new();

    public static string LogDirectory
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LocalDatabase.AppFolderName,
            "logs");

    public static string CurrentCrashLogPath
        => Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd}.log");

    public static void WriteCrash(Exception exception, string context, bool isTerminating)
    {
        string text = BuildEntry("CRASH", exception, context, isTerminating);
        WriteFile(CurrentCrashLogPath, text);
        TryWriteRuntimeLog("ERROR", "GlobalException", context, text);
    }

    public static void WriteRuntime(string level, string context, string message, Exception? exception = null)
    {
        string text = exception is null
            ? BuildMessageEntry(level, context, message)
            : BuildEntry(level, exception, context, false, message);
        WriteFile(Path.Combine(LogDirectory, $"runtime-{DateTime.Now:yyyyMMdd}.log"), text);
        TryWriteRuntimeLog(level, context, message, exception?.ToString());
    }

    private static string BuildEntry(string level, Exception exception, string context, bool isTerminating, string? message = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("============================================================");
        builder.AppendLine($"time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"level: {level}");
        builder.AppendLine($"context: {context}");
        if (!string.IsNullOrWhiteSpace(message))
        {
            builder.AppendLine("message:");
            builder.AppendLine(message);
        }

        builder.AppendLine($"terminating: {isTerminating}");
        AppendException(builder, exception, 0);
        builder.AppendLine();
        return builder.ToString();
    }

    private static string BuildMessageEntry(string level, string context, string message)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "============================================================",
            $"time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}",
            $"level: {level}",
            $"context: {context}",
            $"message: {message}",
            string.Empty
        });
    }

    private static void AppendException(StringBuilder builder, Exception exception, int depth)
    {
        string prefix = depth == 0 ? "exception" : $"inner[{depth}]";
        builder.AppendLine($"{prefix}.type: {exception.GetType().FullName}");
        builder.AppendLine($"{prefix}.message: {exception.Message}");
        builder.AppendLine($"{prefix}.stack:");
        builder.AppendLine(exception.StackTrace ?? "<no stack trace>");
        if (exception.InnerException is not null)
        {
            AppendException(builder, exception.InnerException, depth + 1);
        }
    }

    private static void WriteFile(string path, string text)
    {
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, text, Encoding.UTF8);
            }
        }
        catch
        {
            // File logging is best effort. Never let logging crash the app.
        }
    }

    private static void TryWriteRuntimeLog(string level, string module, string message, string? detail)
    {
        try
        {
            var repository = new LocalDataRepository(new LocalDatabase());
            repository.WriteRuntimeLog(level, module, message, detail);
        }
        catch
        {
            // Database logging is best effort after a crash or startup failure.
        }
    }
}
