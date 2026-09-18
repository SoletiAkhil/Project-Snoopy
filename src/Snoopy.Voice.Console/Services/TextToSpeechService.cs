using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Services;

public sealed class TextToSpeechService : ITextToSpeechService
{
    private readonly SpeechOptions options;

    public TextToSpeechService(SpeechOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();
        return SpeechSdkErrors.RunAsync(async () =>
        {
            var config = SpeechConfig.FromSubscription(options.SubscriptionKey, options.Region);
            config.SpeechSynthesisVoiceName = options.VoiceName;
            using var audio = AudioConfig.FromDefaultSpeakerOutput();
            using var synthesizer = new SpeechSynthesizer(config, audio);
            var speech = synthesizer.SpeakTextAsync(text);
            SpeechSynthesisResult result;
            try
            {
                result = await speech.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await synthesizer.StopSpeakingAsync().ConfigureAwait(false);
                }
                finally
                {
                    // Observe completion and release the result before disposing the native synthesizer.
                    using var canceledResult = await speech.ConfigureAwait(false);
                }
                throw;
            }

            using (result)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Reason == ResultReason.Canceled)
                {
                    throw SpeechSdkErrors.FromCancellation(
                        SpeechSynthesisCancellationDetails.FromResult(result).ErrorCode);
                }

                if (result.Reason != ResultReason.SynthesizingAudioCompleted)
                {
                    throw new SpeechServiceException("Azure Speech did not complete audio playback.");
                }
            }
        }, "TTS");
    }
}
