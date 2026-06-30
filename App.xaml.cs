using System.Windows;
using System.Windows.Threading;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Logging;

namespace CrossETF.Terminal.UiShell.Reference;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        base.OnStartup(e);
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
