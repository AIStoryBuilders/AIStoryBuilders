using AIStoryBuildersV2.Shared.Hosting;
using CommunityToolkit.Maui.Storage;

namespace AIStoryBuildersV2.Services;

/// <summary>Saves through the CommunityToolkit save dialog (D19).</summary>
public sealed class MauiFileExportService : IFileExportService
{
    public async Task<bool> SaveAsync(string suggestedFileName, Stream content, string contentType, CancellationToken ct = default)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() => FileSaver.Default.SaveAsync(suggestedFileName, content, ct));
        if (result.IsSuccessful) return true;
        // The toolkit reports a cancelled dialog as an OperationCanceledException or with no exception at all.
        if (result.Exception is null or OperationCanceledException) return false;
        throw result.Exception;
    }
}
