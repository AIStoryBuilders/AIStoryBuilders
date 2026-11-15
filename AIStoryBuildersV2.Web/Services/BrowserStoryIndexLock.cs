using System.Runtime.ExceptionServices;
using AIStoryBuildersV2.Shared.Stories.Storage;
using Microsoft.JSInterop;

namespace AIStoryBuildersV2.Web.Services;

public sealed class BrowserStoryIndexLock(IJSRuntime js) : IStoryIndexLock
{
    public async Task RunAsync(Func<Task> action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var callback = new LockCallback(action, ct);
        using var target = DotNetObjectReference.Create(callback);
        await js.InvokeVoidAsync("asb.withLock", "asb2:stories", target);
        callback.Failure?.Throw();
    }

    /// <summary>
    /// Holds the action's exception on the .NET side. Thrown through the JS lock, it would come back as a JSException
    /// with .NET stack text in its message, and callers could no longer catch StorageQuotaExceededException.
    /// </summary>
    sealed class LockCallback(Func<Task> action, CancellationToken ct)
    {
        public ExceptionDispatchInfo? Failure { get; private set; }

        [JSInvokable]
        public async Task RunAsync()
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await action();
            }
            catch (Exception ex)
            {
                Failure = ExceptionDispatchInfo.Capture(ex);
            }
        }
    }
}
