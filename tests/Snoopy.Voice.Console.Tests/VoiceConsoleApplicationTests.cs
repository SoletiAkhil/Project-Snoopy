using System.Runtime.CompilerServices;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console.Tests;

public sealed class VoiceConsoleApplicationTests
{
    [Theory]
    [InlineData("4")]
    [InlineData(null)]
    public async Task ExitAndEndOfInputDoNotUseSpeech(string? input)
    {
        var stt = new FakeRecognition([]);
        var tts = new FakeSynthesis();
        var output = new StringWriter();
        await new VoiceConsoleApplication(stt, tts, _ => Task.FromResult(input), output).RunAsync();
        Assert.False(stt.Started);
        Assert.Empty(tts.Spoken);
        Assert.Contains("SNOOPY VOICE V0.1", output.ToString());
    }

    [Fact]
    public async Task TtsSpeaksTextAndReturnsToMenu()
    {
        var tts = new FakeSynthesis();
        await new VoiceConsoleApplication(new FakeRecognition([]), tts,
            ReadSequence("2", "Hello Snoopy", "4"), new StringWriter()).RunAsync();
        Assert.Equal(["Hello Snoopy"], tts.Spoken);
    }

    [Fact]
    public async Task EmptyTextAndInvalidMenuSelectionAreReported()
    {
        var output = new StringWriter();
        var tts = new FakeSynthesis();
        await new VoiceConsoleApplication(new FakeRecognition([]), tts,
            ReadSequence("invalid", "2", " ", "4"), output).RunAsync();
        Assert.Empty(tts.Spoken);
        Assert.Contains("Select 1, 2, 3 or 4", output.ToString());
        Assert.Contains("non-empty text", output.ToString());
    }

    [Fact]
    public async Task EchoUsesOnlyFirstFinalAndClosesMicrophoneBeforeSpeaking()
    {
        var stt = new FakeRecognition(
        [
            new(RecognitionKind.Partial, "Hel"),
            new(RecognitionKind.Final, "Hello Snoopy"),
            new(RecognitionKind.Final, "Do not repeat")
        ]);
        var tts = new FakeSynthesis { BeforeSpeak = () => Assert.True(stt.Disposed) };
        var output = new StringWriter();
        var input = new ListeningInput("3");
        await new VoiceConsoleApplication(stt, tts, input.ReadAsync, output).RunAsync();
        Assert.Equal(["Hello Snoopy"], tts.Spoken);
        Assert.Contains("[STT] Recognized: Hello Snoopy", output.ToString());
        Assert.DoesNotContain("Do not repeat", output.ToString());
        Assert.DoesNotContain("Recognized: Hel\n", output.ToString());
        Assert.True(input.PendingReadCanceled);
    }

    [Theory]
    [InlineData(RecognitionKind.NoMatch)]
    [InlineData(RecognitionKind.Final)]
    public async Task EchoDoesNotSynthesizeEmptyRecognition(RecognitionKind kind)
    {
        var stt = new FakeRecognition([new(kind, string.Empty)]);
        var tts = new FakeSynthesis();
        var output = new StringWriter();
        var input = new ListeningInput("3");
        await new VoiceConsoleApplication(stt, tts, input.ReadAsync, output).RunAsync();
        Assert.Empty(tts.Spoken);
        Assert.True(stt.Disposed);
        Assert.Contains("No speech could be recognized", output.ToString());
    }

    [Fact]
    public async Task ContinuousModeDisplaysMultipleFinalsWithoutSpeaking()
    {
        var stt = new FakeRecognition(
        [
            new(RecognitionKind.Partial, "partial-only"),
            new(RecognitionKind.Final, "First"),
            new(RecognitionKind.NoMatch, ""),
            new(RecognitionKind.Final, "Second")
        ]);
        var tts = new FakeSynthesis();
        var output = new StringWriter();
        var input = new ListeningInput("1");
        await new VoiceConsoleApplication(stt, tts, input.ReadAsync, output).RunAsync();
        Assert.Contains("Recognized: First", output.ToString());
        Assert.Contains("Recognized: Second", output.ToString());
        Assert.DoesNotContain("partial-only", output.ToString());
        Assert.Empty(tts.Spoken);
        Assert.True(stt.Disposed);
    }

