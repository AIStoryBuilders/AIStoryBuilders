using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Services;

namespace AIStoryBuildersV2.Web.Services;

/// <summary>Downloads through <c>asb.saveAsFile</c>. The browser has no cancel signal, so this always returns true (D19).</summary>
public sealed class BrowserFileExportService(BrowserInterop interop) : IFileExportService
{
    public async Task<bool> SaveAsync(string suggestedFileName, Stream content, string contentType, CancellationToken ct = default)
    {
        await interop.SaveAsFileAsync(suggestedFileName, content, contentType);
        return true;
    }
}
