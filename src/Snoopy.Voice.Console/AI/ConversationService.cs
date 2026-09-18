using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.AI;

public sealed class ConversationService : IConversationService
{
    private readonly ILanguageModelClient _languageModel;
    private readonly SnoopyOptions _options;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private List<Turn> _history = [];

    public ConversationService(ILanguageModelClient languageModel, SnoopyOptions options)
    {
        ArgumentNullException.ThrowIfNull(languageModel);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _languageModel = languageModel;
        _options = options;
    }

    public async Task<string> ReplyAsync(string userText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            throw new ArgumentException("A user message must not be empty.", nameof(userText));
        }

        userText = userText.Trim();
        if (userText.Length > _options.MaxInputCharacters)
        {
            throw new LanguageModelException(LanguageModelFailure.InputTooLong);
        }

        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var retained = new List<Turn>(_history);
            TrimHistory(retained, userText.Length);
            var messages = new List<ConversationMessage>(retained.Count * 2 + 2)
            {
                new(ConversationRole.System, _options.SystemPrompt)
            };
            foreach (var turn in retained)
            {
                messages.Add(new(ConversationRole.User, turn.User));
                messages.Add(new(ConversationRole.Assistant, turn.Assistant));
            }
            messages.Add(new(ConversationRole.User, userText));

            cancellationToken.ThrowIfCancellationRequested();
            var reply = await _languageModel.CompleteAsync(messages.AsReadOnly(), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new LanguageModelException(LanguageModelFailure.InvalidResponse);
            }

            retained.Add(new(userText, reply));
            // Return oversized replies intact, but drop even the newest whole pair if it cannot fit.
            TrimHistory(retained);
            cancellationToken.ThrowIfCancellationRequested();
            _history = retained;
            return reply;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private void TrimHistory(List<Turn> history, int pendingInputCharacters = 0)
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

    private sealed record Turn(string User, string Assistant);
}
