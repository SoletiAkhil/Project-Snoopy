namespace Snoopy.Voice.Console.Services;

public interface ITextToSpeechService
{
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}
