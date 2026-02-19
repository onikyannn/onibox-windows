using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Onibox.Services;

namespace Onibox;

public partial class App : Application
{
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
        SetupLogging();
        LogInfo("Startup.");
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogError("Unhandled exception (App).", e.Exception);
    }

    private static void SetupLogging()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
        }
        catch
        {
            // ignore logging setup errors
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LogError("Unhandled exception (AppDomain).", ex);
        };

    }

    private static void LogInfo(string message) => AppendLog("INFO", message, null);

    private static void LogError(string message, Exception? ex) => AppendLog("ERROR", message, ex);

    private static void AppendLog(string level, string message, Exception? ex)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"));
            builder.Append(' ');
            builder.Append('[').Append(level).Append(']').Append(' ');
            builder.Append(message);
            if (ex is not null)
            {
                builder.Append(" | ");
                builder.Append(ex.GetType().Name);
                builder.Append(": ");
                builder.Append(ex.Message);
                builder.AppendLine();
                builder.Append(ex);
            }
            builder.AppendLine();
            File.AppendAllText(AppPaths.AppLogPath, builder.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // ignore logging failures
        }
    }
}
