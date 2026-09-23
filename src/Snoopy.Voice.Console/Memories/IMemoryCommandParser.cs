namespace Snoopy.Voice.Console.Memories;

public interface IMemoryCommandParser
{
    MemoryCommandResult Parse(string? text);
}
