using Microsoft.EntityFrameworkCore;
using Npgsql;
using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Persistence;

public sealed class MemoryDatabaseInitializer(IDbContextFactory<SnoopyDbContext> contexts)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MemoryStorageException(MemoryStorageOperation.Migrate);
        }
        catch (Exception exception) when (
            exception is NpgsqlException or DbUpdateException or TimeoutException or InvalidOperationException)
        {
            throw new MemoryStorageException(MemoryStorageOperation.Migrate);
        }
    }
}
