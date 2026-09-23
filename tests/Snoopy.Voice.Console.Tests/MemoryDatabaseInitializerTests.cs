using Microsoft.EntityFrameworkCore;
using Npgsql;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;

namespace Snoopy.Voice.Console.Tests;

public sealed class MemoryDatabaseInitializerTests
{
    [Theory]
    [InlineData("connection")]
    [InlineData("migration")]
    [InlineData("timeout")]
    [InlineData("model")]
    [InlineData("unexpected-cancellation")]
    public async Task MigrationFailuresAreSanitizedWithoutReportingSuccess(string failure)
    {
        const string sensitive = "Password=never-print-this-test-value";
        Exception underlying = failure switch
        {
            "connection" => new NpgsqlException(sensitive),
            "migration" => new DbUpdateException(sensitive),
            "timeout" => new TimeoutException(sensitive),
            "model" => new InvalidOperationException(sensitive),
            _ => new OperationCanceledException(sensitive)
        };
        var factory = new FailingFactory(underlying);

        var exception = await Assert.ThrowsAsync<MemoryStorageException>(() =>
            new MemoryDatabaseInitializer(factory).MigrateAsync());

        Assert.Equal(MemoryStorageOperation.Migrate, exception.Operation);
        Assert.Contains("Unable to apply memory database migrations", exception.Message);
        Assert.DoesNotContain(sensitive, exception.ToString());
        Assert.Null(exception.InnerException);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task PreCanceledMigrationDoesNotCreateContextOrTouchDatabase()
    {
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        var factory = new FailingFactory(new InvalidOperationException("Should not be used."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MemoryDatabaseInitializer(factory).MigrateAsync(shutdown.Token));

        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task CallerCancellationIsNotMisreportedAsDatabaseFailure()
    {
        using var shutdown = new CancellationTokenSource();
        var factory = new FailingFactory(new OperationCanceledException(shutdown.Token))
        {
            BeforeFailure = shutdown.Cancel
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MemoryDatabaseInitializer(factory).MigrateAsync(shutdown.Token));

        Assert.Equal(shutdown.Token, factory.Token);
    }

    private sealed class FailingFactory(Exception failure) : IDbContextFactory<SnoopyDbContext>
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public Action? BeforeFailure { get; init; }

        public SnoopyDbContext CreateDbContext() => throw new NotSupportedException("Use the async factory.");

        public Task<SnoopyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            BeforeFailure?.Invoke();
            throw failure;
        }
    }
}
