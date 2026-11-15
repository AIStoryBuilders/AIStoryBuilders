using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AIStoryBuildersV2.Shared.Services;

// A thin pass-through: callers decide how to handle a failed JS call (BL0016).
#pragma warning disable BL0016

/// <summary>Wraps <c>wwwroot/asb.js</c>. Scoped, because MAUI's <see cref="IJSRuntime"/> is scoped to the BlazorWebView.</summary>
public sealed class BrowserInterop(IJSRuntime js)
{
    /// <summary>Watches the window width. The handle has <c>isWide()</c> and <c>dispose()</c>; dispose it with the component.</summary>
    public ValueTask<IJSObjectReference> WatchWidthAsync<T>(DotNetObjectReference<T> target, int breakpoint) where T : class =>
        js.InvokeAsync<IJSObjectReference>("asb.watchWidth", target, breakpoint);

    public ValueTask RegisterShortcutsAsync<T>(DotNetObjectReference<T>? target) where T : class =>
        js.InvokeVoidAsync("asb.registerShortcuts", target);

    public ValueTask SetUnsavedAsync(bool unsaved) => js.InvokeVoidAsync("asb.setUnsaved", unsaved);

    public ValueTask FocusAsync(string selector) => js.InvokeVoidAsync("asb.focus", selector);

    public ValueTask ScrollToBottomAsync(string selector) => js.InvokeVoidAsync("asb.scrollToBottom", selector);

    public ValueTask ScrollIntoViewAsync(string selector, string block = "start") => js.InvokeVoidAsync("asb.scrollIntoView", selector, block);

    public ValueTask BindComposerAsync<T>(string id, DotNetObjectReference<T> target) where T : class =>
        js.InvokeVoidAsync("asb.bindComposer", id, target);

    public ValueTask ClearValueAsync(string id) => js.InvokeVoidAsync("asb.clearValue", id);

    /// <summary>Binds zoom, pan and drag to the graph SVG. Call <c>dispose()</c> on the handle to remove its listeners.</summary>
    public ValueTask<IJSObjectReference> BindGraphAsync<T>(ElementReference svg, DotNetObjectReference<T> target) where T : class =>
        js.InvokeAsync<IJSObjectReference>("asb.bindGraph", svg, target);

    /// <summary>Calls <c>dispose()</c> on a JS handle and releases it; a page that is going away is ignored.</summary>
    public static async ValueTask DisposeHandleAsync(IJSObjectReference? handle)
    {
        if (handle is null) return;
        try
        {
            await handle.InvokeVoidAsync("dispose");
            await handle.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
    }

    public ValueTask CenterGraphAsync(ElementReference svg, double x, double y) => js.InvokeVoidAsync("asb.centerGraph", svg, x, y);

    public ValueTask BindGraphPanelAsync<T>(ElementReference panel, DotNetObjectReference<T> target) where T : class =>
        js.InvokeVoidAsync("asb.bindGraphPanel", panel, target);

    public ValueTask FitGraphAsync(ElementReference svg) => js.InvokeVoidAsync("asb.fitGraph", svg);

    public ValueTask BindTreeDragAsync<T>(ElementReference root, DotNetObjectReference<T> target) where T : class =>
        js.InvokeVoidAsync("asb.bindTreeDrag", root, target);

    /// <summary>Downloads the stream as a file (browser host).</summary>
    public async ValueTask SaveAsFileAsync(string fileName, Stream content, string contentType)
    {
        using var streamRef = new DotNetStreamReference(content, leaveOpen: true);
        await js.InvokeVoidAsync("asb.saveAsFile", fileName, streamRef, contentType);
    }

    /// <summary>Opens a URL in a new browser tab (MAUI: the system browser).</summary>
    public ValueTask OpenUrlAsync(string url) => js.InvokeVoidAsync("open", url, "_blank");
}
