namespace Snoopy.Voice.Console.AI;

public interface IMemoryContextProvider
{
    Task<MemoryContext> GetContextAsync(string userText, CancellationToken cancellationToken = default);
}
