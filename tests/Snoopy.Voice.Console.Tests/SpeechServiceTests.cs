using Microsoft.CognitiveServices.Speech;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console.Tests;

public sealed class SpeechServiceTests
{
    private static SpeechOptions ValidOptions => new() { SubscriptionKey = "test-placeholder" };

    [Fact]
    public void ServicesConstructWithoutLoadingAudioOrContactingAzure()
    {
        Assert.IsAssignableFrom<ISpeechToTextService>(new SpeechToTextService(ValidOptions));
        Assert.IsAssignableFrom<ITextToSpeechService>(new TextToSpeechService(ValidOptions));
    }

    [Fact]
    public void ConstructorsRejectNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new SpeechToTextService(null!));
        Assert.Throws<ArgumentNullException>(() => new TextToSpeechService(null!));
    }

    [Fact]
    public void ConstructorsValidateOptions()
    {
        Assert.Throws<ArgumentException>(() => new SpeechToTextService(new SpeechOptions()));
        Assert.Throws<ArgumentException>(() => new TextToSpeechService(new SpeechOptions()));
    }

    [Fact]
    public async Task PreCanceledRecognitionDoesNotOpenMicrophone()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var results = new SpeechToTextService(ValidOptions)
            .RecognizeAsync(cancellation.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => results.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task PreCanceledSynthesisDoesNotOpenSpeaker()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TextToSpeechService(ValidOptions).SpeakAsync("Hello Snoopy", cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptySynthesisIsRejectedBeforeOpeningSpeaker(string? text)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new TextToSpeechService(ValidOptions).SpeakAsync(text!));
    }

    [Theory]
    [InlineData(CancellationErrorCode.AuthenticationFailure, "authentication failed")]
    [InlineData(CancellationErrorCode.ConnectionFailure, "connection failed")]
    [InlineData(CancellationErrorCode.BadRequest, "rejected")]
    [InlineData(CancellationErrorCode.Forbidden, "denied")]
    [InlineData(CancellationErrorCode.TooManyRequests, "rate-limited")]
    [InlineData(CancellationErrorCode.ServiceTimeout, "timed out")]
    [InlineData(CancellationErrorCode.ServiceUnavailable, "unavailable")]
    [InlineData(CancellationErrorCode.NoError, "canceled")]
    public void CancellationHasActionableSafeMessage(CancellationErrorCode code, string expected)
    {
        Assert.Contains(expected, SpeechSdkErrors.FromCancellation(code).Message);
    }

    [Fact]
    public void NativeErrorDetailsAreNotExposed()
    {
        var exception = Assert.Throws<SpeechServiceException>(() =>
            SpeechSdkErrors.Run<int>(() =>
                throw new InvalidOperationException("test-placeholder sensitive SDK diagnostic"), "STT"));
        Assert.Contains("microphone", exception.Message);
        Assert.DoesNotContain("test-placeholder", exception.ToString());
    }

    [Fact]
    public async Task AudioOutputFailureIsActionableAndSafe()
    {
        var exception = await Assert.ThrowsAsync<SpeechServiceException>(() =>
            SpeechSdkErrors.RunAsync(() =>
                throw new InvalidOperationException("test-placeholder sensitive SDK diagnostic"), "TTS"));
        Assert.Contains("speaker", exception.Message);
        Assert.DoesNotContain("test-placeholder", exception.ToString());
    }
}
