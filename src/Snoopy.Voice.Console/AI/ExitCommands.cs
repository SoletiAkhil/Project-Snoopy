namespace Snoopy.Voice.Console.AI;

public static class ExitCommands
{
    public static bool IsExit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var words = new string(text.Select(character => char.IsPunctuation(character) ? ' ' : character)
                .ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return words.Length switch
        {
            1 => IsCommand(words[0]),
            2 => (words[0].Equals("Snoopy", StringComparison.OrdinalIgnoreCase) && IsCommand(words[1])) ||
                 (IsCommand(words[0]) && words[1].Equals("Snoopy", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static bool IsCommand(string text) =>
        text.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("goodbye", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("stop", StringComparison.OrdinalIgnoreCase);
}
