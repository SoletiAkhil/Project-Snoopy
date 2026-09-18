using System.Runtime.CompilerServices;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
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

    [Fact]
    public async Task NextMicrophoneSessionWaitsForPlaybackToComplete()
    {
        var stt = new ScriptedRecognition(Session.Saying("Hello"), Session.Saying("exit"));
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

    private static VoiceConversationApplication Create(
        ScriptedRecognition stt, FakeSynthesis tts, FakeModel model, TextWriter? output = null) =>
        new(stt, tts, new ConversationService(model, new SnoopyOptions()), output ?? new StringWriter());

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
