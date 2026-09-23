using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;

namespace Snoopy.Voice.Console.Tests.Integration;

[Trait("Category", "PostgreSQLIntegration")]
public sealed class PostgreSQLMemoryStoreIntegrationTests : IAsyncLifetime
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private IDbContextFactory<SnoopyDbContext>? _contexts;

    public async Task InitializeAsync()
    {
        using var settings = PostgreSQLTestSettings.Load();
        var configured = new DatabaseOptions
        {
            ConnectionString = settings.GetConnectionString("SnoopyTestDatabase") ?? ""
        };
        var connection = new NpgsqlConnectionStringBuilder(configured.GetConnectionString());
        if (connection.Database is not string database ||
            !database.EndsWith("_tests", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Integration tests require a dedicated database whose name ends in _tests. Do not use the Snoopy application database.");
        }

        connection.Multiplexing = false;
        _connection = new NpgsqlConnection(connection.ConnectionString);
        var options = new DbContextOptionsBuilder<SnoopyDbContext>()
            .UseNpgsql(_connection)
            .EnableSensitiveDataLogging(false)
            .Options;
        try
        {
            await _connection.OpenAsync();
            await using var context = new SnoopyDbContext(options);
            if ((await context.Database.GetPendingMigrationsAsync()).Any())
            {
                throw new InvalidOperationException(
                    "Apply the Snoopy migrations explicitly to the dedicated test database before running integration tests.");
            }
            if (await context.Memories.AnyAsync())
            {
                throw new InvalidOperationException(
                    "Integration tests require an empty dedicated test database. Existing memories will not be cleared by setup.");
            }

            _transaction = await _connection.BeginTransactionAsync();
            _contexts = new TransactionContextFactory(options, _transaction);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            throw new InvalidOperationException(
                "PostgreSQL integration setup failed. Check the test connection settings, availability and applied migrations locally.");
        }
    }

    [PostgreSQLFact]
    public async Task SaveAndReadThroughNewStoreAndContextsPreserveAllFields()
    {
        var store = CreateStore();
        var created = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.FromHours(5.5)).AddTicks(120);
        var updated = created.AddHours(2).ToOffset(TimeSpan.FromHours(-7));
        foreach (var category in Enum.GetValues<MemoryCategory>())
        {
            var memory = new Memory(Guid.NewGuid(), $"  {category}: .NET, C# and C++.\r\n",
                category, category == MemoryCategory.Fact ? int.MinValue : int.MaxValue, created, updated);

            await store.AddAsync(memory);
            var persisted = await CreateStore().GetByIdAsync(memory.Id);

            Assert.Equal(memory, persisted);
            Assert.NotSame(memory, persisted);
            Assert.Equal(TimeSpan.Zero, persisted!.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, persisted.UpdatedAt.Offset);
        }
        Assert.Equal(Enum.GetValues<MemoryCategory>().Length, (await CreateStore().GetAllAsync()).Count);
    }

    [PostgreSQLFact]
    public async Task DuplicateIdsDoNotOverwriteOriginalMemory()
    {
        var store = CreateStore();
        var memory = CreateMemory("Original memory.");
        await store.AddAsync(memory);
        var duplicate = new Memory(memory.Id, "Changed content.", MemoryCategory.Other, 99, memory.CreatedAt);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(duplicate));

        Assert.Equal("A memory with the same ID already exists.", exception.Message);
        Assert.Equal(memory, await CreateStore().GetByIdAsync(memory.Id));
        Assert.Single(await store.GetAllAsync());
    }

    [PostgreSQLFact]
    public async Task DeleteRemovesOnlySelectedMemoryAndMissingIdsReturnFalseOrNull()
    {
        var store = CreateStore();
        var first = CreateMemory("Remove this memory.");
        var second = CreateMemory("Keep this memory.");
        await store.AddAsync(first);
        await store.AddAsync(second);
        var snapshot = await store.GetAllAsync();

        Assert.True(await store.RemoveAsync(first.Id));
        Assert.False(await store.RemoveAsync(first.Id));
        Assert.Null(await CreateStore().GetByIdAsync(first.Id));
        Assert.Equal(second, Assert.Single(await CreateStore().GetAllAsync()));
        Assert.Equal(2, snapshot.Count);
        Assert.True(Assert.IsAssignableFrom<IList<Memory>>(snapshot).IsReadOnly);
    }

    [PostgreSQLFact]
    public async Task ClearRemovesAllMemoriesAndStoreCanBeUsedAgain()
    {
        var store = CreateStore();
        await store.AddAsync(CreateMemory("First."));
        await store.AddAsync(CreateMemory("Second."));

        await store.ClearAsync();
        Assert.Empty(await CreateStore().GetAllAsync());
        await store.ClearAsync();
        var later = CreateMemory("After clear.");
        await store.AddAsync(later);

        Assert.Equal(later, Assert.Single(await CreateStore().GetAllAsync()));
    }

    public async Task DisposeAsync()
    {
        await using var connection = _connection;
        await using var transaction = _transaction;
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
        }
    }

    private PostgreSQLMemoryStore CreateStore() =>
        new(_contexts ?? throw new InvalidOperationException("Integration setup must finish first."));

    private static Memory CreateMemory(string content) =>
        new(Guid.NewGuid(), content, MemoryCategory.Fact, 0,
            new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));

    private sealed class TransactionContextFactory(
        DbContextOptions<SnoopyDbContext> options, NpgsqlTransaction transaction) : IDbContextFactory<SnoopyDbContext>
    {
        public SnoopyDbContext CreateDbContext()
        {
            var context = new SnoopyDbContext(options);
            context.Database.UseTransaction(transaction);
            return context;
        }

        public Task<SnoopyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}

public sealed class PostgreSQLFactAttribute : FactAttribute
{
    public PostgreSQLFactAttribute()
    {
        using var settings = PostgreSQLTestSettings.Load();
        var value = settings["PostgreSQLIntegrationTests:Enabled"];
        if (value is not null && !bool.TryParse(value, out _))
        {
            throw new InvalidOperationException("PostgreSQLIntegrationTests:Enabled must be true or false in local appsettings.json.");
        }
        if (value is null || !bool.Parse(value))
        {
            Skip = "Opt-in PostgreSQL tests: enable in local appsettings.json as documented. No database is contacted by default.";
        }
    }
}

internal static class PostgreSQLTestSettings
{
    public static ConfigurationManager Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Snoopy.sln")))
        {
            directory = directory.Parent;
        }
        var configuration = new ConfigurationManager();
        if (directory is null)
        {
            return configuration;
        }

        try
        {
            configuration.SetBasePath(directory.FullName)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            return configuration;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or FormatException or UnauthorizedAccessException)
        {
            configuration.Dispose();
            throw new InvalidOperationException(
                "Could not read local appsettings.json for integration test settings. Check JSON syntax and permissions.");
        }
    }
}
