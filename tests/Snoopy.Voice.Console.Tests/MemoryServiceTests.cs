using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Tests;

public sealed class MemoryServiceTests
{
    [Fact]
    public async Task RememberCreatesMemoryAndSavesItThroughStore()
    {
        var store = new RecordingMemoryStore();
        IMemoryService service = new MemoryService(store);
        var before = DateTimeOffset.UtcNow;

        var memory = await service.RememberAsync(" \t I  prefer .NET for backend development.\r\n");

        Assert.NotEqual(Guid.Empty, memory.Id);
        Assert.Equal("I  prefer .NET for backend development.", memory.Content);
        Assert.Equal(MemoryCategory.Preference, memory.Category);
        Assert.Equal(0, memory.Importance);
        Assert.InRange(memory.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(memory.CreatedAt, memory.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, memory.CreatedAt.Offset);
        Assert.Same(memory, Assert.Single(await store.Storage.GetAllAsync()));
        Assert.Equal([nameof(IMemoryStore.AddAsync)], store.Calls.Select(call => call.Operation));
    }

    [Theory]
    [InlineData("I prefer .NET.", MemoryCategory.Preference)]
    [InlineData("My preference is C#.", MemoryCategory.Preference)]
    [InlineData("I PREFER C++!", MemoryCategory.Preference)]
    [InlineData("My goal is to learn C#.", MemoryCategory.Goal)]
    [InlineData("I want to learn .NET.", MemoryCategory.Goal)]
    [InlineData("I WANT \t TO learn C#.", MemoryCategory.Goal)]
    [InlineData("My wife works in healthcare.", MemoryCategory.Person)]
    [InlineData("My son likes music.", MemoryCategory.Person)]
    [InlineData("MY FAMILY lives nearby.", MemoryCategory.Person)]
    [InlineData("My name is Akhil.", MemoryCategory.Fact)]
    [InlineData("My favorite programming language is C#.", MemoryCategory.Fact)]
    [InlineData("The goalkeeper likes preferential seating.", MemoryCategory.Fact)]
    [InlineData("I prefer .NET and want to learn Go.", MemoryCategory.Other)]
    [InlineData("My wife and I prefer .NET.", MemoryCategory.Other)]
    [InlineData("My goal is to visit my family.", MemoryCategory.Other)]
    [InlineData("My son and I prefer C# and want to learn Go.", MemoryCategory.Other)]
    [InlineData("I prefer C#; my preference is clear.", MemoryCategory.Preference)]
    public async Task CategoriesUseWholeWordsAndAmbiguousMatchesUseOther(string content, MemoryCategory expected)
    {
        var service = new MemoryService(new InMemoryMemoryStore());

        var memory = await service.RememberAsync(content);

        Assert.Equal(expected, memory.Category);
        Assert.Equal(content, memory.Content);
    }

    [Fact]
    public async Task RepeatedRememberCommandsCreateSeparateMemories()
    {
        var service = new MemoryService(new InMemoryMemoryStore());

        var first = await service.RememberAsync("I prefer .NET.");
        var second = await service.RememberAsync("I prefer .NET.");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, (await service.GetMemoriesAsync()).Count);
    }

    [Theory]
    [InlineData("I prefer .NET for backend development.")]
    [InlineData("i PREFER .net for BACKEND development")]
    [InlineData(" \t I  prefer \r\n .NET for backend development!?  ")]
    public async Task ForgetRemovesNormalizedFullTextMatchAndLeavesOtherMemories(string content)
    {
        var store = new RecordingMemoryStore();
        var matching = new Memory("I prefer .NET for backend development.");
        var different = new Memory("I prefer .NET for frontend development.");
        await store.Storage.AddAsync(matching);
        await store.Storage.AddAsync(different);
        IMemoryService service = new MemoryService(store);

        Assert.True(await service.ForgetAsync(content));

        Assert.Equal(matching.Id, Assert.Single(store.RemovedIds));
        Assert.Equal(different, Assert.Single(await store.Storage.GetAllAsync()));
        Assert.Equal(
            [nameof(IMemoryStore.GetAllAsync), nameof(IMemoryStore.RemoveAsync)],
            store.Calls.Select(call => call.Operation));
    }

