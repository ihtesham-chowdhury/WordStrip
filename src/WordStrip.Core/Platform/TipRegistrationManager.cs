using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace WordStrip.Core.Platform;

/// <summary>Outcome of a register/unregister attempt.</summary>
public enum TipRegistrationResult
{
    Success,

    /// <summary>The user declined the UAC prompt. Not a failure — this is the setting doing exactly what it promises.</summary>
    Cancelled,

    /// <summary><c>regsvr32</c> ran but reported failure — most often the DLL is missing from the install.</summary>
    Failed,
}

/// <summary>
/// Registers or unregisters WordStrip's Text Services Framework text service — the component that lets
/// suggestions reach Chrome, Edge and Word instead of only classic Win32 controls.
///
/// <para><b>Why this exists, and why it elevates a child process rather than the app.</b> Every text service
/// already on a Windows 11 machine is registered under <c>HKEY_LOCAL_MACHINE</c>, which needs administrator
/// rights — confirmed empirically against the 21 services already on a stock install (see
/// <c>CLAUDE_PROJECT_CONTEXT.md</c> §14). WordStrip's own installer stays deliberately
/// <c>PrivilegesRequired=lowest</c>, so registration is this: a small, explicit, user-initiated action from
/// Settings, elevating only <c>regsvr32.exe</c> for the seconds it takes to run — never the tray application
/// itself. <b>A keyboard hook installed by an elevated process cannot see input going to non-elevated
/// windows</b>, so an elevated WordStrip would silently stop suggesting everywhere ordinary. That constraint
/// is the reason this class shells out to a separate process instead of calling the registration APIs
/// in-process.</para>
///
/// <para><b>Registration state is read from the registry on every call, never cached.</b> The autostart
/// checkbox drifting from the real Run-key state (§12 item 13) is exactly the failure shape a cached flag in
/// <c>settings.json</c> would repeat here — and the cost of getting it wrong is worse: a user believing
/// browser support is on when the registry disagrees.</para>
/// </summary>
public static class TipRegistrationManager
{
    /// <summary>
    /// Matches <c>CLSID_WordStripTextService</c> in <c>src/WordStrip.Tip/Guids.h</c> exactly. Generated once
    /// and never regenerated — see that file's own remarks on why.
    /// </summary>
    private const string Clsid = "{85418D7E-C008-4E1B-981B-0DC9586800CC}";

    private const string DllFileName = "WordStripTip.dll";

    /// <summary>
    /// The DLL this installation would register — always next to the running executable, never a path from
    /// a developer's machine. Deliberately not <c>internal</c>-only: the settings UI needs it to explain
    /// what "Enable" is about to do before the UAC prompt appears.
    /// </summary>
    public static string DllPath => Path.Combine(AppContext.BaseDirectory, DllFileName);

    public static bool DllPresent => File.Exists(DllPath);

    /// <summary>
    /// Whether the registered text service is <em>this installation's</em> copy of the DLL — not merely
    /// whether some registration exists.
    ///
    /// <para>The distinction matters in exactly the situation that motivated writing this class: a developer
    /// machine can carry a registration pointing at a source-tree build path
    /// (<c>...\WordStrip.Tip\bin\x64\Release\WordStripTip.dll</c>) that will not exist on any other machine.
    /// Reporting that as "enabled" would be true of the registry and false of reality. Comparing paths means
    /// clicking Enable here always makes the registration correct, whatever it pointed at before —
    /// <c>DllRegisterServer</c> registers whichever physical file <c>regsvr32</c> was pointed at, so
    /// re-running it against the installed copy repairs a stale registration as a side effect.</para>
    /// </summary>
    public static bool IsRegisteredForThisInstall()
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{Clsid}\InprocServer32", writable: false);
        var registeredPath = key?.GetValue(null) as string;

        return registeredPath is not null
            && string.Equals(
                Path.GetFullPath(registeredPath),
                Path.GetFullPath(DllPath),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Elevates <c>regsvr32 &lt;DllPath&gt;</c>. Returns without throwing on every outcome, including the
    /// user cancelling the UAC prompt — a declined elevation request is not a bug to report, it is the
    /// setting working as designed.
    /// </summary>
    public static TipRegistrationResult Register() => RunElevatedRegsvr32(unregister: false);

    /// <summary>Elevates <c>regsvr32 /u &lt;DllPath&gt;</c>. Safe to call even if nothing is registered.</summary>
    public static TipRegistrationResult Unregister() => RunElevatedRegsvr32(unregister: true);

    private static TipRegistrationResult RunElevatedRegsvr32(bool unregister)
    {
        if (!DllPresent) return TipRegistrationResult.Failed;

        var arguments = unregister
            ? $"/s /u \"{DllPath}\""
            : $"/s \"{DllPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = "regsvr32.exe",
            Arguments = arguments,

            // UseShellExecute + Verb "runas" is what produces the UAC prompt for regsvr32.exe alone; this
            // process (the tray app) never itself elevates.
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return TipRegistrationResult.Failed;

            process.WaitForExit();
            return process.ExitCode == 0 ? TipRegistrationResult.Success : TipRegistrationResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user clicked "No" on the UAC prompt.
            return TipRegistrationResult.Cancelled;
        }
    }
}
