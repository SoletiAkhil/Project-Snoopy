using Microsoft.Extensions.DependencyInjection;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Tests;

public sealed class MemoryAwareConversationTests
{
    [Fact]
    public async Task NewApplicationUsesSavedNameWithEmptyConversationHistory()
    {
        var store = new InMemoryMemoryStore();
        var configuration = Configuration(new SnoopyOptions { EnableMemoryContext = true });
        using (var first = ApplicationServices.CreateProvider(configuration, TextWriter.Null, store))
        {
            await first.GetRequiredService<IMemoryService>().RememberAsync("My name is Akhil.");
            await first.GetRequiredService<IConversationHistory>().AddTurnAsync(new("Old conversation.", "Old reply."));
        }
        using var restarted = ApplicationServices.CreateProvider(configuration, TextWriter.Null, store);
        var history = restarted.GetRequiredService<IConversationHistory>();
        Assert.Empty(await history.GetTurnsAsync());
        var model = new RecordingModel();
        var conversation = ActivatorUtilities.CreateInstance<ConversationService>(restarted, model);

        await conversation.ReplyAsync("Do you remember my name?");

        var request = Assert.Single(model.Requests);
        Assert.Equal(3, request.Count);
        Assert.Equal(ConversationRole.System, request[0].Role);
        Assert.DoesNotContain("Akhil", request[0].Text);
        Assert.Equal(ConversationRole.User, request[1].Role);
        Assert.Contains("My name is Akhil.", request[1].Text);
        Assert.Equal(new(ConversationRole.User, "Do you remember my name?"), request[2]);
        Assert.DoesNotContain(request, message => message.Text.Contains("Old conversation.", StringComparison.Ordinal));
        Assert.Equal(new("Do you remember my name?", "Answer."), Assert.Single(await history.GetTurnsAsync()));
    }

    [Fact]
    public async Task ContextIsTransientAndRefreshesAfterForgetAndConversationReset()
    {
        var options = new SnoopyOptions { EnableMemoryContext = true };
        var memories = new MemoryService(new InMemoryMemoryStore());
        await memories.RememberAsync("My name is Akhil.");
        var history = new InMemoryConversationHistory(options);
        var model = new RecordingModel();
        var conversation = new ConversationService(
            model, options, history, new MemoryContextProvider(memories, options, TextWriter.Null));

        await conversation.ReplyAsync("Do you remember my name?");
        await conversation.ClearAsync();
        Assert.Empty(await history.GetTurnsAsync());
        await conversation.ReplyAsync("What is my name?");
        Assert.Equal(3, model.Requests[1].Count);
        Assert.Contains("Akhil", model.Requests[1][1].Text);

        await memories.ForgetAsync("My name is Akhil.");
        await conversation.ReplyAsync("What is my name now?");

        Assert.DoesNotContain(model.Requests[2], message => message.Text.Contains("Akhil", StringComparison.Ordinal));
        Assert.Contains("[]", model.Requests[2][^2].Text);
        Assert.All(await history.GetTurnsAsync(), turn =>
        {
            Assert.DoesNotContain("<documents>", turn.User);
            Assert.DoesNotContain("<documents>", turn.Assistant);
        });
    }

    [Fact]
    public async Task RequestBudgetIncludesMemoryInstructionsAndKeepsOnlyCompleteHistoryPairs()
    {
        var options = new SnoopyOptions
        {
            SystemPrompt = "You are Snoopy.",
            EnableMemoryContext = true,
            MaxMemoryContextCharacters = 1024,
            MaxHistoryCharacters = 2048,
            MaxInputCharacters = 400
        };
        var memories = new MemoryService(new InMemoryMemoryStore());
        await memories.RememberAsync(new string('m', 450));
        var history = new InMemoryConversationHistory(options);
        await history.AddTurnAsync(new(new string('a', 300), new string('A', 300)));
        await history.AddTurnAsync(new(new string('b', 300), new string('B', 300)));
        var model = new RecordingModel();
        var conversation = new ConversationService(
            model, options, history, new MemoryContextProvider(memories, options, TextWriter.Null));

        await conversation.ReplyAsync(new string('q', 400));

        var request = Assert.Single(model.Requests);
        Assert.InRange(request.Sum(message => message.Text.Length), 1, options.MaxHistoryCharacters);
        Assert.Equal(5, request.Count);
        Assert.Equal(new(ConversationRole.User, new string('b', 300)), request[1]);
        Assert.Equal(new(ConversationRole.Assistant, new string('B', 300)), request[2]);
        Assert.StartsWith(MemoryContextProvider.DocumentPrefix, request[^2].Text);
        Assert.Equal(new string('q', 400), request[^1].Text);
        Assert.Equal(new string('q', 400), (await history.GetTurnsAsync())[^1].User);
    }

    [Fact]
    public async Task FailedModelReplyDoesNotCommitContextHistoryOrNewMemories()
    {
        var options = new SnoopyOptions { EnableMemoryContext = true };
        var memories = new MemoryService(new InMemoryMemoryStore());
        await memories.RememberAsync("My name is Akhil.");
        var history = new InMemoryConversationHistory(options);
        var model = new RecordingModel
        {
            Failure = new LanguageModelException(LanguageModelFailure.Network)
        };
        var conversation = new ConversationService(
            model, options, history, new MemoryContextProvider(memories, options, TextWriter.Null));

        await Assert.ThrowsAsync<LanguageModelException>(() => conversation.ReplyAsync("I prefer tea."));

        Assert.Empty(await history.GetTurnsAsync());
        Assert.Equal("My name is Akhil.", Assert.Single(await memories.GetMemoriesAsync()).Content);
    }

    private static ApplicationConfiguration Configuration(SnoopyOptions options) => new()
    {
        Speech = new SpeechOptions { SubscriptionKey = "not-a-real-key", Region = "centralindia" },
        AzureOpenAI = new AzureOpenAIOptions
        {
            Endpoint = "https://unit-test.openai.azure.com/",
            ApiKey = "not-a-real-key",
            DeploymentName = "test-deployment"
        },
        Snoopy = options
    };

    private sealed class RecordingModel : ILanguageModelClient
    {
        public List<IReadOnlyList<ConversationMessage>> Requests { get; } = [];
        public Exception? Failure { get; init; }

        public Task<string> CompleteAsync(
            IReadOnlyList<ConversationMessage> messages, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToArray());
            return Failure is null ? Task.FromResult("Answer.") : Task.FromException<string>(Failure);
        }
    }
}