    [Theory]
    [InlineData("I prefer .NET for backend development.", "I prefer .NET.")]
    [InlineData("My favorite programming language is C#.", "My favorite programming language")]
    [InlineData("I prefer .NET.", "I prefer NET.")]
    [InlineData("I prefer C#.", "I prefer C.")]
    [InlineData("I prefer C++.", "I prefer C.")]
    [InlineData("I prefer C++.", "I prefer C#.")]
    [InlineData("My label is A, B.", "My label is A B.")]
    [InlineData("My name is Akhil.", "My name is Alex.")]
    public async Task PartialOrDifferentContentReturnsNotFoundWithoutDeleting(string stored, string query)
    {
        var store = new InMemoryMemoryStore();
        var memory = new Memory(stored);
        await store.AddAsync(memory);
        var service = new MemoryService(store);

        Assert.False(await service.ForgetAsync(query));

        Assert.Equal(memory, Assert.Single(await service.GetMemoriesAsync()));
    }

    [Fact]
    public async Task ForgetOnEmptyStoreReturnsNotFound()
    {
        var store = new RecordingMemoryStore();
        var service = new MemoryService(store);

        Assert.False(await service.ForgetAsync("I prefer .NET."));

        Assert.Empty(store.RemovedIds);
        Assert.Equal([nameof(IMemoryStore.GetAllAsync)], store.Calls.Select(call => call.Operation));
    }

    [Fact]
    public async Task ForgetRemovesAllNormalizedDuplicatesRatherThanLeavingForgottenContentBehind()
    {
        var service = new MemoryService(new InMemoryMemoryStore());
        await service.RememberAsync("I prefer .NET.");
        await service.RememberAsync("i  prefer .net!");
        var keep = await service.RememberAsync("I prefer C#.");

        Assert.True(await service.ForgetAsync("I prefer .NET"));

        Assert.Equal(keep, Assert.Single(await service.GetMemoriesAsync()));
        Assert.False(await service.ForgetAsync("I prefer .NET"));
    }

    [Fact]
    public async Task ForgetDoesNotReportSuccessIfStoreDidNotRemoveMatchingMemory()
    {
        var store = new RecordingMemoryStore { DeclineRemovals = true };
        await store.Storage.AddAsync(new Memory("I prefer .NET."));

        Assert.False(await new MemoryService(store).ForgetAsync("I prefer .NET."));

        Assert.Single(store.RemovedIds);
    }

    [Fact]
    public async Task GetMemoriesReturnsStoreSnapshotAndClearRemovesAllMemories()
    {
        var store = new RecordingMemoryStore();
        var first = new Memory("First memory.");
        var second = new Memory("Second memory.");
        await store.Storage.AddAsync(first);
        await store.Storage.AddAsync(second);
        IMemoryService service = new MemoryService(store);

        var saved = await service.GetMemoriesAsync();
        Assert.Equal(
            new[] { first, second }.OrderBy(memory => memory.Id),
            saved.OrderBy(memory => memory.Id));
        await service.ClearMemoriesAsync();

        Assert.Empty(await store.Storage.GetAllAsync());
        Assert.Equal(2, saved.Count);
        Assert.Equal(
            [nameof(IMemoryStore.GetAllAsync), nameof(IMemoryStore.ClearAsync)],
            store.Calls.Select(call => call.Operation));
        await service.ClearMemoriesAsync();
        var later = await service.RememberAsync("A new memory.");
        Assert.Equal(later, Assert.Single(await service.GetMemoriesAsync()));
    }

    [Fact]
    public void NullStoreIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MemoryService(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData(".")]
    [InlineData(" .! ? ")]
    public async Task EmptyOrPunctuationOnlyContentIsRejectedBeforeTouchingStore(string? content)
    {
        var store = new RecordingMemoryStore();
        var service = new MemoryService(store);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RememberAsync(content!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ForgetAsync(content!));

        Assert.Empty(store.Calls);
        Assert.Empty(await store.Storage.GetAllAsync());
    }

