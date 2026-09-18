using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Tests;

public sealed class InMemoryConversationHistoryTests
{
    [Fact]
    public async Task NewHistoryIsEmptyAndEmptyClearIsSafe()
    {
        IConversationHistory history = new InMemoryConversationHistory(Options());
        Assert.Empty(await history.GetTurnsAsync());
        await history.ClearAsync();
        Assert.Empty(await history.GetTurnsAsync());
    }

    [Fact]
    public async Task CompleteTurnsKeepTextAndInsertionOrder()
    {
        var history = new InMemoryConversationHistory(Options());
        ConversationTurn[] turns =
        [
            new("  First user  ", "  First assistant\r\n"),
            new("Second user", "Second assistant"),
            new("Third user", "Third assistant")
        ];
        foreach (var turn in turns)
        {
            await history.AddTurnAsync(turn);
        }
        Assert.Equal(turns, await history.GetTurnsAsync());
    }

    [Fact]
    public async Task SnapshotsAreReadOnlyAndUnaffectedByLaterAddOrClear()
    {
        var history = new InMemoryConversationHistory(Options());
        var first = new ConversationTurn("First user", "First assistant");
        await history.AddTurnAsync(first);
        var snapshot = await history.GetTurnsAsync();
        var list = Assert.IsAssignableFrom<IList<ConversationTurn>>(snapshot);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(new("Injected", "Injected")));

        await history.AddTurnAsync(new("Second user", "Second assistant"));
        Assert.Equal(2, (await history.GetTurnsAsync()).Count);
        await history.ClearAsync();

