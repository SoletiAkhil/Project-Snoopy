namespace Snoopy.Voice.Console.Services;

public enum RecognitionKind
{
    Partial,
    Final,
    NoMatch
}

public sealed record SpeechRecognitionUpdate(RecognitionKind Kind, string Text);

public interface ISpeechToTextService
{
    IAsyncEnumerable<SpeechRecognitionUpdate> RecognizeAsync(CancellationToken cancellationToken = default);
}
