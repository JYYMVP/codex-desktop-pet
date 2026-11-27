using System.Windows;
using System.Windows.Threading;
using CodexDesktopPet.Models;

namespace CodexDesktopPet;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\CodexDesktopPet.SingleInstance";
    private const string ShowEventName = @"Local\CodexDesktopPet.Show";
    private const string SettingsEventName = @"Local\CodexDesktopPet.Settings";
    private const string LibraryEventName = @"Local\CodexDesktopPet.Library";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private EventWaitHandle? _settingsEvent;
    private RegisteredWaitHandle? _settingsWait;
    private EventWaitHandle? _libraryEvent;
    private RegisteredWaitHandle? _libraryWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var requestShow = e.Args.Any(argument =>
            string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase));
        var requestSettings = e.Args.Any(argument =>
            string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase));
        var requestLibrary = e.Args.Any(argument =>
            string.Equals(argument, "--library", StringComparison.OrdinalIgnoreCase));
        if (requestShow)
        {
            var settings = AppSettings.Load();
            settings.PetEnabled = true;
            settings.PetHidden = false;
            settings.Save();
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsEventName);
        _libraryEvent = new EventWaitHandle(false, EventResetMode.AutoReset, LibraryEventName);
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            if (requestShow)
                _showEvent.Set();
            if (requestSettings)
                _settingsEvent.Set();
            if (requestLibrary)
                _libraryEvent.Set();
            if (!requestShow && !requestSettings && !requestLibrary)
                System.Windows.MessageBox.Show("Codex 桌宠已经在运行。", "Codex 桌宠",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        var mainWindow = new MainWindow();
        if (string.Equals(Environment.GetEnvironmentVariable("CODEX_DESKTOP_PET_PREVIEW"), "1",
                StringComparison.Ordinal))
        {
            // Local visual QA mode: expose the otherwise taskbar-less transparent window
            // to Windows accessibility and capture tools without changing normal behavior.
            mainWindow.ShowInTaskbar = true;
            mainWindow.ShowActivated = true;
        }
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
        _settingsWait = RegisterExternalAction(
            _settingsEvent, mainWindow.OpenSettingsFromExternalRequest);
        _libraryWait = RegisterExternalAction(
            _libraryEvent, mainWindow.OpenPetLibraryFromExternalRequest);
        if (requestSettings)
            Dispatcher.BeginInvoke(mainWindow.OpenSettingsFromExternalRequest);
        if (requestLibrary)
            Dispatcher.BeginInvoke(mainWindow.OpenPetLibraryFromExternalRequest);
    }

    private RegisteredWaitHandle RegisterExternalAction(
        EventWaitHandle waitHandle,
        Action action) => ThreadPool.RegisterWaitForSingleObject(
        waitHandle,
        (_, timedOut) =>
        {
            if (!timedOut)
                Dispatcher.BeginInvoke(action);
        },
        null,
        Timeout.Infinite,
        executeOnlyOnce: false);

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _settingsWait?.Unregister(null);
        _libraryWait?.Unregister(null);
        _showEvent?.Dispose();
        _settingsEvent?.Dispose();
        _libraryEvent?.Dispose();
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