        Assert.Equal(first, Assert.Single(snapshot));
        Assert.Empty(await history.GetTurnsAsync());
        await history.AddTurnAsync(new("Fresh user", "Fresh assistant"));
        Assert.Equal(new("Fresh user", "Fresh assistant"), Assert.Single(await history.GetTurnsAsync()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MaxTurnsEvictsOldestCompletePairsFirst(int capacity)
    {
        var history = new InMemoryConversationHistory(Options(maxTurns: capacity));
        for (var index = 0; index < 6; index++)
        {
            await history.AddTurnAsync(new($"User {index}", $"Assistant {index}"));
        }
        var expected = Enumerable.Range(6 - capacity, capacity)
            .Select(index => new ConversationTurn($"User {index}", $"Assistant {index}"));
        Assert.Equal(expected, await history.GetTurnsAsync());
    }

    [Theory]
    [InlineData(124, 2)]
    [InlineData(125, 1)]
    [InlineData(524, 1)]
    [InlineData(525, 0)]
    [InlineData(924, 0)]
    public async Task PendingInputBudgetIncludesSystemAndBothSidesWithoutChangingStoredHistory(
        int pendingCharacters, int expectedCount)
    {
        var options = Options();
        var history = new InMemoryConversationHistory(options);
        var first = new ConversationTurn(new string('a', 200), new string('A', 200));
        var second = new ConversationTurn(new string('b', 200), new string('B', 200));
        await history.AddTurnAsync(first);
        await history.AddTurnAsync(second);

        var snapshot = await history.GetTurnsAsync(pendingCharacters);

        Assert.Equal(expectedCount, snapshot.Count);
        if (expectedCount > 0)
        {
            Assert.Equal(second, snapshot[^1]);
        }
        Assert.True(options.SystemPrompt.Length + pendingCharacters +
            snapshot.Sum(turn => turn.User.Length + turn.Assistant.Length) <= options.MaxHistoryCharacters);
        Assert.Equal<ConversationTurn>([first, second], await history.GetTurnsAsync());
    }

    [Fact]
    public async Task CharacterLimitAcceptsExactBudgetThenEvictsOldestWholePair()
    {
        var history = new InMemoryConversationHistory(Options());
        var first = new ConversationTurn(new string('a', 200), new string('A', 200));
        var second = new ConversationTurn(new string('b', 262), new string('B', 262));
        await history.AddTurnAsync(first);
        await history.AddTurnAsync(second);
        Assert.Equal<ConversationTurn>([first, second], await history.GetTurnsAsync());

        var third = new ConversationTurn("c", "C");
        await history.AddTurnAsync(third);

        Assert.Equal<ConversationTurn>([second, third], await history.GetTurnsAsync());
    }

    [Fact]
    public async Task SuccessfulAddEvictsHistoryThatWasExcludedFromPendingInputSnapshot()
    {
        var history = new InMemoryConversationHistory(Options());
        await history.AddTurnAsync(new(new string('a', 200), new string('A', 200)));
        var second = new ConversationTurn(new string('b', 200), new string('B', 200));
        await history.AddTurnAsync(second);
        Assert.Equal(second, Assert.Single(await history.GetTurnsAsync(125)));

        var third = new ConversationTurn(new string('c', 125), "C");
        await history.AddTurnAsync(third);

        Assert.Equal<ConversationTurn>([second, third], await history.GetTurnsAsync());
    }

    [Fact]
    public async Task IndividuallyOversizedTurnIsEvictedWholeAfterOlderTurns()
    {
        var history = new InMemoryConversationHistory(Options());
        await history.AddTurnAsync(new("Old user", "Old assistant"));
        await history.AddTurnAsync(new(new string('c', 924), "C"));
        Assert.Empty(await history.GetTurnsAsync());
    }

    [Fact]
    public async Task PreCanceledOperationsDoNotModifyHistory()
    {
        var history = new InMemoryConversationHistory(Options());
        var original = new ConversationTurn("Original user", "Original assistant");
        await history.AddTurnAsync(original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            history.GetTurnsAsync(cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            history.AddTurnAsync(new("Canceled user", "Canceled assistant"), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => history.ClearAsync(cancellation.Token));
        Assert.Equal(original, Assert.Single(await history.GetTurnsAsync()));
    }

    [Theory]
    [InlineData(null, "Assistant")]
    [InlineData("", "Assistant")]
    [InlineData("  ", "Assistant")]
    [InlineData("User", null)]
    [InlineData("User", "")]
    [InlineData("User", "  ")]
    public async Task InvalidTurnsAreRejectedWithoutChangingHistory(string? user, string? assistant)
    {
        var history = new InMemoryConversationHistory(Options());
        var original = new ConversationTurn("Original user", "Original assistant");
        await history.AddTurnAsync(original);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => history.AddTurnAsync(new(user!, assistant!)));
        Assert.Equal(original, Assert.Single(await history.GetTurnsAsync()));
    }

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryConversationHistory(null!));
        Assert.Throws<ArgumentException>(() =>
            new InMemoryConversationHistory(new SnoopyOptions { MaxHistoryTurns = 0 }));
        var history = new InMemoryConversationHistory(Options());
        await Assert.ThrowsAsync<ArgumentNullException>(() => history.AddTurnAsync(null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => history.GetTurnsAsync(-1));
    }

    [Fact]
    public async Task ConcurrentAddsDoNotLoseOrSplitTurns()
    {
        var history = new InMemoryConversationHistory(new SnoopyOptions { MaxHistoryTurns = 20 });
        var turns = Enumerable.Range(0, 20)
            .Select(index => new ConversationTurn($"User {index}", $"Assistant {index}")).ToArray();
        await Task.WhenAll(turns.Select(turn => Task.Run(() => history.AddTurnAsync(turn))))
            .WaitAsync(TimeSpan.FromSeconds(5));

        var stored = await history.GetTurnsAsync();
        Assert.Equal(turns.OrderBy(turn => turn.User), stored.OrderBy(turn => turn.User));
    }

    private static SnoopyOptions Options(int maxTurns = 12) => new()
    {
        SystemPrompt = new string('s', 100),
        MaxHistoryTurns = maxTurns,
        MaxHistoryCharacters = 1024,
        MaxInputCharacters = 924
    };
}
