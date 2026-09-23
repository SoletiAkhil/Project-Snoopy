namespace Snoopy.Voice.Console.Memories;

public sealed class MemoryCommandParser : IMemoryCommandParser
{
    public MemoryCommandResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(MemoryCommandType.None);
        }

        var input = text.Trim();
        var (word, remainder) = ReadLeadingWord(input);
        if (word.Equals("please", StringComparison.OrdinalIgnoreCase))
        {
            input = remainder;
            (word, remainder) = ReadLeadingWord(input);
        }

        var command = MemoryText.NormalizeForComparison(input);
        if (command.Equals("forget everything you remember", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("clear my memories", StringComparison.OrdinalIgnoreCase))
        {
            return new(MemoryCommandType.Clear);
        }
        if (command.Equals("what do you remember about me", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("what do you remember", StringComparison.OrdinalIgnoreCase))
        {
            return new(MemoryCommandType.Recall);
        }

        var commandType = word.ToLowerInvariant() switch
        {
            "remember" => MemoryCommandType.Remember,
            "forget" => MemoryCommandType.Forget,
            _ => MemoryCommandType.None
        };
        if (commandType == MemoryCommandType.None)
        {
            return new(MemoryCommandType.None);
        }

        var (nextWord, content) = ReadLeadingWord(remainder);
        return new(commandType, nextWord.Equals("that", StringComparison.OrdinalIgnoreCase) ? content : remainder);
    }

    private static (string Word, string Remainder) ReadLeadingWord(string text)
    {
        var parts = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (parts[0].TrimEnd('.', '!', '?', ','), string.Empty),
            _ => (parts[0].TrimEnd('.', '!', '?', ','), parts[1].Trim())
        };
    }
}
