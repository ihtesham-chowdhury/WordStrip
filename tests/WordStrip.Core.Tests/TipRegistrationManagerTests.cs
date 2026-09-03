using WordStrip.Core.Platform;

namespace WordStrip.Core.Tests;

/// <summary>
/// Covers what can be tested without actually eleveting a UAC prompt on a CI machine — the path logic and
/// the "is a real registration to a real file" comparison, which is the part that turned a stale
/// developer-machine registration into a silent lie the first time this was tried by hand.
///
/// <para>Registering and unregistering for real is not exercised here on purpose: it requires an interactive
/// admin consent, which a test runner cannot supply and should not try to suppress — a test that disabled
/// UAC to pass would be testing a different, less honest code path than the one that ships.</para>
/// </summary>
public class TipRegistrationManagerTests
{
    [Fact]
    public void The_expected_dll_path_sits_next_to_the_running_executable()
    {
        // Not "some absolute path" - specifically beside the test binary, matching AppContext.BaseDirectory.
        // A registration manager that resolved this any other way would register the wrong copy of the DLL
        // on a real install, which is the exact bug this class exists to prevent.
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "WordStripTip.dll"),
            TipRegistrationManager.DllPath);
    }

    [Fact]
    public void Dll_presence_reflects_the_real_filesystem()
    {
        // No WordStripTip.dll ships next to the managed test binary - it is a native artefact built by a
        // separate toolchain (src/WordStrip.Tip/build.bat) and never a dependency of WordStrip.Core.Tests.
        Assert.False(TipRegistrationManager.DllPresent);
    }

    [Fact]
    public void With_no_such_file_the_install_is_never_reported_as_registered()
    {
        // Guards the specific failure this class was written to catch: a registry entry can exist and still
        // not describe this install. With the DLL absent entirely, "registered for this install" must be
        // false regardless of what happens to be sitting in the registry from an unrelated build.
        Assert.False(TipRegistrationManager.IsRegisteredForThisInstall());
    }
}
