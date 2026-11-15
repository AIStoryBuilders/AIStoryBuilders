using AIStoryBuildersV2.Shared.Knowledge;

namespace AIStoryBuildersV2.Shared.Hosting;

/// <summary>KGB's host capabilities (SettingsContracts.cs:45-55), plus <see cref="SupportsSpeechToText"/> (plan §6.1).</summary>
public interface IHostCapabilities
{
    bool CanToggleTheme { get; }

    /// <summary>MAUI: true (opens the stories root).</summary>
    bool CanOpenFolder { get; }
    bool IsBrowser { get; }

    /// <summary>False in both apps: the backup is Export Project.</summary>
    bool HasBackup { get; }

    /// <summary>KB deletes go to the Recycle Bin on MAUI.</summary>
    bool DeletesAreRecoverable { get; }

    /// <summary>"Documents\AIStoryBuilders" or "Browser storage".</summary>
    string RootDisplay { get; }

    /// <summary>The stories root: &lt;Documents&gt;\AIStoryBuilders, or MEMFS MyDocuments/AIStoryBuilders.</summary>
    string RootPath { get; }
    void OpenRootFolder();

    /// <summary>Whether <c>RadzenSpeechToTextButton</c> works in this host (R9).</summary>
    bool SupportsSpeechToText { get; }
}

public interface IFileExportService
{
    /// <returns>false when the user cancelled the save dialog (MAUI). The browser always returns true.</returns>
    Task<bool> SaveAsync(string suggestedFileName, Stream content, string contentType, CancellationToken ct = default);
}

public enum AppDataFile { Log }

/// <summary>App files that live outside a story folder: the log (D5, D19).</summary>
public interface IAppDataStore
{
    Task<string?> ReadAsync(AppDataFile file, CancellationToken ct = default);

    /// <exception cref="StorageQuotaExceededException">Browser storage is full (WASM).</exception>
    Task WriteAsync(AppDataFile file, string content, CancellationToken ct = default);
}
