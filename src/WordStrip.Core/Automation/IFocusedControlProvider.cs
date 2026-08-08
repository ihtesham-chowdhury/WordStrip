namespace WordStrip.Core.Automation;

/// <summary>
/// Seam over "what has keyboard focus right now". The real implementation reads live Win32 state, which a
/// test process cannot stand in for: <see cref="FocusedControlInspector"/> asks the OS for the foreground
/// window, so under a test runner it always reports "not a text field" and every behaviour that depends on
/// focus becomes untestable. Mirrors the reason <see cref="Input.ITextInjector"/> exists.
/// </summary>
public interface IFocusedControlProvider
{
    FocusedControlInfo GetFocusedControlInfo();
}

/// <summary>The production implementation: the live Win32 inspection in <see cref="FocusedControlInspector"/>.</summary>
public sealed class Win32FocusedControlProvider : IFocusedControlProvider
{
    public static Win32FocusedControlProvider Instance { get; } = new();

    public FocusedControlInfo GetFocusedControlInfo() => FocusedControlInspector.GetFocusedControlInfo();
}
