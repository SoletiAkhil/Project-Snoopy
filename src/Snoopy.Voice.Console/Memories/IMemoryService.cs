namespace Snoopy.Voice.Console.Memories;

public interface IMemoryService
{
    Task<Memory> RememberAsync(string content, CancellationToken cancellationToken = default);

    // Removes all normalized full-text matches, including repeated saves of the same content.
    Task<bool> ForgetAsync(string content, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Memory>> GetMemoriesAsync(CancellationToken cancellationToken = default);

    Task ClearMemoriesAsync(CancellationToken cancellationToken = default);
}
