namespace Snoopy.Voice.Console.Memories;

public interface IMemoryStore
{
    // Duplicate IDs are rejected rather than overwriting an existing memory.
    Task AddAsync(Memory memory, CancellationToken cancellationToken = default);

    Task<Memory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Returns a read-only snapshot; enumeration order is not guaranteed.
    Task<IReadOnlyList<Memory>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
