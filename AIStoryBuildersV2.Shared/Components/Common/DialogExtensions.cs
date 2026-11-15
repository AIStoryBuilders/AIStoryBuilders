using AIStoryBuildersV2.Shared.Knowledge;
using Radzen;
using static AIStoryBuildersV2.Shared.Components.Common.ChoiceDialog;

namespace AIStoryBuildersV2.Shared.Components.Common;

public enum UnsavedChoice { Save, Discard, Cancel }
public enum ConflictChoice { Reload, Overwrite, Cancel }

/// <summary>The app's standard dialogs (§ Dialogs in Docs/design/README.md).</summary>
public static class DialogExtensions
{
    static DialogOptions Options(string width = "440px") => new() { Width = width, CloseDialogOnEsc = true, ShowClose = true, Draggable = false };

    public static async Task<string?> AskNameAsync(this DialogService dialogs, string title, string label, string okText,
        bool isPage, IReadOnlyCollection<string> siblings, string? initial = null, string? unchanged = null, string? help = null)
    {
        var result = await dialogs.OpenAsync<NameDialog>(title, new Dictionary<string, object?>
        {
            [nameof(NameDialog.Label)] = label,
            [nameof(NameDialog.OkText)] = okText,
            [nameof(NameDialog.IsPage)] = isPage,
            [nameof(NameDialog.Siblings)] = siblings,
            [nameof(NameDialog.Initial)] = initial,
            [nameof(NameDialog.Unchanged)] = unchanged,
            [nameof(NameDialog.Help)] = help,
            [nameof(NameDialog.MaxLength)] = 120,
        }, Options());
        return result as string;
    }

    public static async Task<string?> ChooseAsync(this DialogService dialogs, string title, string message, IReadOnlyList<Choice> choices,
        string? icon = null, string tone = "ne", IReadOnlyList<string>? items = null, string? confirmText = null, string width = "460px")
    {
        var result = await dialogs.OpenAsync<ChoiceDialog>(title, new Dictionary<string, object?>
        {
            [nameof(ChoiceDialog.Message)] = message,
            [nameof(ChoiceDialog.Icon)] = icon,
            [nameof(ChoiceDialog.Tone)] = tone,
            [nameof(ChoiceDialog.Items)] = items,
            [nameof(ChoiceDialog.Choices)] = choices,
            [nameof(ChoiceDialog.ConfirmText)] = confirmText,
        }, Options(width));
        return result as string;
    }

    public static async Task<UnsavedChoice> AskUnsavedAsync(this DialogService dialogs, IReadOnlyList<string> pages)
    {
        var r = await dialogs.ChooseAsync("Unsaved changes",
            pages.Count == 1 ? "This page has unsaved changes. Save them before leaving?" : "These pages have unsaved changes. Save them before leaving?",
            [
                new Choice("Cancel", "cancel"),
                new Choice("Discard", "discard", ButtonStyle.Danger, Variant.Outlined),
                new Choice("Save", "save", ButtonStyle.Primary, Variant.Filled, Default: true),
            ], "edit_note", "wa", pages);
        return r switch { "save" => UnsavedChoice.Save, "discard" => UnsavedChoice.Discard, _ => UnsavedChoice.Cancel };
    }

    public static async Task<ConflictChoice> AskConflictAsync(this DialogService dialogs, string path, bool isBrowser)
    {
        var r = await dialogs.ChooseAsync("This page changed",
            isBrowser
                ? $"{path} was changed since you opened it. Reload to get the latest version, or overwrite it with your text."
                : $"{path} was changed outside the app since you opened it. Reload to get the latest version, or overwrite it with your text.",
            [
                new Choice("Cancel", "cancel"),
                new Choice("Overwrite", "overwrite", ButtonStyle.Danger, Variant.Outlined),
                new Choice("Reload", "reload", ButtonStyle.Primary, Variant.Filled, Default: true),
            ], "sync_problem", "wa");
        return r switch { "reload" => ConflictChoice.Reload, "overwrite" => ConflictChoice.Overwrite, _ => ConflictChoice.Cancel };
    }

    public static async Task<bool> ConfirmDeleteAsync(this DialogService dialogs, KnowledgeNode node, FolderStats stats, bool recoverable, Func<Task>? exportFirst)
    {
        var parts = new List<string>();
        if (node.IsFolder)
        {
            parts.Add(Count(stats.Pages, "page"));
            parts.Add(Count(stats.Folders + 1, "folder"));
            if (stats.OtherFiles > 0) parts.Add(Count(stats.OtherFiles, "other file") + " (hidden or non-Markdown)");
        }
        else parts.Add("1 page");
        var what = parts.Count > 1 ? string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts[^1] : parts[0];
        var message = recoverable
            ? $"{what} will be moved to the Recycle Bin."
            : $"{what} will be deleted permanently. This can't be undone.";

        var choices = new List<Choice> { new("Cancel", "cancel", Default: true) };
        if (!recoverable && exportFirst is not null) choices.Add(new Choice("Export first", null, Action: exportFirst));
        choices.Add(new Choice(recoverable ? "Delete" : "Delete permanently", "delete", ButtonStyle.Danger, Variant.Filled, NeedsConfirm: !recoverable));

        var r = await dialogs.ChooseAsync($"Delete {node.Name}?", message, choices,
            recoverable ? "delete" : "delete_forever", recoverable ? "wa" : "da",
            confirmText: !recoverable && node.IsFolder ? node.Name : null);
        return r == "delete";
    }

    static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
