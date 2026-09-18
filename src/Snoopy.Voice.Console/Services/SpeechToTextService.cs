using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Services;

public sealed class SpeechToTextService : ISpeechToTextService
{
    private readonly SpeechOptions options;

    public SpeechToTextService(SpeechOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
    }

    public async IAsyncEnumerable<SpeechRecognitionUpdate> RecognizeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = SpeechSdkErrors.Run(
            () => SpeechConfig.FromSubscription(options.SubscriptionKey, options.Region), "STT");
        config.SpeechRecognitionLanguage = "en-IN";
        using var audio = SpeechSdkErrors.Run(AudioConfig.FromDefaultMicrophoneInput, "STT");
        using var recognizer = SpeechSdkErrors.Run(() => new SpeechRecognizer(config, audio), "STT");
        var updates = Channel.CreateUnbounded<SpeechRecognitionUpdate>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

        EventHandler<SpeechRecognitionEventArgs> recognizing = (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Result.Text))
            {
                updates.Writer.TryWrite(new(RecognitionKind.Partial, args.Result.Text));
            }
        };
        EventHandler<SpeechRecognitionEventArgs> recognized = (_, args) =>
        {
            if (args.Result.Reason == ResultReason.RecognizedSpeech &&
                !string.IsNullOrWhiteSpace(args.Result.Text))
            {
                updates.Writer.TryWrite(new(RecognitionKind.Final, args.Result.Text));
            }
            else if (args.Result.Reason is ResultReason.NoMatch or ResultReason.RecognizedSpeech)
            {
                updates.Writer.TryWrite(new(RecognitionKind.NoMatch, string.Empty));
            }
        };
        EventHandler<SpeechRecognitionCanceledEventArgs> canceled = (_, args) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                updates.Writer.TryComplete();
            }
            else
            {
                updates.Writer.TryComplete(SpeechSdkErrors.FromCancellation(args.ErrorCode));
            }
        };
        EventHandler<SessionEventArgs> stopped = (_, _) => updates.Writer.TryComplete();

        recognizer.Recognizing += recognizing;
        recognizer.Recognized += recognized;
        recognizer.Canceled += canceled;
        recognizer.SessionStopped += stopped;
        var started = false;
        try
        {
            await SpeechSdkErrors.RunAsync(recognizer.StartContinuousRecognitionAsync, "STT");
            started = true;
            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            try
            {
                if (started)
                {
                    await SpeechSdkErrors.RunAsync(recognizer.StopContinuousRecognitionAsync, "STT");
                }
            }
            finally
            {
                recognizer.Recognizing -= recognizing;
                recognizer.Recognized -= recognized;
                recognizer.Canceled -= canceled;
                recognizer.SessionStopped -= stopped;
                updates.Writer.TryComplete();
            }
        }
    }
}
