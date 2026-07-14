using System.Windows;
using System.Windows.Threading;
using System.Reflection;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Logging;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Persistence;

namespace CrossETF.Terminal.UiShell.Reference;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private DatabaseStartupCoordinator? _databaseStartupCoordinator;
    private DatabaseStartupCompletionResult? _databaseStartupCompletion;
    private bool _databaseStartupNotificationsShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        DatabaseStartupPreflightResult preflight;
        try
        {
            string applicationVersion = ResolveApplicationVersion();
            var backupService = DatabaseBackupService.CreateDefault(applicationVersion);
            _databaseStartupCoordinator = new DatabaseStartupCoordinator(backupService);
            preflight = _databaseStartupCoordinator.RunPreInitialize();
        }
        catch (Exception ex)
        {
            string message = "数据库启动前安全检查失败，程序未初始化数据库：" + ex.Message;
            DatabaseRestoreBootstrap.WriteStartupFileLog("CRITICAL", message, ex);
            MessageBox.Show(message, "数据库安全保护", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        if (!preflight.CanContinue)
        {
            MessageBox.Show(
                preflight.Message,
                "数据库安全保护",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        base.OnStartup(e);
    }

    internal void CompleteDatabaseStartup(LocalDataRepository repository)
    {
        if (_databaseStartupCoordinator is null)
        {
            return;
        }

        _databaseStartupCompletion = _databaseStartupCoordinator.CompleteAfterDatabaseInitialization(repository);
    }

    internal void ShowDatabaseStartupNotifications(Window owner)
    {
        if (_databaseStartupNotificationsShown || _databaseStartupCoordinator is null)
        {
            return;
        }

        _databaseStartupNotificationsShown = true;
        DatabaseRestoreResult? restoreResult = _databaseStartupCoordinator.PendingRestoreResult;
        if (restoreResult is not null)
        {
            MessageBox.Show(
                owner,
                restoreResult.Message,
                restoreResult.Success ? "数据库恢复完成" : "数据库恢复结果",
                MessageBoxButton.OK,
                restoreResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            _databaseStartupCoordinator.AcknowledgeRestoreResult();
        }

        if (!string.IsNullOrWhiteSpace(_databaseStartupCompletion?.WarningMessage))
        {
            MessageBox.Show(
                owner,
                _databaseStartupCompletion.WarningMessage,
                "数据库自动备份提醒",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string ResolveApplicationVersion()
    {
        Assembly assembly = typeof(App).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return DatabaseBackupService.NormalizeVersion(
            informationalVersion ?? assembly.GetName().Version?.ToString(3));
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppExceptionLogger.WriteCrash(e.Exception, AppOperationContext.Current, false);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppExceptionLogger.WriteCrash(exception, AppOperationContext.Current, e.IsTerminating);
        }
        else
        {
            AppExceptionLogger.WriteRuntime("ERROR", AppOperationContext.Current, "Unhandled non-Exception object: " + e.ExceptionObject);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppExceptionLogger.WriteCrash(e.Exception, AppOperationContext.Current, false);
        e.SetObserved();
    }
}
