namespace Snoopy.Voice.Console.AI;

public interface IConversationService
{
    Task<string> ReplyAsync(string userText, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
