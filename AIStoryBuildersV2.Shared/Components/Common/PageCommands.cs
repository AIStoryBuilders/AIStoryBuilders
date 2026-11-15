using AIStoryBuildersV2.Shared.Knowledge;
using Radzen;

namespace AIStoryBuildersV2.Shared.Components.Common;

/// <summary>Interactive page commands shared by the explorer, editor and links panel.</summary>
public static class PageCommands
{
    public static string NewPageTemplate(string title) => $"---\ntitle: {title}\n---\n# {title}\n\n";

    /// <summary>Asks for a name and creates the page. Returns its path, or null when cancelled or failed.</summary>
    public static async Task<string?> CreatePageAsync(DialogService dialogs, IKnowledgeWorkspace workspace, NotificationService notify,
        string folder, string? initialName = null, string? markdown = null)
    {
        IReadOnlyCollection<string> siblings;
        try { siblings = (await workspace.GetChildrenAsync(folder)).Select(n => n.Name).ToList(); }
        catch (KnowledgeException) { siblings = []; folder = ""; }

        var name = await dialogs.AskNameAsync("New page", folder.Length == 0 ? "Page name" : $"Page name in {folder}", "Create", true, siblings,
            initialName, help: "Saved as a .md file.");
        if (name is null) return null;

        try
        {
            var path = KnowledgePath.Parse(folder.Length == 0 ? name : folder + "/" + name, isPage: true).Value;
            var title = KnowledgePath.StripExtension(name);
            var result = await workspace.SavePageAsync(path, markdown ?? NewPageTemplate(title), 0);
            if (result.Status == SaveStatus.AlreadyExists)
            {
                notify.Notify(NotificationSeverity.Warning, "Already exists", $"{path} already exists.", TimeSpan.FromSeconds(6));
                return null;
            }
            return path;
        }
        catch (KnowledgeException ex)
        {
            ShowError(notify, ex);
            return null;
        }
    }

    public static void ShowError(NotificationService notify, Exception ex, string? summary = null)
    {
        var title = summary ?? ex switch
        {
            StorageQuotaExceededException => "Browser storage is full",
            PageTooLargeException => "Page too large",
            KnowledgeAlreadyExistsException => "Already exists",
            InvalidKnowledgePathException => "Invalid name",
            KnowledgeNotFoundException => "Not found",
            _ => "Something went wrong",
        };
        var detail = ex is StorageQuotaExceededException
            ? "Nothing was saved. Use Export Project on the Details tab, and delete pages you don't need."
            : ex.Message;
        notify.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = title, Detail = detail, Duration = null });
    }
}