    [Fact]
    public async Task EnterStopsRecognitionAndReturnsToMenu()
    {
        var stt = new FakeRecognition([]) { WaitForCancellation = true };
        var calls = 0;
        Task<string?> Read(CancellationToken _) => ++calls switch
        {
            1 => Task.FromResult<string?>("1"),
            2 => StopAfterStart(),
            _ => Task.FromResult<string?>("4")
        };
        async Task<string?> StopAfterStart()
        {
            await stt.StartedSignal.Task;
            return string.Empty;
        }

        var output = new StringWriter();
        await new VoiceConsoleApplication(stt, new FakeSynthesis(), Read, output)
            .RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(stt.Disposed);
        Assert.Contains("Recognition stopped", output.ToString());
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task RecognitionFailureCancelsPendingInputAndReturnsToMenu()
    {
        var stt = new FakeRecognition([])
        {
            Failure = new SpeechServiceException("Azure Speech authentication failed.")
        };
        var input = new ListeningInput("1");
        var output = new StringWriter();
        await new VoiceConsoleApplication(stt, new FakeSynthesis(), input.ReadAsync, output).RunAsync();
        Assert.Contains("[ERROR] Azure Speech authentication failed.", output.ToString());
        Assert.True(input.PendingReadCanceled);
        Assert.True(stt.Disposed);
    }

    [Fact]
    public async Task SynthesisFailureIsReportedAndReturnsToMenu()
    {
        var output = new StringWriter();
        var tts = new FakeSynthesis { Failure = new SpeechServiceException("Audio output failed.") };
        await new VoiceConsoleApplication(new FakeRecognition([]), tts,
            ReadSequence("2", "Hello", "4"), output).RunAsync();
        Assert.Contains("[ERROR] Audio output failed.", output.ToString());
        Assert.DoesNotContain("Playback complete", output.ToString());
    }

    [Fact]
    public async Task ShutdownDuringRecognitionDisposesSessionAndCancelsInput()
    {
        using var shutdown = new CancellationTokenSource();
        var stt = new FakeRecognition([]) { WaitForCancellation = true };
        var input = new ListeningInput("1");
        var run = new VoiceConsoleApplication(stt, new FakeSynthesis(), input.ReadAsync, new StringWriter())
            .RunAsync(shutdown.Token);
        await stt.StartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stt.Disposed);
        Assert.True(input.PendingReadCanceled);
    }

    private static Func<CancellationToken, Task<string?>> ReadSequence(params string[] values)
    {
        var lines = new Queue<string>(values);
        return _ => Task.FromResult<string?>(lines.Count > 0 ? lines.Dequeue() : null);
    }

    private sealed class ListeningInput(string option)
    {
        private int calls;
        public bool PendingReadCanceled { get; private set; }

        public async Task<string?> ReadAsync(CancellationToken token)
        {
            if (++calls == 1)
            {
                return option;
            }
            if (calls > 2)
            {
                return "4";
            }
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return null;
            }
            finally
            {
                PendingReadCanceled = token.IsCancellationRequested;
            }
        }
    }

    private sealed class FakeRecognition(SpeechRecognitionUpdate[] updates) : ISpeechToTextService
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public bool WaitForCancellation { get; init; }
        public SpeechServiceException? Failure { get; init; }
        public TaskCompletionSource StartedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<SpeechRecognitionUpdate> RecognizeAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started = true;
            StartedSignal.TrySetResult();
            try
            {
                if (Failure is not null)
                {
                    throw Failure;
                }
                foreach (var update in updates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return update;
                }
                if (WaitForCancellation)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            }
            finally
            {
                Disposed = true;
            }
        }
    }

    private sealed class FakeSynthesis : ITextToSpeechService
    {
        public List<string> Spoken { get; } = [];
        public Action? BeforeSpeak { get; init; }
        public SpeechServiceException? Failure { get; init; }

        public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeSpeak?.Invoke();
            if (Failure is not null)
            {
                throw Failure;
            }
            Spoken.Add(text);
            return Task.CompletedTask;
        }
    }
}
