using System.Diagnostics;
using AIStoryBuildersV2.Shared.Hosting;

namespace AIStoryBuildersV2.Services;

/// <summary>Stories live in V1's folder, Documents\AIStoryBuilders (D4).</summary>
public sealed class MauiHostCapabilities(string rootPath) : IHostCapabilities
{
    public bool CanToggleTheme => false;
    public bool CanOpenFolder => OperatingSystem.IsWindows();
    public bool IsBrowser => false;
    public bool HasBackup => false;
    public bool DeletesAreRecoverable => OperatingSystem.IsWindows();
    public string RootDisplay => @"Documents\AIStoryBuilders";
    public string RootPath => rootPath;

    // R9: the Web Speech API is not verified in WebView2 yet (T12b).
    public bool SupportsSpeechToText => false;

    public void OpenRootFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{rootPath}\"") { UseShellExecute = true });
    }
}
