using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console;

public sealed class VoiceConversationApplication(
    ISpeechToTextService speechToText,
    ITextToSpeechService textToSpeech,
    IConversationService conversation,
    TextWriter output)
{
    public const string Goodbye = "Goodbye! Talk to you later.";
    public const string ConversationReset = "Sure. I've started a new conversation.";
    public const string ModelUnavailable =
        "I'm having trouble reaching my language model right now. Please try again.";
    private static readonly TimeSpan RecognitionRetryDelay = TimeSpan.FromSeconds(1);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        output.WriteLine("========================================");
        output.WriteLine("          Snoopy Voice Assistant");
        output.WriteLine("========================================");
        output.WriteLine("[INFO] Speech: configured. LLM: configured. Connections are checked when used.");
        output.WriteLine("Speak naturally in English (India). Pause at the end of each turn.");
        output.WriteLine("Say exit, quit, goodbye or stop (optionally with Snoopy) to finish.");
        output.WriteLine("Say reset conversation or new conversation to start fresh.");
        output.WriteLine("Ctrl+C cancels the current operation and exits.");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine();
            output.WriteLine("Snoopy is listening...");
            string? userText;
            try
            {
                userText = await ListenForTurnAsync(cancellationToken);
            }
            catch (SpeechServiceException exception)
            {
                output.WriteLine($"[ERROR] {exception.Message}");
                output.WriteLine("[INFO] Retrying microphone recognition in one second. Ctrl+C exits.");
                await Task.Delay(RecognitionRetryDelay, cancellationToken);
                continue;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                output.WriteLine("[STT] Recognition was canceled. Listening again in one second.");
                await Task.Delay(RecognitionRetryDelay, cancellationToken);
                continue;
            }

            if (userText is null)
            {
                output.WriteLine("[STT] Recognition ended without a final result. Listening again in one second.");
                await Task.Delay(RecognitionRetryDelay, cancellationToken);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine($"You: {userText}");
            if (ExitCommands.IsExit(userText))
            {
                await DisplayAndSpeakAsync(Goodbye, cancellationToken);
                return;
            }
            if (ConversationCommands.IsReset(userText))
            {
                await conversation.ClearAsync(cancellationToken);
                await DisplayAndSpeakAsync(ConversationReset, cancellationToken);
                continue;
            }

            string response;
            try
            {
                response = await conversation.ReplyAsync(userText, cancellationToken);
            }
            catch (LanguageModelException exception)
            {
                output.WriteLine($"[ERROR] {exception.Message}");
                response = exception.Failure == LanguageModelFailure.InputTooLong
                    ? "That question is too long. Please try a shorter question."
                    : ModelUnavailable;
            }
            await DisplayAndSpeakAsync(response, cancellationToken);
        }
    }

    private async Task<string?> ListenForTurnAsync(CancellationToken cancellationToken)
    {
        await foreach (var update in speechToText.RecognizeAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update.Kind == RecognitionKind.Final && !string.IsNullOrWhiteSpace(update.Text))
            {
                // Disposing this iterator releases the microphone before any LLM/TTS work.
                return update.Text.Trim();
            }
            if (update.Kind is RecognitionKind.NoMatch or RecognitionKind.Final)
            {
                output.WriteLine("[STT] I didn't catch that. Check the default microphone and try again.");
            }
        }
        return null;
    }

    private async Task DisplayAndSpeakAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.WriteLine($"Snoopy: {text}");
        try
        {
            await textToSpeech.SpeakAsync(text, cancellationToken);
        }
        catch (SpeechServiceException exception)
        {
            output.WriteLine($"[ERROR] {exception.Message}");
            output.WriteLine("[INFO] The response is still available above. Check the default audio output.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("[TTS] Playback was canceled. The response is still available above.");
        }
    }
}
