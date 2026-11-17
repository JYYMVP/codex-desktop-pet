using System.Windows;
using System.Windows.Threading;

namespace CodexDesktopPet;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\CodexDesktopPet.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("Codex 桌宠已经在运行。", "Codex 桌宠",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(e.Exception.Message, "Codex 桌宠遇到错误",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
