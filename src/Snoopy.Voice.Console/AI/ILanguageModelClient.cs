namespace Snoopy.Voice.Console.AI;

public enum ConversationRole
{
    System,
    User,
    Assistant
}

public sealed record ConversationMessage(ConversationRole Role, string Text);

public interface ILanguageModelClient
{
    Task<string> CompleteAsync(
        IReadOnlyList<ConversationMessage> messages, CancellationToken cancellationToken = default);
}
