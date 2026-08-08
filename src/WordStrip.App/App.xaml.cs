using System.IO;
using WordStrip.App.Coordination;
using WordStrip.App.Tray;
using WordStrip.App.UI;
using WordStrip.Core.Input;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;
using WordStrip.Core.Suggestions;

namespace WordStrip.App;

// UseWindowsForms (for the tray icon's NotifyIcon) adds an implicit global "using System.Windows.Forms;"
// which collides with "System.Windows" on several type names (Application, MessageBox, ...) — qualify
// explicitly here rather than relying on the bare name.
public partial class App : System.Windows.Application
{
    private readonly AppSettingsStore _settingsStore = new();
    private AppSettings _settings = new();

    private TrayIconController? _trayIcon;
    private SuggestionBarWindow? _barWindow;
    private SettingsWindow? _settingsWindow;

    private LowLevelKeyboardHook? _keyboardHook;
    private LowLevelMouseHook? _mouseHook;
    private TypingSession? _typingSession;
    private SuggestionController? _suggestionController;
    private SingleInstance? _singleInstance;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var openSettingsOnLaunch = e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            // Already running. Surface the existing copy's settings rather than starting a second keyboard
            // hook that would fight the first one over every keystroke.
            SingleInstance.SignalExistingInstanceToShowSettings();
            Shutdown();
            return;
        }

        _singleInstance.ListenForShowSettings(() => Dispatcher.Invoke(ShowSettingsWindow));

        _settings = _settingsStore.Load();

        // Tray icon appears immediately so the app doesn't look hung while the ~5s dictionary index builds.
        _trayIcon = new TrayIconController();
        _trayIcon.ExitRequested += (_, _) => Shutdown();
        _trayIcon.SettingsRequested += (_, _) => ShowSettingsWindow();
        _trayIcon.PauseToggled += (_, _) =>
        {
            if (_suggestionController is not null)
                _suggestionController.IsPaused = _trayIcon.IsPaused;
        };

        // An optional dict\ file beside the exe overrides the dictionary compiled into the assembly.
        var dictionaryPath = Path.Combine(AppContext.BaseDirectory, "dict", "frequency_dictionary_en_82_765.txt");
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var predictionEngine = await System.Threading.Tasks.Task.Run(
            () => PredictionEngine.LoadDefault(dictionaryPath, assembly));

        StartSuggestionEngine(predictionEngine);

        if (openSettingsOnLaunch)
            ShowSettingsWindow();
    }

    private void StartSuggestionEngine(PredictionEngine predictionEngine)
    {
        _keyboardHook = new LowLevelKeyboardHook();
        _mouseHook = new LowLevelMouseHook();

        _barWindow = new SuggestionBarWindow(_settings);

        var textInjector = new Win32TextInjector();
        _typingSession = new TypingSession(_keyboardHook, _mouseHook); // not yet subscribed to the hook — see Attach() below
        // Text replacements must run after the keyboard hook callback returns — see SuggestionController's
        // postToMessageLoop parameter. The hook runs on this UI thread, so queuing to its dispatcher is
        // exactly "once the current keystroke is done being processed."
        var postToMessageLoop = new Action<Action>(action =>
            _barWindow.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, action));

        _suggestionController = new SuggestionController(_typingSession, predictionEngine, textInjector, _settings, postToMessageLoop)
        {
            IsPaused = _trayIcon?.IsPaused ?? false,
        };

        // Subscription order matters: BarInputRouter's Tab handling must run before TypingSession's, since
        // accepting a suggestion needs to read TypingSession.CurrentWord before TypingSession's own Tab
        // handling clears it. Router subscribes here; TypingSession only subscribes on the explicit Attach() below.
        var router = new BarInputRouter(_keyboardHook, _suggestionController, _barWindow);
        _typingSession.Attach();

        _suggestionController.SuggestionsChanged += (_, update) => _barWindow.ShowSuggestions(update.Suggestions, update.Caret);
        _barWindow.SuggestionClicked += (_, suggestion) => _suggestionController.AcceptSuggestion(suggestion);

        _keyboardHook.Install();
        _mouseHook.Install();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        if (_barWindow is null) return; // engine still loading; the tray icon exists but there's nothing to preview yet

        var executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var viewModel = new SettingsViewModel(_settings, _settingsStore, executablePath,
            onAppearanceChanged: () => _barWindow?.ApplyAppearance());
        _settingsWindow = new SettingsWindow(viewModel, _settings);
        _settingsWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _keyboardHook?.Dispose();
        _mouseHook?.Dispose();
        _typingSession?.Dispose();
        _suggestionController?.Dispose();
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
