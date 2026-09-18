namespace Snoopy.Voice.Console.AI;

public sealed record ConversationTurn(string User, string Assistant);

public interface IConversationHistory
{
    // Returns a non-mutating snapshot, reserving space for a pending user message.
    Task<IReadOnlyList<ConversationTurn>> GetTurnsAsync(
        int pendingInputCharacters = 0, CancellationToken cancellationToken = default);

    Task AddTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
