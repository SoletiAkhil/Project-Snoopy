namespace Snoopy.Voice.Console.Memories;

public sealed class InMemoryMemoryStore : IMemoryStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Memory> _memories = [];

    public Task AddAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_memories.TryAdd(memory.Id, memory))
            {
                throw new InvalidOperationException("A memory with the same ID already exists.");
            }
        }
        return Task.CompletedTask;
    }

    public Task<Memory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_memories.GetValueOrDefault(id));
        }
    }

    public Task<IReadOnlyList<Memory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Memory>>(_memories.Values.ToList().AsReadOnly());
        }
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_memories.Remove(id));
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _memories.Clear();
        }
        return Task.CompletedTask;
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A memory ID must not be empty.", nameof(id));
        }
    }
}
