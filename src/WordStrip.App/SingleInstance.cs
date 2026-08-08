using System.Threading;

namespace WordStrip.App;

/// <summary>
/// Ensures only one copy of WordStrip runs at a time, and gives a second launch a way to reach the first.
///
/// <para>This matters more than usual here: the app installs a system-wide keyboard hook and injects
/// keystrokes, so two running copies would each see every keypress and each try to rewrite the same word.
/// Since the app lives in the tray with no window, a user who forgets it's running and launches it again
/// would have no obvious way to notice.</para>
///
/// <para>The second launch signals the first to open its settings window and then exits, which is also how
/// the Start Menu "Settings" shortcut works.</para>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\WordStrip.SingleInstance";
    private const string ShowSettingsEventName = @"Local\WordStrip.ShowSettings";

    private readonly Mutex _mutex;
    private EventWaitHandle? _showSettingsSignal;
    private RegisteredWaitHandle? _registration;

    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>Tells the already-running copy to show its settings window. Called by the second instance before it exits.</summary>
    public static void SignalExistingInstanceToShowSettings()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var handle))
            {
                using (handle) handle.Set();
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The other instance exited between our check and this call; nothing to signal.
        }
    }

    /// <summary>Starts listening for "show settings" requests from later launches. First instance only.</summary>
    public void ListenForShowSettings(Action onShowSettings)
    {
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showSettingsSignal,
            (_, _) => onShowSettings(),
            state: null,
            timeout: Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showSettingsSignal?.Dispose();

        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* never acquired; nothing to release */ }
        }

        _mutex.Dispose();
    }
}
