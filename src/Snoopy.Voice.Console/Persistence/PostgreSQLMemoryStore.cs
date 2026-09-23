using Microsoft.EntityFrameworkCore;
using Npgsql;
using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Persistence;

public sealed class PostgreSQLMemoryStore : IMemoryStore
{
    private readonly IDbContextFactory<SnoopyDbContext> _contexts;

    public PostgreSQLMemoryStore(IDbContextFactory<SnoopyDbContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        _contexts = contexts;
    }

    public Task AddAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return ExecuteAsync(MemoryStorageOperation.Save, async (context, token) =>
        {
            context.Memories.Add(MemoryEntity.FromMemory(memory));
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<Memory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        return ExecuteAsync(MemoryStorageOperation.Read, async (context, token) =>
        {
            var entity = await context.Memories.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id, token).ConfigureAwait(false);
            return entity?.ToMemory();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Memory>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<Memory>>(MemoryStorageOperation.Read, async (context, token) =>
        {
            var entities = await context.Memories.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            return entities.Select(item => item.ToMemory()).ToList().AsReadOnly();
        }, cancellationToken);

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        return ExecuteAsync(MemoryStorageOperation.Remove, async (context, token) =>
            await context.Memories.Where(item => item.Id == id).ExecuteDeleteAsync(token).ConfigureAwait(false) > 0,
            cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(MemoryStorageOperation.Clear, async (context, token) =>
        {
            await context.Memories.ExecuteDeleteAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        MemoryStorageOperation operation,
        Func<SnoopyDbContext, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await action(context, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (
            operation == MemoryStorageOperation.Save &&
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_Memories"
            })
        {
            throw new InvalidOperationException("A memory with the same ID already exists.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MemoryStorageException(operation);
        }
        catch (Exception exception) when (exception is NpgsqlException or DbUpdateException or TimeoutException)
        {
            throw new MemoryStorageException(operation);
        }
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A memory ID must not be empty.", nameof(id));
        }
    }
}
