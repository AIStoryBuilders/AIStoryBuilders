namespace AIStoryBuildersV2.Platforms.Windows;

/// <summary>
/// Windows-specific shell configuration hook, invoked from <c>MauiProgram</c>.
/// Extend this as the app grows — e.g. set a default window size, or confirm on close
/// when there are unsaved edits.
/// </summary>
public static class WindowsShell
{
    public static void Configure(MauiAppBuilder builder)
    {
        // No-op for the scaffold. Add Windows lifecycle configuration here.
    }
}
