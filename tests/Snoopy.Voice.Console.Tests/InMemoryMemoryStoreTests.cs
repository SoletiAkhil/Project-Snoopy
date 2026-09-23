using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Tests;

public sealed class InMemoryMemoryStoreTests
{
    [Fact]
    public async Task NewStoreIsEmptyAndEmptyClearIsSafe()
    {
        IMemoryStore store = new InMemoryMemoryStore();

        Assert.Empty(await store.GetAllAsync());
        await store.ClearAsync();
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task AddedMemoryCanBeRetrievedByIdAndInSnapshotWithEveryPropertyPreserved()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.FromHours(5.5));
        var updatedAt = createdAt.AddHours(2).ToOffset(TimeSpan.FromHours(-7));
        var memory = new Memory(
            id, "  Prefer .NET for backend development.\r\n", MemoryCategory.Preference, 7, createdAt, updatedAt);

        await store.AddAsync(memory);

        var stored = Assert.IsType<Memory>(await store.GetByIdAsync(id));
        Assert.Equal(id, stored.Id);
        Assert.Equal(memory.Content, stored.Content);
        Assert.Equal(MemoryCategory.Preference, stored.Category);
        Assert.Equal(7, stored.Importance);
        Assert.Equal(createdAt.ToUniversalTime(), stored.CreatedAt);
        Assert.Equal(updatedAt.ToUniversalTime(), stored.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, stored.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, stored.UpdatedAt.Offset);
        Assert.Equal(memory, stored);
        Assert.Equal(memory, Assert.Single(await store.GetAllAsync()));
    }

    [Fact]
    public async Task MultipleMemoriesPreserveEveryCategory()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var memories = Enum.GetValues<MemoryCategory>()
            .Select(category => new Memory($"Memory for {category}.", category)).ToArray();

        foreach (var memory in memories)
        {
            await store.AddAsync(memory);
        }

        var stored = await store.GetAllAsync();
        Assert.Equal(memories.OrderBy(memory => memory.Id), stored.OrderBy(memory => memory.Id));
        foreach (var memory in memories)
        {
            Assert.Equal(memory, await store.GetByIdAsync(memory.Id));
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public async Task ImportanceIsPreservedWithoutReinterpretingItsValue(int importance)
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var memory = new Memory("A goal.", MemoryCategory.Goal, importance);

        await store.AddAsync(memory);

        Assert.Equal(importance, Assert.IsType<Memory>(await store.GetByIdAsync(memory.Id)).Importance);
        Assert.Equal(importance, Assert.Single(await store.GetAllAsync()).Importance);
    }

    [Fact]
    public async Task UnknownIdReturnsNullAndCannotBeRemoved()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var memory = new Memory("Keep this memory.");
        await store.AddAsync(memory);
        var missingId = Guid.NewGuid();

        Assert.Null(await store.GetByIdAsync(missingId));
        Assert.False(await store.RemoveAsync(missingId));
        Assert.Equal(memory, Assert.Single(await store.GetAllAsync()));
    }

    [Fact]
    public async Task RemoveDeletesOnlyTheSelectedMemory()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var first = new Memory("Remove this memory.");
        var second = new Memory("Keep this memory.");
        await store.AddAsync(first);
        await store.AddAsync(second);

        Assert.True(await store.RemoveAsync(first.Id));

        Assert.Null(await store.GetByIdAsync(first.Id));
        Assert.False(await store.RemoveAsync(first.Id));
        Assert.Equal(second, await store.GetByIdAsync(second.Id));
        Assert.Equal(second, Assert.Single(await store.GetAllAsync()));
    }

    [Fact]
    public async Task ClearRemovesAllMemoriesAndStoreCanBeReused()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var first = new Memory("First memory.");
        var second = new Memory("Second memory.");
        await store.AddAsync(first);
        await store.AddAsync(second);

        await store.ClearAsync();

        Assert.Empty(await store.GetAllAsync());
        Assert.Null(await store.GetByIdAsync(first.Id));
        Assert.Null(await store.GetByIdAsync(second.Id));
        await store.AddAsync(first);
        Assert.Equal(first, Assert.Single(await store.GetAllAsync()));
    }

    [Fact]
    public async Task DuplicateIdsAreRejectedWithoutOverwritingTheOriginalMemory()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var original = new Memory("Original content.");
        await store.AddAsync(original);
        var duplicate = new Memory(
            original.Id, "Replacement content.", MemoryCategory.Event, 10, original.CreatedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(duplicate));

        Assert.Equal(original, await store.GetByIdAsync(original.Id));
        Assert.Equal(original, Assert.Single(await store.GetAllAsync()));
    }

    [Fact]
    public async Task IdenticalContentWithDifferentIdsIsNotDeduplicated()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var first = new Memory("Same content.");
        var second = new Memory("Same content.");

        await store.AddAsync(first);
        await store.AddAsync(second);

        Assert.Equal(2, (await store.GetAllAsync()).Count);
        Assert.Equal(first, await store.GetByIdAsync(first.Id));
        Assert.Equal(second, await store.GetByIdAsync(second.Id));
    }

    [Fact]
    public async Task SnapshotsAreReadOnlyAndUnaffectedByLaterAddRemoveOrClear()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var first = new Memory("First memory.");
        await store.AddAsync(first);
        var snapshot = await store.GetAllAsync();
        var list = Assert.IsAssignableFrom<IList<Memory>>(snapshot);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(new Memory("Injected memory.")));
        Assert.Throws<NotSupportedException>(() => list[0] = new Memory("Replacement memory."));

        var second = new Memory("Second memory.");
        await store.AddAsync(second);
        Assert.Equal(2, (await store.GetAllAsync()).Count);
        Assert.True(await store.RemoveAsync(first.Id));
        Assert.Equal(second, Assert.Single(await store.GetAllAsync()));
        await store.ClearAsync();

        Assert.Equal(first, Assert.Single(snapshot));
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task SeparateStoresDoNotShareState()
    {
        IMemoryStore first = new InMemoryMemoryStore();
        IMemoryStore second = new InMemoryMemoryStore();
        var memory = new Memory("Only in the first store.");
        await first.AddAsync(memory);

        Assert.Empty(await second.GetAllAsync());
        Assert.Null(await second.GetByIdAsync(memory.Id));
        await second.ClearAsync();
        Assert.Equal(memory, Assert.Single(await first.GetAllAsync()));
    }

    [Fact]
    public async Task InvalidArgumentsAreRejectedWithoutChangingTheStore()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var original = new Memory("Keep this memory.");
        await store.AddAsync(original);

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetByIdAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RemoveAsync(Guid.Empty));

        Assert.Equal(original, Assert.Single(await store.GetAllAsync()));
    }

    [Fact]
    public async Task PreCanceledOperationsDoNotModifyTheStore()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var original = new Memory("Keep this memory.");
        await store.AddAsync(original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.AddAsync(new Memory("Canceled memory."), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetByIdAsync(original.Id, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAllAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RemoveAsync(original.Id, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ClearAsync(cancellation.Token));

        Assert.Equal(original, Assert.Single(await store.GetAllAsync()));
    }

    [Fact]
    public async Task ConcurrentAddsReadsAndRemovalsPreserveCompleteMemories()
    {
        IMemoryStore store = new InMemoryMemoryStore();
        var memories = Enumerable.Range(0, 64)
            .Select(index => new Memory($"Memory {index}.", MemoryCategory.Fact, index)).ToArray();

        await Task.WhenAll(memories.Select(memory => Task.Run(async () =>
        {
            await store.AddAsync(memory);
            Assert.Equal(memory, await store.GetByIdAsync(memory.Id));
            Assert.Contains(memory, await store.GetAllAsync());
        }))).WaitAsync(TimeSpan.FromSeconds(5));

        var stored = await store.GetAllAsync();
        Assert.Equal(memories.OrderBy(memory => memory.Id), stored.OrderBy(memory => memory.Id));

        var removed = await Task.WhenAll(memories.Select(memory =>
            Task.Run(() => store.RemoveAsync(memory.Id)))).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(removed, result => Assert.True(result));
        Assert.Empty(await store.GetAllAsync());
    }
}
