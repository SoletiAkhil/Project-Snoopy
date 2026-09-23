namespace Snoopy.Voice.Console.AI;

public sealed record MemoryContext(string Instructions, string Text, string? Warning = null)
{
    public static MemoryContext Empty { get; } = new(string.Empty, string.Empty);

    public int CharacterCount => Instructions.Length + Text.Length;
}
