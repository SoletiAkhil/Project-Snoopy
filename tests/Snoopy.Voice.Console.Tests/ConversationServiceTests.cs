using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Tests;

public sealed class ConversationServiceTests
{
    [Fact]
    public async Task MultipleTurnsCarryIdentityAndOrderedCompletePairs()
    {
        var options = Options(systemPrompt: "You are Snoopy. Remember the user's name in this session.");
        var replies = new Queue<string>(
        [
            "Nice to meet you, Akhil.",
            "Your name is Akhil.",
            "You told me your name is Akhil."
        ]);
        var model = new FakeLanguageModel { Handler = (_, _) => Task.FromResult(replies.Dequeue()) };
        IConversationService service = new ConversationService(model, options);

        Assert.Equal("Nice to meet you, Akhil.", await service.ReplyAsync("My name is Akhil."));
        Assert.Equal("Your name is Akhil.", await service.ReplyAsync("What is my name?"));
        Assert.Equal("You told me your name is Akhil.", await service.ReplyAsync("What did I tell you first?"));

        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "My name is Akhil.")
        ], model.Requests[0]);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "My name is Akhil."),
            new(ConversationRole.Assistant, "Nice to meet you, Akhil."),
            new(ConversationRole.User, "What is my name?"),
            new(ConversationRole.Assistant, "Your name is Akhil."),
            new(ConversationRole.User, "What did I tell you first?")
        ], model.Requests[2]);
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public async Task InputIsTrimmedButConfiguredPromptAndActualReplyArePreserved()
    {
        var options = Options(systemPrompt: "  Follow this configured prompt exactly.  ");
        const string reply = "  A precise answer.\r\n";
        var model = new FakeLanguageModel { Handler = (_, _) => Task.FromResult(reply) };
        var service = new ConversationService(model, options);

        Assert.Equal(reply, await service.ReplyAsync(" \t A question. \r\n"));
        await service.ReplyAsync("Follow up.");

        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "A question."),
            new(ConversationRole.Assistant, reply),
            new(ConversationRole.User, "Follow up.")
        ], model.Requests[1]);
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public async Task SeparateServicesDoNotShareSessionHistory()
    {
        var model = new FakeLanguageModel();
        var options = Options();
        var first = new ConversationService(model, options);
        var second = new ConversationService(model, options);

        await first.ReplyAsync("Only in the first session.");
        await second.ReplyAsync("Only in the second session.");
        await first.ReplyAsync("Continue the first session.");

        Assert.Equal(2, model.Requests[1].Count);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "Only in the first session."),
            new(ConversationRole.Assistant, "Answer."),
            new(ConversationRole.User, "Continue the first session.")
        ], model.Requests[2]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TurnLimitRetainsOnlyNewestCompletePairs(int capacity)
    {
        var options = Options(maxHistoryTurns: capacity);
        var model = new FakeLanguageModel
        {
            Handler = (messages, _) => Task.FromResult($"Answer to {messages[^1].Text}")
        };
        var service = new ConversationService(model, options);
        var totalTurns = capacity + 3;

        for (var turn = 1; turn <= totalTurns; turn++)
        {
            await service.ReplyAsync($"Question {turn}");
        }

        var expected = new List<ConversationMessage> { new(ConversationRole.System, options.SystemPrompt) };
        for (var turn = totalTurns - capacity; turn < totalTurns; turn++)
        {
            expected.Add(new(ConversationRole.User, $"Question {turn}"));
            expected.Add(new(ConversationRole.Assistant, $"Answer to Question {turn}"));
        }
        expected.Add(new(ConversationRole.User, $"Question {totalTurns}"));
        Assert.Equal<ConversationMessage>(expected, model.Requests[^1]);
        AssertBudgetedRequests(model, options);
    }

    [Theory]
    [InlineData(124, 2)]
    [InlineData(125, 1)]
    [InlineData(524, 1)]
    [InlineData(525, 0)]
    [InlineData(924, 0)]
    public async Task CharacterBudgetIncludesSystemPendingInputAndBothSidesOfPairs(
        int pendingCharacters, int retainedTurns)
    {
        var (service, model, options) = await SeedHistoryAsync();
        model.Handler = (_, _) => Task.FromResult("C");
        var pending = new string('c', pendingCharacters);

        await service.ReplyAsync(pending);

        var expected = new List<ConversationMessage> { new(ConversationRole.System, options.SystemPrompt) };
        if (retainedTurns == 2)
        {
            expected.Add(new(ConversationRole.User, new string('a', 200)));
            expected.Add(new(ConversationRole.Assistant, new string('A', 200)));
        }
        if (retainedTurns >= 1)
        {
            expected.Add(new(ConversationRole.User, new string('b', 200)));
            expected.Add(new(ConversationRole.Assistant, new string('B', 200)));
        }
        expected.Add(new(ConversationRole.User, pending));
        Assert.Equal<ConversationMessage>(expected, model.Requests[^1]);
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public async Task AssistantOutputCanEvictAnOlderWholePair()
    {
        var options = Options(systemPrompt: new string('s', 100));
        var replies = new Queue<string>([new string('A', 300), new string('B', 500), "Done."]);
        var model = new FakeLanguageModel { Handler = (_, _) => Task.FromResult(replies.Dequeue()) };
        var service = new ConversationService(model, options);

        await service.ReplyAsync(new string('a', 100));
        await service.ReplyAsync(new string('b', 100));
        await service.ReplyAsync("next");

        Assert.Equal(4, model.Requests[1].Count);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, new string('b', 100)),
            new(ConversationRole.Assistant, new string('B', 500)),
            new(ConversationRole.User, "next")
        ], model.Requests[2]);
        AssertBudgetedRequests(model, options);
    }

    [Theory]
    [InlineData(LanguageModelFailure.Network)]
    [InlineData(LanguageModelFailure.RateLimited)]
    [InlineData(LanguageModelFailure.Timeout)]
    [InlineData(LanguageModelFailure.InvalidResponse)]
    [InlineData(LanguageModelFailure.OutputLimit)]
    public async Task FailedRequestsRollBackSpeculativeEvictionWithoutAddingFallbacks(LanguageModelFailure failure)
    {
        var (service, model, options) = await SeedHistoryAsync();
        var expected = new LanguageModelException(failure);
        model.Handler = (_, _) => Task.FromException<string>(expected);

        var exception = await Assert.ThrowsAsync<LanguageModelException>(() =>
            service.ReplyAsync(new string('c', 125)));

        Assert.Same(expected, exception);
        Assert.Equal(4, model.Requests[^1].Count);
        model.Handler = (_, _) => Task.FromResult("Recovered.");
        await service.ReplyAsync("retry");
        AssertOriginalHistory(model.Requests[^1], options, "retry");
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public async Task UnexpectedClientExceptionAlsoLeavesHistoryUnchanged()
    {
        var (service, model, options) = await SeedHistoryAsync();
        var expected = new InvalidOperationException("Client failed before returning a response.");
        model.Handler = (_, _) => throw expected;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReplyAsync(new string('c', 125)));

        Assert.Same(expected, exception);
        model.Handler = (_, _) => Task.FromResult("Recovered.");
        await service.ReplyAsync("retry");
        AssertOriginalHistory(model.Requests[^1], options, "retry");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task EmptyReplyIsInvalidAndDoesNotCommitOrEvictHistory(string? reply)
    {
        var (service, model, options) = await SeedHistoryAsync();
        model.Handler = (_, _) => Task.FromResult(reply!);

        var exception = await Assert.ThrowsAsync<LanguageModelException>(() =>
            service.ReplyAsync(new string('c', 125)));

        Assert.Equal(LanguageModelFailure.InvalidResponse, exception.Failure);
        Assert.Equal(4, model.Requests[^1].Count);
        model.Handler = (_, _) => Task.FromResult("Recovered.");
        await service.ReplyAsync("retry");
        AssertOriginalHistory(model.Requests[^1], options, "retry");
        AssertBudgetedRequests(model, options);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task BlankInputIsRejectedBeforeClientAndLeavesHistoryUnchanged(string? text)
    {
        var (service, model, options) = await SeedHistoryAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ReplyAsync(text!));

        Assert.Equal("userText", exception.ParamName);
        Assert.Equal(2, model.Requests.Count);
        await service.ReplyAsync("retry");
        AssertOriginalHistory(model.Requests[^1], options, "retry");
    }

    [Fact]
    public async Task TooLongInputIsRejectedBeforeClientAndLeavesHistoryUnchanged()
    {
        var (service, model, options) = await SeedHistoryAsync();
        var input = " \t" + new string('c', options.MaxInputCharacters + 1) + "\r\n";

        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => service.ReplyAsync(input));

        Assert.Equal(LanguageModelFailure.InputTooLong, exception.Failure);
        Assert.Equal(2, model.Requests.Count);
        await service.ReplyAsync("retry");
        AssertOriginalHistory(model.Requests[^1], options, "retry");
    }

    [Fact]
    public async Task InputAtLimitIsAcceptedAfterTrimming()
    {
        var model = new FakeLanguageModel();
        var service = new ConversationService(model, Options(maxInputCharacters: 4));

        Assert.Equal("Answer.", await service.ReplyAsync(" \tword\r\n "));

        Assert.Equal("word", Assert.Single(model.Requests)[^1].Text);
    }

    [Fact]
    public async Task PreCanceledRequestDoesNotCallClientOrChangeHistory()
    {
        var (service, model, options) = await SeedHistoryAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReplyAsync(new string('c', 125), cancellation.Token));

        Assert.Equal(2, model.Requests.Count);
        await service.ReplyAsync("retry");
        AssertOriginalHistory(model.Requests[^1], options, "retry");
    }

    [Fact]
    public async Task CancellationDuringModelRequestRollsBackEvictionAndReleasesSession()
    {
        var (service, model, options) = await SeedHistoryAsync();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.Handler = async (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return "This response must not be committed.";
        };

        var request = service.ReplyAsync(new string('c', 125), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(4, model.Requests[^1].Count);
        model.Handler = (_, _) => Task.FromResult("Recovered.");
        await service.ReplyAsync("retry").WaitAsync(TimeSpan.FromSeconds(5));
        AssertOriginalHistory(model.Requests[^1], options, "retry");
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public async Task ClientIgnoringCancellationCannotCommitItsReply()
    {
        var (service, model, options) = await SeedHistoryAsync();
        using var cancellation = new CancellationTokenSource();
        model.Handler = (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            return Task.FromResult("This response must not be committed.");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReplyAsync(new string('c', 125), cancellation.Token));

        Assert.Equal(4, model.Requests[^1].Count);
        model.Handler = (_, _) => Task.FromResult("Recovered.");
        await service.ReplyAsync("retry");
        AssertOriginalHistory(model.Requests[^1], options, "retry");
        AssertBudgetedRequests(model, options);
    }

    [Theory]
    [InlineData(1, 1024)]
    [InlineData(600, 400)]
    [InlineData(924, 1)]
    public async Task OversizedNewestPairIsReturnedIntactButEvictedWholeAfterOlderPairs(
        int inputCharacters, int replyCharacters)
    {
        var (service, model, options) = await SeedHistoryAsync();
        var oversizedReply = new string('z', replyCharacters);
        model.Handler = (_, _) => Task.FromResult(oversizedReply);

        Assert.Equal(oversizedReply, await service.ReplyAsync(new string('c', inputCharacters)));

        model.Handler = (_, _) => Task.FromResult("Recovered.");
        await service.ReplyAsync("retry");
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "retry")
        ], model.Requests[^1]);
        await service.ReplyAsync("next");
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "retry"),
            new(ConversationRole.Assistant, "Recovered."),
            new(ConversationRole.User, "next")
        ], model.Requests[^1]);
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public async Task ClientReceivesIndependentReadOnlySnapshots()
    {
        var options = Options(maxHistoryTurns: 1);
        var model = new FakeLanguageModel
        {
            Handler = (messages, _) =>
            {
                var list = Assert.IsAssignableFrom<IList<ConversationMessage>>(messages);
                Assert.True(list.IsReadOnly);
                Assert.Throws<NotSupportedException>(() =>
                    list.Add(new(ConversationRole.System, "Cannot inject a message.")));
                return Task.FromResult("Answer.");
            }
        };
        var service = new ConversationService(model, options);

        await service.ReplyAsync("first");
        await service.ReplyAsync("second");
        await service.ReplyAsync("third");

        Assert.NotSame(model.Requests[0], model.Requests[1]);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "first")
        ], model.Requests[0]);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "first"),
            new(ConversationRole.Assistant, "Answer."),
            new(ConversationRole.User, "second")
        ], model.Requests[1]);
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public async Task OverlappingCallsAreSequentialAndCanceledWaitersDoNotBecomeTurns()
    {
        var options = Options();
        var firstReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new FakeLanguageModel
        {
            Handler = (messages, _) =>
            {
                if (messages[^1].Text == "first")
                {
                    started.TrySetResult();
                    return firstReply.Task;
                }
                return Task.FromResult("Second answer.");
            }
        };
        var service = new ConversationService(model, options);
        using var cancellation = new CancellationTokenSource();

        var first = service.ReplyAsync("first");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var canceled = service.ReplyAsync("canceled waiter", cancellation.Token);
        var second = service.ReplyAsync("second");
        try
        {
            Assert.Single(model.Requests);
            Assert.False(second.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                canceled.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Single(model.Requests);
        }
        finally
        {
            firstReply.TrySetResult("First answer.");
        }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("First answer.", await first);
        Assert.Equal("Second answer.", await second);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, "first"),
            new(ConversationRole.Assistant, "First answer."),
            new(ConversationRole.User, "second")
        ], model.Requests[1]);
        AssertBudgetedRequests(model, options);
    }

    [Fact]
    public void NullDependenciesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ConversationService(null!, Options()));
        Assert.Throws<ArgumentNullException>(() => new ConversationService(new FakeLanguageModel(), null!));
    }

    [Fact]
    public void InvalidOptionsCannotAllowSystemAndInputToExceedRequestBudget()
    {
        var options = Options(systemPrompt: new string('s', 100), maxInputCharacters: 925);

        Assert.Throws<ArgumentException>(() => new ConversationService(new FakeLanguageModel(), options));
    }

    private static SnoopyOptions Options(
        int maxHistoryTurns = 12, int maxInputCharacters = 924, string systemPrompt = "You are Snoopy.") => new()
    {
        SystemPrompt = systemPrompt,
        MaxHistoryTurns = maxHistoryTurns,
        MaxHistoryCharacters = 1024,
        MaxInputCharacters = maxInputCharacters
    };

    private static async Task<(ConversationService Service, FakeLanguageModel Model, SnoopyOptions Options)>
        SeedHistoryAsync()
    {
        var options = Options(systemPrompt: new string('s', 100));
        var model = new FakeLanguageModel
        {
            Handler = (messages, _) => Task.FromResult(messages[^1].Text.ToUpperInvariant())
        };
        var service = new ConversationService(model, options);
        await service.ReplyAsync(new string('a', 200));
        await service.ReplyAsync(new string('b', 200));
        return (service, model, options);
    }

    private static void AssertOriginalHistory(
        IReadOnlyList<ConversationMessage> request, SnoopyOptions options, string pending)
    {
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, options.SystemPrompt),
            new(ConversationRole.User, new string('a', 200)),
            new(ConversationRole.Assistant, new string('A', 200)),
            new(ConversationRole.User, new string('b', 200)),
            new(ConversationRole.Assistant, new string('B', 200)),
            new(ConversationRole.User, pending)
        ], request);
    }

    private static void AssertBudgetedRequests(FakeLanguageModel model, SnoopyOptions options)
    {
        Assert.All(model.Requests, request =>
        {
            Assert.InRange(request.Count, 2, options.MaxHistoryTurns * 2 + 2);
            Assert.Equal(0, (request.Count - 2) % 2);
            Assert.Equal(new(ConversationRole.System, options.SystemPrompt), request[0]);
            Assert.Single(request, message => message.Role == ConversationRole.System);
            Assert.Equal(ConversationRole.User, request[^1].Role);
            Assert.InRange(request.Sum(message => (long)message.Text.Length), 0L, options.MaxHistoryCharacters);
            for (var index = 1; index < request.Count - 1; index += 2)
            {
                Assert.Equal(ConversationRole.User, request[index].Role);
                Assert.Equal(ConversationRole.Assistant, request[index + 1].Role);
            }
        });
    }

    private sealed class FakeLanguageModel : ILanguageModelClient
    {
        public Func<IReadOnlyList<ConversationMessage>, CancellationToken, Task<string>> Handler { get; set; } =
            (_, _) => Task.FromResult("Answer.");

        public List<IReadOnlyList<ConversationMessage>> Requests { get; } = [];

        public Task<string> CompleteAsync(
            IReadOnlyList<ConversationMessage> messages, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages);
            return Handler(messages, cancellationToken);
        }
    }
}
