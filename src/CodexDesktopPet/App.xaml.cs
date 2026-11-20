using System.Windows;
using System.Windows.Threading;
using CodexDesktopPet.Models;

namespace CodexDesktopPet;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\CodexDesktopPet.SingleInstance";
    private const string ShowEventName = @"Local\CodexDesktopPet.Show";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var requestShow = e.Args.Any(argument =>
            string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase));
        if (requestShow)
        {
            var settings = AppSettings.Load();
            settings.PetEnabled = true;
            settings.PetHidden = false;
            settings.Save();
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            if (requestShow)
                _showEvent.Set();
            else
                System.Windows.MessageBox.Show("Codex 桌宠已经在运行。", "Codex 桌宠",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                    Dispatcher.BeginInvoke(async () => await mainWindow.ShowFromExternalRequestAsync());
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_ownsSingleInstanceMutex)
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
