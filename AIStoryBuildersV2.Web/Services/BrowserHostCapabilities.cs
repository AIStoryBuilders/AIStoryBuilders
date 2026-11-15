using AIStoryBuildersV2.Shared.Hosting;

namespace AIStoryBuildersV2.Web.Services;

/// <summary>The stories root is in the .NET in-memory file system; the durable copy is in localStorage (D3, D5).</summary>
public sealed class BrowserHostCapabilities(string rootPath) : IHostCapabilities
{
    public bool CanToggleTheme => false;
    public bool CanOpenFolder => false;
    public bool IsBrowser => true;
    public bool HasBackup => false;
    public bool DeletesAreRecoverable => false;
    public string RootDisplay => "Browser storage";
    public string RootPath => rootPath;
    public void OpenRootFolder() { }
    public bool SupportsSpeechToText => true;
}
