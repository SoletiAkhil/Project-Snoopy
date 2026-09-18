using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console;

public sealed class VoiceConsoleApplication(
    ISpeechToTextService speechToText,
    ITextToSpeechService textToSpeech,
    Func<CancellationToken, Task<string?>> readLine,
    TextWriter output)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            output.WriteLine();
            output.WriteLine("============================================");
            output.WriteLine("             SNOOPY VOICE V0.1");
            output.WriteLine("============================================");
            output.WriteLine("1. Test Speech-to-Text");
            output.WriteLine("2. Test Text-to-Speech");
            output.WriteLine("3. Test Speech-to-Text -> Text-to-Speech");
            output.WriteLine("4. Exit");
            output.Write("Select an option: ");
            var selection = await readLine(cancellationToken);
            if (selection is null || selection.Trim() == "4")
            {
                return;
            }

            try
            {
                switch (selection.Trim())
                {
                    case "1":
                        await ListenAsync(singleUtterance: false, cancellationToken);
                        break;
                    case "2":
                        output.Write("Enter text: ");
                        var text = await readLine(cancellationToken);
                        if (text is null)
                        {
                            return;
                        }
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            output.WriteLine("[INFO] Enter non-empty text to synthesize.");
                            break;
                        }
                        await SpeakAsync(text, cancellationToken);
                        break;
                    case "3":
                        var recognized = await ListenAsync(singleUtterance: true, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(recognized))
                        {
                            await SpeakAsync(recognized, cancellationToken);
                        }
                        break;
                    default:
                        output.WriteLine("[INFO] Select 1, 2, 3 or 4.");
                        break;
                }
            }
            catch (SpeechServiceException exception)
            {
                output.WriteLine($"[ERROR] {exception.Message}");
            }
        }
    }

    private async Task<string?> ListenAsync(bool singleUtterance, CancellationToken cancellationToken)
    {
        output.WriteLine(singleUtterance ? "[INFO] SNOOPY END-TO-END TEST" : "[INFO] SNOOPY STT TEST");
        output.WriteLine("[STT] Listening... Speak in English (India).");
        output.WriteLine("[INFO] Press Enter to stop. Ctrl+C exits the application.");
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopInput = StopOnEnterAsync(session);
        try
        {
            await foreach (var update in speechToText.RecognizeAsync(session.Token))
            {
                session.Token.ThrowIfCancellationRequested();
                switch (update.Kind)
                {
                    case RecognitionKind.Final when !string.IsNullOrWhiteSpace(update.Text):
                        output.WriteLine($"[STT] Recognized: {update.Text}");
                        if (singleUtterance)
                        {
                            // Leaving the iterator stops/disposes the microphone before TTS starts.
                            return update.Text;
                        }
                        break;
                    case RecognitionKind.NoMatch:
                    case RecognitionKind.Final:
                        output.WriteLine("[STT] No speech could be recognized. Check the microphone and try again.");
                        if (singleUtterance)
                        {
                            return null;
                        }
                        break;
                }
            }
            output.WriteLine("[INFO] Recognition session ended.");
            return null;
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("[INFO] Recognition stopped.");
            return null;
        }
        finally
        {
            await session.CancelAsync();
            try
            {
                await stopInput;
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
                // Cancel the pending input read so it cannot consume the next menu selection.
            }
        }
    }

    private async Task StopOnEnterAsync(CancellationTokenSource session)
    {
        try
        {
            await readLine(session.Token);
        }
        finally
        {
            await session.CancelAsync();
        }
    }

    private async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        output.WriteLine("[TTS] Snoopy is speaking...");
        await textToSpeech.SpeakAsync(text, cancellationToken);
        output.WriteLine("[TTS] Playback complete.");
    }
}
