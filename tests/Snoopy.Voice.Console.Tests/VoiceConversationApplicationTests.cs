using System.Runtime.CompilerServices;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console.Tests;

public sealed class VoiceConversationApplicationTests
{
    [Fact]
    public async Task MultipleTurnsPreserveContextAndNeverOverlapRecognitionModelOrPlayback()
    {
        var events = new List<string>();
        var stt = new ScriptedRecognition(
            Session.Saying("My name is Akhil."), Session.Saying("What is my name?"), Session.Saying("Goodbye Snoopy."))
        { Events = events };
        var model = new FakeModel
        {
            Handler = (messages, _) =>
            {
                Assert.False(stt.MicrophoneOpen);
                events.Add("model");
                return Task.FromResult(messages.Count == 2 ? "Nice to meet you, Akhil." : "Your name is Akhil.");
            }
        };
        var tts = new FakeSynthesis
        {
            Handler = (_, _) =>
            {
                Assert.False(stt.MicrophoneOpen);
                events.Add("playback");
                return Task.CompletedTask;
            }
        };
        var output = new StringWriter();
        await Create(stt, tts, model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(
            ["My name is Akhil.", "Nice to meet you, Akhil.", "What is my name?"],
            model.Requests[1].Skip(1).Select(message => message.Text));
        Assert.Equal(
            ["Nice to meet you, Akhil.", "Your name is Akhil.", VoiceConversationApplication.Goodbye], tts.Spoken);
        Assert.Equal(
        [
            "microphone-open", "microphone-closed", "model", "playback",
            "microphone-open", "microphone-closed", "model", "playback",
            "microphone-open", "microphone-closed", "playback"
        ], events);
        Assert.Contains("You: What is my name?", output.ToString());
        Assert.Contains("Snoopy: Your name is Akhil.", output.ToString());
        Assert.DoesNotContain("Connected", output.ToString());
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("QUIT!")]
    [InlineData("Goodbye Snoopy.")]
    [InlineData("Snoopy, stop.")]
    public async Task VoiceExitSpeaksFarewellWithoutCallingModel(string command)
    {
        var stt = new ScriptedRecognition(Session.Saying(command));
        var model = new FakeModel();
        var tts = new FakeSynthesis();
        await Create(stt, tts, model).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(model.Requests);
        Assert.Equal([VoiceConversationApplication.Goodbye], tts.Spoken);
        Assert.False(stt.MicrophoneOpen);
        Assert.Equal(1, stt.SessionsStarted);
    }

    [Fact]
    public async Task EmptyPartialAndNoMatchResultsNeverReachModel()
    {
        var stt = new ScriptedRecognition(
            new Session
            {
                Updates =
                [
                    new(RecognitionKind.Partial, "not a final"),
                    new(RecognitionKind.Final, ""),
                    new(RecognitionKind.Final, " "),
                    new(RecognitionKind.NoMatch, ""),
                    new(RecognitionKind.Final, "Hello")
                ]
            },
            Session.Saying("exit"));
        var model = new FakeModel();
        var output = new StringWriter();
        await Create(stt, new FakeSynthesis(), model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(model.Requests);
        Assert.Equal("Hello", model.Requests[0][^1].Text);
        Assert.Contains("I didn't catch that", output.ToString());
        Assert.DoesNotContain("not a final", output.ToString());
    }

    [Fact]
    public async Task FailedLlmTurnIsNotAddedToHistoryAndListeningContinues()
    {
        var stt = new ScriptedRecognition(
            Session.Saying("First question"), Session.Saying("Second question"), Session.Saying("quit"));
        var calls = 0;
        var model = new FakeModel
        {
            Handler = (_, _) => ++calls == 1
                ? throw new LanguageModelException(LanguageModelFailure.RateLimited)
                : Task.FromResult("The second answer.")
        };
        var tts = new FakeSynthesis();
        var output = new StringWriter();
        await Create(stt, tts, model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(2, model.Requests[1].Count);
        Assert.Equal("Second question", model.Requests[1][^1].Text);
        Assert.Equal(
            [VoiceConversationApplication.ModelUnavailable, "The second answer.", VoiceConversationApplication.Goodbye],
            tts.Spoken);
        Assert.Contains("rate-limited", output.ToString());
    }

    [Fact]
    public async Task OversizedInputIsReportedWithoutSendingItToModel()
    {
        var stt = new ScriptedRecognition(Session.Saying(new string('a', 4001)), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();
        var output = new StringWriter();
        await Create(stt, tts, model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(model.Requests);
        Assert.Contains("Please try a shorter question", tts.Spoken[0]);
        Assert.Contains("input limit", output.ToString());
    }

    [Fact]
    public async Task UnexpectedPlaybackCancellationDoesNotStopConversation()
    {
        var stt = new ScriptedRecognition(Session.Saying("Hello"), Session.Saying("exit"));
        var calls = 0;
        var tts = new FakeSynthesis
        {
            Handler = (_, _) => ++calls == 1 ? throw new OperationCanceledException() : Task.CompletedTask
        };
        var output = new StringWriter();
        await Create(stt, tts, new FakeModel(), output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, stt.SessionsStarted);
        Assert.Contains("Playback was canceled", output.ToString());
    }

    [Fact]
    public async Task TtsFailureKeepsDisplayedAnswerAndHistoryAndContinues()
    {
        var stt = new ScriptedRecognition(
            Session.Saying("First"), Session.Saying("Second"), Session.Saying("goodbye"));
        var model = new FakeModel();
        var calls = 0;
        var tts = new FakeSynthesis
        {
            Handler = (_, _) => ++calls == 1
                ? throw new SpeechServiceException("Audio output failed.")
                : Task.CompletedTask
        };
        var output = new StringWriter();
        await Create(stt, tts, model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(4, model.Requests[1].Count);
        Assert.Equal(3, tts.Spoken.Count);
        Assert.Contains("Snoopy: Model answer.", output.ToString());
        Assert.Contains("Audio output failed.", output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientSpeechFailureOrUnexpectedCancellationRecovers(bool canceled)
    {
        var stt = new ScriptedRecognition(
            new Session
            {
                Failure = canceled
                    ? new OperationCanceledException()
                    : new SpeechServiceException("Temporary microphone failure.")
            },
            Session.Saying("Hello"),
            Session.Saying("exit"));
        var model = new FakeModel();
        var output = new StringWriter();
        await Create(stt, new FakeSynthesis(), model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(model.Requests);
        Assert.Equal(3, stt.SessionsStarted);
        Assert.False(stt.MicrophoneOpen);
        Assert.Contains(canceled ? "Recognition was canceled" : "Temporary microphone failure", output.ToString());
    }

    [Fact]
    public async Task SessionEndingWithoutTextRetriesRatherThanCallingModel()
    {
        var stt = new ScriptedRecognition(new Session(), Session.Saying("exit"));
        var model = new FakeModel();
        var output = new StringWriter();
        await Create(stt, new FakeSynthesis(), model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(model.Requests);
        Assert.Equal(2, stt.SessionsStarted);
        Assert.Contains("without a final result", output.ToString());
    }

    [Fact]
    public async Task FarewellPlaybackFailureStillExits()
    {
        var stt = new ScriptedRecognition(Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis { Handler = (_, _) => throw new SpeechServiceException("Speaker unavailable.") };
        await Create(stt, tts, model).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(model.Requests);
        Assert.Single(tts.Spoken);
        Assert.Equal(1, stt.SessionsStarted);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("Remember that I prefer .NET.")]
    [InlineData("What do you remember?")]
    [InlineData("Clear my memories.")]
    public async Task NextMicrophoneSessionWaitsForPlaybackToComplete(string input)
    {
        var stt = new ScriptedRecognition(Session.Saying(input), Session.Saying("exit"));
        var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishPlayback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var tts = new FakeSynthesis
        {
            Handler = async (_, token) =>
            {
                if (++calls == 1)
                {
                    playing.TrySetResult();
                    await finishPlayback.Task.WaitAsync(token);
                }
            }
        };
        var run = Create(stt, tts, new FakeModel()).RunAsync();
        try
        {
            await playing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, stt.SessionsStarted);
            Assert.False(stt.MicrophoneOpen);
        }
        finally
        {
            finishPlayback.TrySetResult();
        }
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, stt.SessionsStarted);
    }

    [Theory]
    [InlineData("stt")]
    [InlineData("llm")]
    [InlineData("tts")]
    public async Task CtrlCCancelsEveryPhaseWithoutStartingAnotherSession(string phase)
    {
        using var shutdown = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stt = new ScriptedRecognition(phase == "stt"
            ? new Session { WaitForCancellation = true, Started = started }
            : Session.Saying("Hello"));
        var model = new FakeModel
        {
            Handler = async (_, token) =>
            {
                if (phase == "llm")
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                return "Answer";
            }
        };
        var tts = new FakeSynthesis
        {
            Handler = async (_, token) =>
            {
                if (phase == "tts")
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
            }
        };
        var run = Create(stt, tts, model).RunAsync(shutdown.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(stt.MicrophoneOpen);
        Assert.Equal(1, stt.SessionsStarted);
        if (phase != "tts")
        {
            Assert.Empty(tts.Spoken);
        }
    }

    [Theory]
    [InlineData("reset conversation")]
    [InlineData("New conversation.")]
    public async Task ResetClearsContextWithoutSendingCommandOrConfirmationToModel(string command)
    {
        var stt = new ScriptedRecognition(
            Session.Saying("My name is Akhil."), Session.Saying("What is my name?"),
            Session.Saying(command), Session.Saying("What is my name?"), Session.Saying("goodbye"));
        var model = new FakeModel
        {
            Handler = (messages, _) =>
            {
                Assert.False(stt.MicrophoneOpen);
                var nameKnown = messages.Any(message => message.Text == "My name is Akhil.");
                return Task.FromResult(nameKnown ? "Your name is Akhil." : "I don't know your name.");
            }
        };
        var tts = new FakeSynthesis
        {
            Handler = (_, _) =>
            {
                Assert.False(stt.MicrophoneOpen);
                return Task.CompletedTask;
            }
        };
        var output = new StringWriter();
        await Create(stt, tts, model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, model.Requests.Count);
        Assert.Equal(4, model.Requests[1].Count);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, SnoopyOptions.DefaultSystemPrompt),
            new(ConversationRole.User, "What is my name?")
        ], model.Requests[2]);
        Assert.Equal(
        [
            "Your name is Akhil.", "Your name is Akhil.", VoiceConversationApplication.ConversationReset,
            "I don't know your name.", VoiceConversationApplication.Goodbye
        ], tts.Spoken);
        Assert.Contains($"Snoopy: {VoiceConversationApplication.ConversationReset}", output.ToString());
    }

    [Fact]
    public async Task RepeatedResetsOfEmptyConversationDoNotCallModel()
    {
        var stt = new ScriptedRecognition(
            Session.Saying("reset conversation"), Session.Saying("new conversation"), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();
        await Create(stt, tts, model).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(model.Requests);
        Assert.Equal(
        [
            VoiceConversationApplication.ConversationReset,
            VoiceConversationApplication.ConversationReset,
            VoiceConversationApplication.Goodbye
        ], tts.Spoken);
    }

    [Fact]
    public async Task FailedResetConfirmationPlaybackDoesNotRestoreHistoryOrStopListening()
    {
        var stt = new ScriptedRecognition(
            Session.Saying("My name is Akhil."), Session.Saying("reset conversation"),
            Session.Saying("What is my name?"), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis
        {
            Handler = (text, _) => text == VoiceConversationApplication.ConversationReset
                ? throw new SpeechServiceException("Speaker unavailable.")
                : Task.CompletedTask
        };
        var output = new StringWriter();
        await Create(stt, tts, model, output).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(2, model.Requests[1].Count);
        Assert.Equal("What is my name?", model.Requests[1][^1].Text);
        Assert.Contains("Speaker unavailable.", output.ToString());
        Assert.Equal(4, stt.SessionsStarted);
    }

    [Fact]
    public async Task CancellationDuringClearDoesNotAnnounceSuccessOrContinueListening()
    {
        using var shutdown = new CancellationTokenSource();
        var stt = new ScriptedRecognition(Session.Saying("reset conversation"));
        var tts = new FakeSynthesis();
        var conversation = new BlockingClearConversation();
        var output = new StringWriter();
        var run = new VoiceConversationApplication(stt, tts, conversation,
            new MemoryCommandParser(), new MemoryService(new InMemoryMemoryStore()), output).RunAsync(shutdown.Token);
        await conversation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(tts.Spoken);
        Assert.DoesNotContain(VoiceConversationApplication.ConversationReset, output.ToString());
        Assert.Equal(1, stt.SessionsStarted);
        Assert.False(stt.MicrophoneOpen);
    }

    [Fact]
    public async Task RememberRecallForgetRecallUsesLocalMemoryAndExistingSpeechPipeline()
    {
        const string content = "I prefer .NET for backend development.";
        var store = new InMemoryMemoryStore();
        var stt = new ScriptedRecognition(
            Session.Saying($"Remember that {content}"),
            Session.Saying("What do you remember about me?"),
            Session.Saying($"Forget that {content}"),
            Session.Saying("What do you remember about me?"),
            Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis
        {
            Handler = async (text, _) =>
            {
                Assert.False(stt.MicrophoneOpen);
                if (text.StartsWith("Got it.", StringComparison.Ordinal))
                {
                    var memory = Assert.Single(await store.GetAllAsync());
                    Assert.Equal(content, memory.Content);
                    Assert.Equal(MemoryCategory.Preference, memory.Category);
                }
            }
        };
        var output = new StringWriter();

        await Create(stt, tts, model, output, store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Empty(await store.GetAllAsync());
        Assert.Equal(
        [
            $"Got it. I've saved this memory: \"{content}\"",
            $"Here's what I remember: \"{content}\"",
            "Okay, I've forgotten that.",
            "I don't have any saved memories yet.",
            VoiceConversationApplication.Goodbye
        ], tts.Spoken);
        Assert.Contains($"You: Remember that {content}", output.ToString());
        Assert.Contains("Snoopy: Okay, I've forgotten that.", output.ToString());
        Assert.Equal(5, stt.SessionsStarted);
    }

    [Theory]
    [InlineData("Forget everything you remember.")]
    [InlineData("Clear my memories.")]
    public async Task RememberClearRecallSpeaksConfirmationAndEmptyMemoryList(string clearCommand)
    {
        var store = new InMemoryMemoryStore();
        var stt = new ScriptedRecognition(
            Session.Saying("Remember that I prefer C#."), Session.Saying(clearCommand),
            Session.Saying("What do you remember?"), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Empty(await store.GetAllAsync());
        Assert.Equal(
        [
            "Got it. I've saved this memory: \"I prefer C#.\"",
            "Okay. I've cleared your saved memories.",
            "I don't have any saved memories yet.",
            VoiceConversationApplication.Goodbye
        ], tts.Spoken);
    }

    [Theory]
    [InlineData("forget that I prefer .NET.")]
    [InlineData("please forget that I like C#")]
    [InlineData("forget my favorite programming language")]
    public async Task ForgetWithoutFullTextMatchReportsNotFoundAndPreservesMemories(string command)
    {
        var store = new InMemoryMemoryStore();
        var memory = new Memory("My favorite programming language is C#.");
        await store.AddAsync(memory);
        var stt = new ScriptedRecognition(Session.Saying(command), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Equal(memory, Assert.Single(await store.GetAllAsync()));
        Assert.Equal(["I couldn't find a memory matching that.", VoiceConversationApplication.Goodbye], tts.Spoken);
    }

    [Theory]
    [InlineData("What do you remember?")]
    [InlineData("What do you remember about me?")]
    public async Task RecallSpeaksAllOriginalMemoriesInCreationOrderWithoutModel(string command)
    {
        var store = new InMemoryMemoryStore();
        var created = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var first = new Memory(Guid.NewGuid(), "I prefer .NET.", MemoryCategory.Preference, 0, created);
        var second = new Memory(Guid.NewGuid(), "My name is Akhil.", MemoryCategory.Fact, 0, created.AddMinutes(1));
        await store.AddAsync(second);
        await store.AddAsync(first);
        var stt = new ScriptedRecognition(Session.Saying(command), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Equal(
            ["Here's what I remember: \"I prefer .NET.\"; \"My name is Akhil.\"", VoiceConversationApplication.Goodbye],
            tts.Spoken);
        Assert.Equal(2, (await store.GetAllAsync()).Count);
    }

    [Theory]
    [InlineData("What do you remember?")]
    [InlineData("What do you remember about me?")]
    public async Task RecallEmptyMemoryListDoesNotAskModelToInventMemories(string command)
    {
        var stt = new ScriptedRecognition(Session.Saying(command), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Equal(["I don't have any saved memories yet.", VoiceConversationApplication.Goodbye], tts.Spoken);
    }

    [Theory]
    [InlineData("Clear my memories.")]
    [InlineData("Forget everything you remember.")]
    public async Task ClearingEmptyMemoriesRepeatedlyIsSafeAndLocal(string command)
    {
        var stt = new ScriptedRecognition(Session.Saying(command), Session.Saying(command), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Equal(
        [
            "Okay. I've cleared your saved memories.",
            "Okay. I've cleared your saved memories.",
            VoiceConversationApplication.Goodbye
        ], tts.Spoken);
    }

    [Fact]
    public async Task NormalPreferenceStillCallsModelAndDoesNotCreateMemory()
    {
        const string input = "I prefer .NET for backend development.";
        var store = new InMemoryMemoryStore();
        var stt = new ScriptedRecognition(
            Session.Saying(input), Session.Saying("What do you remember?"), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(input, Assert.Single(model.Requests)[^1].Text);
        Assert.Empty(await store.GetAllAsync());
        Assert.Equal(
            ["Model answer.", "I don't have any saved memories yet.", VoiceConversationApplication.Goodbye], tts.Spoken);
    }

    [Fact]
    public async Task MemoryContentCommandsAndConfirmationsAreNotAddedToModelContext()
    {
        var store = new InMemoryMemoryStore();
        var stt = new ScriptedRecognition(
            Session.Saying("Hello"),
            Session.Saying("Remember that I prefer .NET."),
            Session.Saying("How are you?"),
            Session.Saying("What do you remember?"),
            Session.Saying("Forget that I prefer .NET."),
            Session.Saying("Clear my memories."),
            Session.Saying("Follow up"),
            Session.Saying("exit"));
        var model = new FakeModel();

        await Create(stt, new FakeSynthesis(), model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, model.Requests.Count);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, SnoopyOptions.DefaultSystemPrompt),
            new(ConversationRole.User, "Hello"),
            new(ConversationRole.Assistant, "Model answer."),
            new(ConversationRole.User, "How are you?")
        ], model.Requests[1]);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, SnoopyOptions.DefaultSystemPrompt),
            new(ConversationRole.User, "Hello"),
            new(ConversationRole.Assistant, "Model answer."),
            new(ConversationRole.User, "How are you?"),
            new(ConversationRole.Assistant, "Model answer."),
            new(ConversationRole.User, "Follow up")
        ], model.Requests[2]);
        Assert.Empty(await store.GetAllAsync());
    }

    [Theory]
    [InlineData("reset conversation")]
    [InlineData("new conversation")]
    public async Task ConversationResetClearsHistoryButPreservesSavedMemories(string command)
    {
        var store = new InMemoryMemoryStore();
        var stt = new ScriptedRecognition(
            Session.Saying("Remember that I prefer .NET."),
            Session.Saying("My name is Akhil."),
            Session.Saying(command),
            Session.Saying("What is my name?"),
            Session.Saying("What do you remember?"),
            Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("I prefer .NET.", Assert.Single(await store.GetAllAsync()).Content);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, SnoopyOptions.DefaultSystemPrompt),
            new(ConversationRole.User, "What is my name?")
        ], model.Requests[1]);
        Assert.Equal(VoiceConversationApplication.ConversationReset, tts.Spoken[2]);
        Assert.Equal("Here's what I remember: \"I prefer .NET.\"", tts.Spoken[4]);
    }

    [Theory]
    [InlineData("Clear my memories.")]
    [InlineData("Forget everything you remember.")]
    public async Task MemoryClearPreservesConversationHistory(string command)
    {
        var store = new InMemoryMemoryStore();
        var stt = new ScriptedRecognition(
            Session.Saying("My name is Akhil."),
            Session.Saying("Remember that I prefer .NET."),
            Session.Saying(command),
            Session.Saying("What is my name?"),
            Session.Saying("What do you remember?"),
            Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(await store.GetAllAsync());
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal<ConversationMessage>(
        [
            new(ConversationRole.System, SnoopyOptions.DefaultSystemPrompt),
            new(ConversationRole.User, "My name is Akhil."),
            new(ConversationRole.Assistant, "Model answer."),
            new(ConversationRole.User, "What is my name?")
        ], model.Requests[1]);
        Assert.Equal("Okay. I've cleared your saved memories.", tts.Spoken[2]);
        Assert.Equal("I don't have any saved memories yet.", tts.Spoken[4]);
    }

    [Theory]
    [InlineData("remember", true)]
    [InlineData("please remember that", true)]
    [InlineData("Remember that .?!", true)]
    [InlineData("forget", false)]
    [InlineData("Please forget that.", false)]
    [InlineData("Forget that .!?", false)]
    public async Task IncompleteMemoryCommandsAskForContentWithoutChangingMemoryOrCallingModel(
        string command, bool remember)
    {
        var store = new InMemoryMemoryStore();
        var original = new Memory("Keep this memory.");
        await store.AddAsync(original);
        var stt = new ScriptedRecognition(Session.Saying(command), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();

        await Create(stt, tts, model, memoryStore: store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Equal(original, Assert.Single(await store.GetAllAsync()));
        Assert.Equal(
        [
            remember ? "Please tell me what you'd like me to remember." : "Please tell me what you'd like me to forget.",
            VoiceConversationApplication.Goodbye
        ], tts.Spoken);
    }

    [Theory]
    [InlineData("Remember that I prefer .NET.")]
    [InlineData("Forget that I prefer .NET.")]
    [InlineData("What do you remember?")]
    [InlineData("Clear my memories.")]
    public async Task CancellationDuringMemoryOperationDoesNotAnnounceSuccessOrContinueListening(string command)
    {
        using var shutdown = new CancellationTokenSource();
        var stt = new ScriptedRecognition(Session.Saying(command));
        var model = new FakeModel();
        var tts = new FakeSynthesis();
        var service = new ControlledMemoryService();
        var output = new StringWriter();

        var run = Create(stt, tts, model, output, memoryService: service).RunAsync(shutdown.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(shutdown.Token, service.Token);
        Assert.Empty(model.Requests);
        Assert.Empty(tts.Spoken);
        Assert.DoesNotContain("Snoopy:", output.ToString());
        Assert.Equal(1, stt.SessionsStarted);
        Assert.False(stt.MicrophoneOpen);
    }

    [Theory]
    [InlineData("Remember that I prefer .NET.")]
    [InlineData("Forget that I prefer .NET.")]
    [InlineData("What do you remember?")]
    [InlineData("Clear my memories.")]
    public async Task MemoryFailureIsNotTurnedIntoASuccessConfirmationOrModelRequest(string command)
    {
        var stt = new ScriptedRecognition(Session.Saying(command));
        var model = new FakeModel();
        var tts = new FakeSynthesis();
        var failure = new InvalidOperationException("Memory storage failed.");
        var service = new ControlledMemoryService { Failure = failure };
        var output = new StringWriter();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(stt, tts, model, output, memoryService: service).RunAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(failure, thrown);
        Assert.Empty(model.Requests);
        Assert.Empty(tts.Spoken);
        Assert.DoesNotContain("Snoopy:", output.ToString());
        Assert.Equal(1, stt.SessionsStarted);
        Assert.False(stt.MicrophoneOpen);
    }

    [Theory]
    [InlineData("Remember that I prefer .NET.", true)]
    [InlineData("Forget that I prefer .NET.", false)]
    [InlineData("Clear my memories.", false)]
    public async Task FailedMemoryConfirmationPlaybackDoesNotUndoSavedChange(string command, bool memoryRemains)
    {
        var store = new InMemoryMemoryStore();
        if (!memoryRemains)
        {
            await store.AddAsync(new Memory("I prefer .NET."));
        }
        var stt = new ScriptedRecognition(
            Session.Saying(command), Session.Saying("What do you remember?"), Session.Saying("exit"));
        var model = new FakeModel();
        var calls = 0;
        var tts = new FakeSynthesis
        {
            Handler = (_, _) => ++calls == 1
                ? throw new SpeechServiceException("Speaker unavailable.")
                : Task.CompletedTask
        };
        var output = new StringWriter();

        await Create(stt, tts, model, output, store).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(model.Requests);
        Assert.Equal(memoryRemains ? 1 : 0, (await store.GetAllAsync()).Count);
        Assert.Equal(
            memoryRemains ? "Here's what I remember: \"I prefer .NET.\"" : "I don't have any saved memories yet.",
            tts.Spoken[1]);
        Assert.Contains("Speaker unavailable.", output.ToString());
        Assert.Equal(3, stt.SessionsStarted);
    }

    [Theory]
    [InlineData("Remember that I prefer .NET.", MemoryStorageOperation.Save)]
    [InlineData("Forget that I prefer .NET.", MemoryStorageOperation.Remove)]
    [InlineData("What do you remember?", MemoryStorageOperation.Read)]
    [InlineData("Clear my memories.", MemoryStorageOperation.Clear)]
    public async Task PersistentStorageFailureSpeaksSafeErrorAndNormalConversationContinues(
        string command, MemoryStorageOperation operation)
    {
        var stt = new ScriptedRecognition(
            Session.Saying(command), Session.Saying("Hello"), Session.Saying("exit"));
        var model = new FakeModel();
        var tts = new FakeSynthesis();
        var service = new ControlledMemoryService { Failure = new MemoryStorageException(operation) };
        var output = new StringWriter();

        await Create(stt, tts, model, output, memoryService: service).RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            [VoiceConversationApplication.MemoryUnavailable, "Model answer.", VoiceConversationApplication.Goodbye],
            tts.Spoken);
        Assert.Equal("Hello", Assert.Single(model.Requests)[^1].Text);
        Assert.Equal(2, model.Requests[0].Count);
        Assert.Contains("[ERROR] Unable to complete the persistent memory", output.ToString());
        Assert.DoesNotContain("Memory saved successfully", output.ToString());
        Assert.DoesNotContain("Saved memories cleared.", output.ToString());
        Assert.Equal(3, stt.SessionsStarted);
    }

    [Fact]
    public async Task NaturalNameQuestionAfterRestartReceivesTheExplicitlySavedMemory()
    {
        var options = new SnoopyOptions { EnableMemoryContext = true };
        var store = new InMemoryMemoryStore();
        var firstModel = new FakeModel();
        var firstSpeech = new ScriptedRecognition(
            Session.Saying("Remember my name as Akhil."),
            Session.Saying("What do you remember?"),
            Session.Saying("exit"));

        await Create(firstSpeech, new FakeSynthesis(), firstModel, memoryStore: store, options: options)
            .RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(firstModel.Requests);
        var nextModel = new FakeModel
        {
            Handler = (messages, _) =>
            {
                Assert.Contains("my name as Akhil.", messages[^2].Text);
                Assert.Equal(ConversationRole.User, messages[^2].Role);
                return Task.FromResult("Your name is Akhil.");
            }
        };
        var nextSpeech = new ScriptedRecognition(Session.Saying("Do you remember my name?"), Session.Saying("exit"));
        var synthesis = new FakeSynthesis();

        await Create(nextSpeech, synthesis, nextModel, memoryStore: store, options: options)
            .RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Your name is Akhil.", synthesis.Spoken[0]);
        Assert.Equal(3, Assert.Single(nextModel.Requests).Count);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task MemoryContextOutageWarnsThroughSpeechWhileNormalChatContinues()
    {
        var speech = new ScriptedRecognition(Session.Saying("Hello"), Session.Saying("exit"));
        var synthesis = new FakeSynthesis();
        var model = new FakeModel();
        var output = new StringWriter();
        var memories = new ControlledMemoryService { Failure = new MemoryStorageException(MemoryStorageOperation.Read) };

        await Create(speech, synthesis, model, output, memoryService: memories,
                options: new SnoopyOptions { EnableMemoryContext = true })
            .RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal($"{MemoryContextProvider.UnavailableWarning} Model answer.", synthesis.Spoken[0]);
        Assert.Contains("lookup failed", Assert.Single(model.Requests)[^2].Text);
        Assert.Contains("[WARNING]", output.ToString());
        Assert.Equal(VoiceConversationApplication.Goodbye, synthesis.Spoken[1]);
    }

    private static VoiceConversationApplication Create(
        ScriptedRecognition stt, FakeSynthesis tts, FakeModel model, TextWriter? output = null,
        IMemoryStore? memoryStore = null, IMemoryService? memoryService = null, SnoopyOptions? options = null)
    {
        options ??= new SnoopyOptions();
        var memories = memoryService ?? new MemoryService(memoryStore ?? new InMemoryMemoryStore());
        var writer = output ?? new StringWriter();
        return new(stt, tts, new ConversationService(model, options, new InMemoryConversationHistory(options),
                new MemoryContextProvider(memories, options, writer)),
            new MemoryCommandParser(), memories, writer);
    }

    private sealed class ControlledMemoryService : IMemoryService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; init; }

        public Task<Memory> RememberAsync(string content, CancellationToken cancellationToken = default) =>
            WaitAsync<Memory>(cancellationToken);

        public Task<bool> ForgetAsync(string content, CancellationToken cancellationToken = default) =>
            WaitAsync<bool>(cancellationToken);

        public Task<IReadOnlyList<Memory>> GetMemoriesAsync(CancellationToken cancellationToken = default) =>
            WaitAsync<IReadOnlyList<Memory>>(cancellationToken);

        public Task ClearMemoriesAsync(CancellationToken cancellationToken = default) =>
            WaitAsync<bool>(cancellationToken);

        private async Task<T> WaitAsync<T>(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            if (Failure is not null)
            {
                throw Failure;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("A blocked memory operation must not complete without cancellation.");
        }
    }

    private sealed class BlockingClearConversation : IConversationService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> ReplyAsync(string userText, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A reset must not request a model reply.");

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class Session
    {
        public SpeechRecognitionUpdate[] Updates { get; init; } = [];
        public Exception? Failure { get; init; }
        public bool WaitForCancellation { get; init; }
        public TaskCompletionSource? Started { get; init; }

        public static Session Saying(string text) => new() { Updates = [new(RecognitionKind.Final, text)] };
    }

    private sealed class ScriptedRecognition(params Session[] sessions) : ISpeechToTextService
    {
        private readonly Queue<Session> remaining = new(sessions);
        public List<string> Events { get; init; } = [];
        public bool MicrophoneOpen { get; private set; }
        public int SessionsStarted { get; private set; }

        public async IAsyncEnumerable<SpeechRecognitionUpdate> RecognizeAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.False(MicrophoneOpen);
            Assert.NotEmpty(remaining);
            var session = remaining.Dequeue();
            SessionsStarted++;
            MicrophoneOpen = true;
            Events.Add("microphone-open");
            try
            {
                session.Started?.TrySetResult();
                if (session.Failure is not null)
                {
                    throw session.Failure;
                }
                foreach (var update in session.Updates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return update;
                }
                if (session.WaitForCancellation)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            }
            finally
            {
                MicrophoneOpen = false;
                Events.Add("microphone-closed");
            }
        }
    }

    private sealed class FakeModel : ILanguageModelClient
    {
        public List<IReadOnlyList<ConversationMessage>> Requests { get; } = [];
        public Func<IReadOnlyList<ConversationMessage>, CancellationToken, Task<string>> Handler { get; init; } =
            (_, _) => Task.FromResult("Model answer.");

        public Task<string> CompleteAsync(
            IReadOnlyList<ConversationMessage> messages, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToArray());
            return Handler(messages, cancellationToken);
        }
    }

    private sealed class FakeSynthesis : ITextToSpeechService
    {
        public List<string> Spoken { get; } = [];
        public Func<string, CancellationToken, Task> Handler { get; init; } = (_, _) => Task.CompletedTask;

        public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Spoken.Add(text);
            return Handler(text, cancellationToken);
        }
    }
}
