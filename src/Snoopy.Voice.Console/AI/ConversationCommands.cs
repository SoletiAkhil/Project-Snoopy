namespace Snoopy.Voice.Console.AI;

public static class ConversationCommands
{
    public static bool IsReset(string? text)
    {
        var words = GetWords(text);
        return words.Length == 2 &&
               (words[0].Equals("reset", StringComparison.OrdinalIgnoreCase) ||
                words[0].Equals("new", StringComparison.OrdinalIgnoreCase)) &&
               words[1].Equals("conversation", StringComparison.OrdinalIgnoreCase);
    }

    internal static string[] GetWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : new string(text.Select(character => char.IsPunctuation(character) ? ' ' : character).ToArray())
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
