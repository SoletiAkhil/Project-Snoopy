using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.AI;

public sealed class InMemoryConversationHistory : IConversationHistory
{
    private readonly SnoopyOptions _options;
    private readonly object _sync = new();
    private List<ConversationTurn> _turns = [];

    public InMemoryConversationHistory(SnoopyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public Task<IReadOnlyList<ConversationTurn>> GetTurnsAsync(
        int pendingInputCharacters = 0, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pendingInputCharacters);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var snapshot = new List<ConversationTurn>(_turns);
            TrimHistory(snapshot, pendingInputCharacters);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ConversationTurn>>(snapshot.AsReadOnly());
        }
    }

    public Task AddTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.User);
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.Assistant);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var retained = new List<ConversationTurn>(_turns) { turn };
            // Adding a complete pair also evicts any history excluded by its pending-input snapshot.
            TrimHistory(retained);
            cancellationToken.ThrowIfCancellationRequested();
            _turns = retained;
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _turns = [];
        }
        return Task.CompletedTask;
    }

    private void TrimHistory(List<ConversationTurn> history, int pendingInputCharacters = 0)
    {
        var characters = (long)_options.SystemPrompt.Length + pendingInputCharacters;
        foreach (var turn in history)
        {
            characters += (long)turn.User.Length + turn.Assistant.Length;
        }

        var removeCount = 0;
        while (removeCount < history.Count &&
               (history.Count - removeCount > _options.MaxHistoryTurns ||
                characters > _options.MaxHistoryCharacters))
        {
            var oldest = history[removeCount++];
            characters -= (long)oldest.User.Length + oldest.Assistant.Length;
        }
        if (removeCount > 0)
        {
            history.RemoveRange(0, removeCount);
        }
    }
}
