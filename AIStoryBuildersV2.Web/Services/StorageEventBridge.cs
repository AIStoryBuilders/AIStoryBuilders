using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;
using Microsoft.JSInterop;

namespace AIStoryBuildersV2.Web.Services;

/// <summary>
/// Forwards browser events to .NET (plan §5.5): <c>storage</c> events for asb2:* keys written by other tabs, and the tab
/// going to the background, which flushes dirty stories (best effort: the browser does not wait for .NET).
/// Modeled on KGB's StorageEventBridge.
/// </summary>
public sealed class StorageEventBridge(IJSRuntime js, ZipStoryPersistence persistence) : IAsyncDisposable
{
    DotNetObjectReference<StorageEventBridge>? _self;

    /// <summary>Raised for every asb2:* key another tab changed (null when the whole storage was cleared).</summary>
    public event Action<string?>? KeyChanged;

    public async Task StartAsync()
    {
        _self = DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("asb.registerStorageListener", _self, StoryStorageConstants.KeyPrefix);
        await js.InvokeVoidAsync("asb.registerVisibilityListener", _self);
    }

    [JSInvokable]
    public async Task OnStorageChanged(string? key)
    {
        KeyChanged?.Invoke(key);
        await persistence.OnExternalChangeAsync(key);
    }

    [JSInvokable]
    public async Task OnHidden()
    {
        try
        {
            await persistence.FlushAsync();
        }
        catch (Exception)
        {
            // The workspace shows the save-failed state from the persistence event; nothing else to do here.
        }
    }

    public ValueTask DisposeAsync()
    {
        _self?.Dispose();
        return ValueTask.CompletedTask;
    }
}
