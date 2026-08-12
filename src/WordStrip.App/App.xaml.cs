using System.IO;
using WordStrip.App.Coordination;
using WordStrip.App.Tray;
using WordStrip.App.UI;
using WordStrip.Core.Input;
using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.Neural;
using WordStrip.Core.Settings;
using WordStrip.Core.Suggestions;
using WordStrip.Core.Text;

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
    private KeyboardHookTextContextProvider? _hookContextProvider;
    private CompositeTextContextProvider? _contextProvider;
    private SuggestionController? _suggestionController;
    private SingleInstance? _singleInstance;
    private System.Windows.Threading.DispatcherTimer? _focusWatchdog;
    private System.Windows.Threading.DispatcherTimer? _learningSaveTimer;
    private PersonalVocabularyStore? _personalVocabulary;
    private PersonalLanguageModel? _personalLearning;
    private NeuralModelStore? _neuralModelStore;
    private WordStrip.Neural.OnnxNeuralReranker? _neuralReranker;
    private NeuralRerankCoordinator? _neuralCoordinator;

    /// <summary>
    /// Where an unhandled exception is recorded before the process dies.
    ///
    /// <para>Added after a crash that left nothing behind but a Windows Error Reporting entry saying
    /// "0xe0434352", which means only "a .NET exception" and identifies nothing. A tray application has no
    /// console and no window to show a dialog in, so without this a crash is genuinely undiagnosable — and
    /// the one that prompted it was in the settings window, where the user would simply see the whole app
    /// vanish.</para>
    /// </summary>
    private static readonly string CrashLogPath =
        Path.Combine(Path.GetTempPath(), "wordstrip_crash.log");

    private static void RecordCrash(string source, Exception exception)
    {
        try
        {
            File.AppendAllText(CrashLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing sensible to do while already crashing.
        }
    }

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) => RecordCrash("dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) RecordCrash("appdomain", ex);
        };

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

        // The user's own words and, if they have switched it on, what has been learned from their typing.
        // Both live under %LOCALAPPDATA%\WordStrip and are loaded before the engine so the very first
        // keystroke already benefits from them.
        _personalVocabulary = new PersonalVocabularyStore();
        _personalLearning = new PersonalLanguageModel();

        // Optional loose files beside the exe override the copies compiled into the assembly, for both the
        // dictionary and the n-gram model — so either can be swapped and tried without a rebuild.
        var dictionaryPath = Path.Combine(AppContext.BaseDirectory, "dict", "frequency_dictionary_en_82_765.txt");
        var nGramDirectory = Path.Combine(AppContext.BaseDirectory, "ngram");
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();

        var predictionEngine = await System.Threading.Tasks.Task.Run(() =>
        {
            _personalVocabulary.Load();
            _personalLearning.Load();

            return PredictionEngine.LoadDefault(
                dictionaryPath,
                assembly,
                nGramDirectory: nGramDirectory,
                personalVocabulary: _personalVocabulary,
                personalLearning: _personalLearning,
                // Supplied unconditionally; the setting is checked per keystroke so toggling it applies
                // immediately rather than at the next launch, like every other setting in the app.
                emoji: EmojiSuggester.Default);
        });

        // Loaded after the engine, on the same background thread, and only if the user has both downloaded a
        // model and switched the feature on. Three seconds of ONNX initialisation must never sit between
        // launching the app and being able to type.
        await LoadNeuralModelAsync();

        StartSuggestionEngine(predictionEngine);

        if (openSettingsOnLaunch)
            ShowSettingsWindow();
    }

    /// <summary>
    /// Loads the neural model if there is one and the user wants it.
    ///
    /// <para>Every branch out of here leaves the app fully working. No model, feature switched off, a
    /// corrupt file, an ONNX runtime that will not start on this CPU — all end with no coordinator, which
    /// the controller reads as "do not rerank" and the statistical stack carries on exactly as it does for
    /// everyone who never downloads anything.</para>
    /// </summary>
    private async System.Threading.Tasks.Task LoadNeuralModelAsync()
    {
        _neuralModelStore = new NeuralModelStore();

        if (!_settings.NeuralRerankingEnabled || !_neuralModelStore.IsDownloaded) return;

        var reranker = new WordStrip.Neural.OnnxNeuralReranker();
        var loaded = await System.Threading.Tasks.Task.Run(() => reranker.TryLoad(_neuralModelStore));

        if (!loaded)
        {
            reranker.Dispose();
            return;
        }

        _neuralReranker = reranker;
        _neuralCoordinator = new NeuralRerankCoordinator(reranker);
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

        // Phase 7: the controller is fed through a provider rather than the hook directly, and through the
        // composite even though there is currently only one provider to choose between. That is deliberate —
        // the fallback path is exercised in every build from now on, rather than being a wrapper added in a
        // hurry once a second provider exists and turns out to be unreliable. A TSF provider goes in front of
        // the hook here, and nothing else in this method changes when it does.
        _hookContextProvider = new KeyboardHookTextContextProvider(_typingSession);
        _contextProvider = new CompositeTextContextProvider(_hookContextProvider);

        _suggestionController = new SuggestionController(
            _contextProvider, predictionEngine, textInjector, _settings, postToMessageLoop,
            personalLearning: _personalLearning,
            neuralReranking: _neuralCoordinator)
        {
            IsPaused = _trayIcon?.IsPaused ?? false,
        };

        // Subscription order matters: BarInputRouter's Tab handling must run before TypingSession's, since
        // accepting a suggestion needs to read TypingSession.CurrentWord before TypingSession's own Tab
        // handling clears it. Router subscribes here; TypingSession only subscribes on the explicit Attach() below.
        var router = new BarInputRouter(_keyboardHook, _suggestionController, _barWindow);

        // Same reasoning on the mouse hook, and just as load-bearing. A click outside the bar dismisses it,
        // and TypingSession reacts to the same click by resetting its buffer — which republishes the idle
        // list. Dismissing first means that republish is suppressed; dismissing second makes the bar flash
        // back on for one frame before disappearing. (The hook already ignores clicks on our own windows,
        // so clicking a suggestion doesn't come through here.)
        _mouseHook.MouseButtonDown += (_, _) => _suggestionController.Dismiss();
        _typingSession.Attach();

        _suggestionController.SuggestionsChanged += (_, update) => _barWindow.ShowSuggestions(update.Suggestions, update.Caret);
        _barWindow.SuggestionClicked += (_, suggestion) => _suggestionController.AcceptSuggestion(suggestion);

        StartFocusWatchdog();
        StartLearningSaveTimer();

        _keyboardHook.Install();
        _mouseHook.Install();
    }

    /// <summary>
    /// Flushes anything newly learned to disk every half minute.
    ///
    /// <para>Batched rather than written per word: learning fires on every committed word, and rewriting the
    /// file that often would put disk I/O on the typing path for no benefit. The store tracks whether
    /// anything actually changed, so an idle machine does no work at all. Worst case a crash loses half a
    /// minute of learning, which costs the user nothing they will notice.</para>
    /// </summary>
    private void StartLearningSaveTimer()
    {
        _learningSaveTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(30),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => SavePersonalData(),
            Dispatcher);

        _learningSaveTimer.Start();
    }

    private void SavePersonalData()
    {
        try
        {
            _personalLearning?.SaveIfDirty();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed background save must never take the app down mid-typing. The data is still in memory
            // and the next tick will try again.
        }
    }

    /// <summary>
    /// Alt+Tab, a window closing, or focus moving by any means other than a click or a keystroke produces no
    /// event this app can see, so a persistent bar would sit over the new window until the user typed
    /// something. Polling focus is the pragmatic fix — one GetGUIThreadInfo call a second, and only while the
    /// bar is actually up. A DispatcherTimer rather than a threadpool one because the handler ends in a
    /// WPF window update, which has to happen on this thread anyway.
    /// </summary>
    private void StartFocusWatchdog()
    {
        _focusWatchdog = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => _suggestionController?.PollFocus(),
            Dispatcher);

        _focusWatchdog.Start();
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
            onAppearanceChanged: () => _barWindow?.ApplyAppearance(),
            personalVocabulary: _personalVocabulary,
            personalLearning: _personalLearning);

        // The learned-data summary is a snapshot, and typing carries on behind the settings window. Flushing
        // and refreshing on open means the figure shown is current rather than whatever it was last time.
        SavePersonalData();
        viewModel.RefreshLearnedDataLabel();

        if (_neuralModelStore is not null) viewModel.AttachNeuralModel(_neuralModelStore);

        _settingsWindow = new SettingsWindow(viewModel, _settings);
        _settingsWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _focusWatchdog?.Stop();
        _learningSaveTimer?.Stop();

        // Last chance to persist what was learned since the previous tick.
        SavePersonalData();

        _neuralCoordinator?.Dispose();
        _neuralReranker?.Dispose();
        _keyboardHook?.Dispose();
        _mouseHook?.Dispose();
        _typingSession?.Dispose();
        _suggestionController?.Dispose();

        // Disposed here rather than by the controller: neither the composite nor the providers it wraps are
        // owned by their consumer, so the composition root that created them takes them down.
        _contextProvider?.Dispose();
        _hookContextProvider?.Dispose();
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
