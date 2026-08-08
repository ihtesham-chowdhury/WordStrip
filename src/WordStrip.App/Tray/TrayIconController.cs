using Forms = System.Windows.Forms;

namespace WordStrip.App.Tray;

/// <summary>
/// Owns the system tray icon and its context menu (Pause/Resume, Settings, Exit). Uses WinForms'
/// NotifyIcon — WPF has no equivalent of its own, and pulling in WinForms just for this is the standard,
/// well-trodden approach rather than hand-rolling Shell_NotifyIcon P/Invoke.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseMenuItem;

    public event EventHandler? PauseToggled;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public bool IsPaused { get; private set; }

    public TrayIconController()
    {
        _pauseMenuItem = new Forms.ToolStripMenuItem("Pause suggestions", null, (_, _) => TogglePause());
        var settingsItem = new Forms.ToolStripMenuItem("Settings...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        var exitItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.CreateIcon(),
            Text = "WordStrip — word suggestions",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        _pauseMenuItem.Text = IsPaused ? "Resume suggestions" : "Pause suggestions";
        PauseToggled?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
