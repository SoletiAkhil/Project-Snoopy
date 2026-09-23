using Microsoft.EntityFrameworkCore;
using Npgsql;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;

namespace Snoopy.Voice.Console.Tests;

public sealed class PostgreSQLMemoryStoreTests
{
    private const string FailureDetails = "private-memory-content; private-database-error-details";
    private static readonly Guid MemoryId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    public static TheoryData<string> Operations => new()
    {
        nameof(IMemoryStore.AddAsync),
        nameof(IMemoryStore.GetByIdAsync),
        nameof(IMemoryStore.GetAllAsync),
        nameof(IMemoryStore.RemoveAsync),
        nameof(IMemoryStore.ClearAsync)
    };

    [Fact]
    public void NullContextFactoryIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgreSQLMemoryStore(null!));
    }

    [Fact]
    public async Task InvalidArgumentsAreRejectedBeforeTheContextFactoryIsUsed()
    {
        var factory = new FailingFactory(new InvalidOperationException("The factory must not be used."));
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        var nullMemory = await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddAsync(null!));
        var emptyReadId = await Assert.ThrowsAsync<ArgumentException>(() => store.GetByIdAsync(Guid.Empty));
        var emptyRemoveId = await Assert.ThrowsAsync<ArgumentException>(() => store.RemoveAsync(Guid.Empty));

        Assert.Equal("memory", nullMemory.ParamName);
        Assert.Equal("id", emptyReadId.ParamName);
        Assert.Equal("id", emptyRemoveId.ParamName);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task PreCanceledOperationsDoNotUseTheContextFactory(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new FailingFactory(new InvalidOperationException("The factory must not be used."));
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeOperationAsync(store, operation, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData(nameof(IMemoryStore.AddAsync), MemoryStorageOperation.Save)]
    [InlineData(nameof(IMemoryStore.GetByIdAsync), MemoryStorageOperation.Read)]
    [InlineData(nameof(IMemoryStore.GetAllAsync), MemoryStorageOperation.Read)]
    [InlineData(nameof(IMemoryStore.RemoveAsync), MemoryStorageOperation.Remove)]
    [InlineData(nameof(IMemoryStore.ClearAsync), MemoryStorageOperation.Clear)]
    public async Task KnownFailuresAreSanitizedForTheCorrectOperation(
        string operation, MemoryStorageOperation expectedOperation)
    {
        using var cancellation = new CancellationTokenSource();

        foreach (var failure in CreateStorageFailures())
        {
            foreach (var returnFaultedTask in new[] { false, true })
            {
                var factory = new FailingFactory(failure) { ReturnFaultedTask = returnFaultedTask };
                IMemoryStore store = new PostgreSQLMemoryStore(factory);

                var exception = await Assert.ThrowsAsync<MemoryStorageException>(() =>
                    InvokeOperationAsync(store, operation, cancellation.Token));

                AssertSanitized(exception, expectedOperation);
                Assert.Equal(1, factory.Calls);
                Assert.Equal(cancellation.Token, factory.Token);
                Assert.False(cancellation.IsCancellationRequested);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task CancellationRequestedDuringContextCreationIsPreserved(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(FailureDetails, cancellation.Token);
        var factory = new FailingFactory(failure)
        {
            BeforeFailure = cancellation.Cancel,
            ReturnFaultedTask = true
        };
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeOperationAsync(store, operation, cancellation.Token));

        Assert.Same(failure, exception);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(cancellation.Token, factory.Token);
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task UnknownProgrammerErrorsArePropagatedWithoutWrapping(string operation)
    {
        Exception[] failures =
        [
            new InvalidOperationException(FailureDetails),
            new ArgumentException(FailureDetails),
            new NotSupportedException(FailureDetails),
            new FormatException(FailureDetails)
        ];

        foreach (var failure in failures)
        {
            var factory = new FailingFactory(failure) { ReturnFaultedTask = true };
            IMemoryStore store = new PostgreSQLMemoryStore(factory);

            var exception = await Record.ExceptionAsync(() => InvokeOperationAsync(store, operation));

            Assert.Same(failure, exception);
            Assert.Equal(1, factory.Calls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateMemoryPrimaryKeyUsesTheExistingSanitizedDuplicateIdError(bool returnFaultedTask)
    {
        var factory = new FailingFactory(CreateUpdateFailure(PostgresErrorCodes.UniqueViolation, "PK_Memories"))
        {
            ReturnFaultedTask = returnFaultedTask
        };
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddAsync(new Memory("A fact.")));

        Assert.Equal("A memory with the same ID already exists.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(FailureDetails, exception.ToString());
        Assert.DoesNotContain("PK_Memories", exception.ToString());
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "PK_OtherTable")]
    [InlineData(PostgresErrorCodes.UniqueViolation, "pk_memories")]
    [InlineData(PostgresErrorCodes.UniqueViolation, null)]
    [InlineData(PostgresErrorCodes.UniqueViolation, "")]
    [InlineData(PostgresErrorCodes.NotNullViolation, "PK_Memories")]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "PK_Memories")]
    [InlineData(PostgresErrorCodes.CheckViolation, "PK_Memories")]
    public async Task OtherConstraintFailuresAreNotMisreportedAsDuplicateMemoryIds(
        string sqlState, string? constraintName)
    {
        var factory = new FailingFactory(CreateUpdateFailure(sqlState, constraintName));
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        var exception = await Assert.ThrowsAsync<MemoryStorageException>(() =>
            store.AddAsync(new Memory("A fact.")));

        AssertSanitized(exception, MemoryStorageOperation.Save);
        Assert.DoesNotContain(sqlState, exception.Message);
        if (!string.IsNullOrEmpty(constraintName))
        {
            Assert.DoesNotContain(constraintName, exception.Message);
        }
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData(nameof(IMemoryStore.GetByIdAsync), MemoryStorageOperation.Read)]
    [InlineData(nameof(IMemoryStore.GetAllAsync), MemoryStorageOperation.Read)]
    [InlineData(nameof(IMemoryStore.RemoveAsync), MemoryStorageOperation.Remove)]
    [InlineData(nameof(IMemoryStore.ClearAsync), MemoryStorageOperation.Clear)]
    public async Task PrimaryKeyFailuresOutsideAddRemainStorageFailures(
        string operation, MemoryStorageOperation expectedOperation)
    {
        var factory = new FailingFactory(CreateUpdateFailure(PostgresErrorCodes.UniqueViolation, "PK_Memories"));
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        var exception = await Assert.ThrowsAsync<MemoryStorageException>(() =>
            InvokeOperationAsync(store, operation));

        AssertSanitized(exception, expectedOperation);
        Assert.DoesNotContain("PK_Memories", exception.ToString());
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task UnwrappedPostgresUniqueViolationRemainsASanitizedStorageFailure()
    {
        var factory = new FailingFactory(CreatePostgresFailure(PostgresErrorCodes.UniqueViolation, "PK_Memories"));
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        var exception = await Assert.ThrowsAsync<MemoryStorageException>(() =>
            store.AddAsync(new Memory("A fact.")));

        AssertSanitized(exception, MemoryStorageOperation.Save);
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EachFailedOperationCreatesAndDisposesAFreshContext(string operation)
    {
        // Missing provider configuration forces failure before any connection can be created.
        var options = new DbContextOptionsBuilder<SnoopyDbContext>().Options;
        using var factory = new RecordingFactory(options);
        IMemoryStore store = new PostgreSQLMemoryStore(factory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeOperationAsync(store, operation));

            Assert.Equal(attempt + 1, factory.Contexts.Count);
            Assert.Throws<ObjectDisposedException>(() => factory.Contexts[attempt].Set<MemoryEntity>());
        }

        Assert.NotSame(factory.Contexts[0], factory.Contexts[1]);
    }

    private static Task InvokeOperationAsync(
        IMemoryStore store, string operation, CancellationToken cancellationToken = default) => operation switch
    {
        nameof(IMemoryStore.AddAsync) => store.AddAsync(new Memory("A fact."), cancellationToken),
        nameof(IMemoryStore.GetByIdAsync) => store.GetByIdAsync(MemoryId, cancellationToken),
        nameof(IMemoryStore.GetAllAsync) => store.GetAllAsync(cancellationToken),
        nameof(IMemoryStore.RemoveAsync) => store.RemoveAsync(MemoryId, cancellationToken),
        nameof(IMemoryStore.ClearAsync) => store.ClearAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown test operation.")
    };

    private static IEnumerable<Exception> CreateStorageFailures()
    {
        yield return new NpgsqlException(FailureDetails, new InvalidOperationException(FailureDetails));
        yield return new DbUpdateException(FailureDetails, new InvalidOperationException(FailureDetails));
        yield return new TimeoutException(FailureDetails, new InvalidOperationException(FailureDetails));
        yield return new OperationCanceledException(FailureDetails);
        yield return new OperationCanceledException(
            FailureDetails, new InvalidOperationException(FailureDetails), new CancellationToken(canceled: true));
        yield return new TaskCanceledException(FailureDetails, new InvalidOperationException(FailureDetails));
    }

    private static DbUpdateException CreateUpdateFailure(string sqlState, string? constraintName) =>
        new(FailureDetails, CreatePostgresFailure(sqlState, constraintName));

    private static PostgresException CreatePostgresFailure(string sqlState, string? constraintName) =>
        new(FailureDetails, "ERROR", "ERROR", sqlState, detail: FailureDetails, constraintName: constraintName);

    private static void AssertSanitized(MemoryStorageException exception, MemoryStorageOperation operation)
    {
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(new MemoryStorageException(operation).Message, exception.Message);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(FailureDetails, exception.ToString());
    }

    private sealed class FailingFactory(Exception failure) : IDbContextFactory<SnoopyDbContext>
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public Action? BeforeFailure { get; init; }
        public bool ReturnFaultedTask { get; init; }

        public SnoopyDbContext CreateDbContext()
        {
            Calls++;
            BeforeFailure?.Invoke();
            throw failure;
        }

        public Task<SnoopyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            BeforeFailure?.Invoke();
            if (ReturnFaultedTask)
            {
                return Task.FromException<SnoopyDbContext>(failure);
            }
            throw failure;
        }
    }

    private sealed class RecordingFactory(DbContextOptions<SnoopyDbContext> options)
        : IDbContextFactory<SnoopyDbContext>, IDisposable
    {
        public List<SnoopyDbContext> Contexts { get; } = [];

        public SnoopyDbContext CreateDbContext()
        {
            var context = new SnoopyDbContext(options);
            Contexts.Add(context);
            return context;
        }

        public Task<SnoopyDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public void Dispose()
        {
            foreach (var context in Contexts)
            {
                context.Dispose();
            }
        }
    }
}
