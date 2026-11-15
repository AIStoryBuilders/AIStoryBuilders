using AIStoryBuildersV2.Shared.Knowledge;
using Microsoft.VisualBasic.FileIO;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

namespace AIStoryBuildersV2.Services;

/// <summary>Deletes KB pages to the Windows Recycle Bin (KGB). Other platforms delete permanently.</summary>
public sealed class RecycleBinFileDeleter : IFileDeleter
{
    public bool IsRecoverable => OperatingSystem.IsWindows();

    public void DeleteFile(string fullPath)
    {
        if (OperatingSystem.IsWindows())
            FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            File.Delete(fullPath);
    }

    public void DeleteDirectory(string fullPath)
    {
        if (OperatingSystem.IsWindows())
            FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            Directory.Delete(fullPath, recursive: true);
    }
}
