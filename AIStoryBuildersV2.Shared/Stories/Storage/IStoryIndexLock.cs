namespace AIStoryBuildersV2.Shared.Stories.Storage;

public interface IStoryIndexLock
{
    Task RunAsync(Func<Task> action, CancellationToken ct = default);
}
