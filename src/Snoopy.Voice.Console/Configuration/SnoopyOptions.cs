namespace Snoopy.Voice.Console.Configuration;

public sealed class SnoopyOptions
{
    public const string DefaultSystemPrompt =
        "You are Snoopy, a helpful voice assistant. Speak naturally and clearly. " +
        "Keep normal responses conversational and concise because they will be spoken aloud. " +
        "Avoid unnecessary markdown, tables, long lists and code blocks unless explicitly requested. " +
        "When the user asks for detail, provide the requested detail rather than aggressively shortening it.";

    public string SystemPrompt { get; init; } = DefaultSystemPrompt;
    public int MaxHistoryTurns { get; init; } = 12;
    public int MaxHistoryCharacters { get; init; } = 24000;
    public int MaxInputCharacters { get; init; } = 4000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SystemPrompt))
        {
            throw new ArgumentException("Snoopy SystemPrompt must not be empty.");
        }
        if (MaxHistoryTurns is < 1 or > 100)
        {
            throw new ArgumentException("Snoopy MaxHistoryTurns must be between 1 and 100.");
        }
        if (MaxHistoryCharacters is < 1024 or > 1000000)
        {
            throw new ArgumentException("Snoopy MaxHistoryCharacters must be between 1024 and 1000000.");
        }
        if (MaxInputCharacters < 1 || MaxInputCharacters > MaxHistoryCharacters - SystemPrompt.Length)
        {
            throw new ArgumentException(
                "Snoopy MaxInputCharacters must be positive and fit alongside SystemPrompt " +
                "within MaxHistoryCharacters.");
        }
    }
}