    [Theory]
    [InlineData("remember")]
    [InlineData("forget")]
    [InlineData("recall")]
    [InlineData("clear")]
    public async Task PreCanceledOperationsDoNotTouchStore(string operation)
    {
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        var store = new RecordingMemoryStore();
        await store.Storage.AddAsync(new Memory("I prefer .NET."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeAsync(new MemoryService(store), operation, shutdown.Token));

        Assert.Empty(store.Calls);
        Assert.Single(await store.Storage.GetAllAsync());
    }

    [Theory]
    [InlineData("remember", 1)]
    [InlineData("forget", 2)]
    [InlineData("recall", 1)]
    [InlineData("clear", 1)]
    public async Task CancellationTokenIsForwardedToEveryStorageOperation(string operation, int expectedCalls)
    {
        using var shutdown = new CancellationTokenSource();
        var store = new RecordingMemoryStore();
        await store.Storage.AddAsync(new Memory("I prefer .NET."));

        await InvokeAsync(new MemoryService(store), operation, shutdown.Token);

        Assert.Equal(expectedCalls, store.Calls.Count);
        Assert.All(store.Calls, call => Assert.Equal(shutdown.Token, call.Token));
    }

    [Theory]
    [InlineData("remember", nameof(IMemoryStore.AddAsync))]
    [InlineData("forget", nameof(IMemoryStore.GetAllAsync))]
    [InlineData("forget", nameof(IMemoryStore.RemoveAsync))]
    [InlineData("recall", nameof(IMemoryStore.GetAllAsync))]
    [InlineData("clear", nameof(IMemoryStore.ClearAsync))]
    public async Task StorageFailuresPropagateRatherThanReturningSuccess(string operation, string failedCall)
    {
        var failure = new InvalidOperationException("Memory storage failed.");
        var store = new RecordingMemoryStore { Failure = failure, FailedCall = failedCall };
        await store.Storage.AddAsync(new Memory("I prefer .NET."));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(new MemoryService(store), operation));

        Assert.Same(failure, thrown);
        Assert.Single(await store.Storage.GetAllAsync());
    }

    private static Task InvokeAsync(IMemoryService service, string operation, CancellationToken token = default) =>
        operation switch
        {
            "remember" => service.RememberAsync("I prefer .NET.", token),
            "forget" => service.ForgetAsync("I prefer .NET.", token),
            "recall" => service.GetMemoriesAsync(token),
            "clear" => service.ClearMemoriesAsync(token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private sealed class RecordingMemoryStore : IMemoryStore
    {
        public InMemoryMemoryStore Storage { get; } = new();
        public List<(string Operation, CancellationToken Token)> Calls { get; } = [];
        public List<Guid> RemovedIds { get; } = [];
        public bool DeclineRemovals { get; init; }
        public Exception? Failure { get; init; }
        public string? FailedCall { get; init; }

        public Task AddAsync(Memory memory, CancellationToken cancellationToken = default)
        {
            Record(nameof(AddAsync), cancellationToken);
            return Storage.AddAsync(memory, cancellationToken);
        }

        public Task<Memory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Record(nameof(GetByIdAsync), cancellationToken);
            return Storage.GetByIdAsync(id, cancellationToken);
        }

        public Task<IReadOnlyList<Memory>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Record(nameof(GetAllAsync), cancellationToken);
            return Storage.GetAllAsync(cancellationToken);
        }

        public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Record(nameof(RemoveAsync), cancellationToken);
            RemovedIds.Add(id);
            return DeclineRemovals ? Task.FromResult(false) : Storage.RemoveAsync(id, cancellationToken);
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Record(nameof(ClearAsync), cancellationToken);
            return Storage.ClearAsync(cancellationToken);
        }

        private void Record(string operation, CancellationToken token)
        {
            Calls.Add((operation, token));
            if (operation == FailedCall && Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
