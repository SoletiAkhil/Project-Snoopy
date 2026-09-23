using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.AI;

public sealed class ConversationService : IConversationService
{
    private readonly ILanguageModelClient _languageModel;
    private readonly SnoopyOptions _options;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly IConversationHistory _history;
    private readonly IMemoryContextProvider _memoryContext;

    public ConversationService(
        ILanguageModelClient languageModel, SnoopyOptions options, IConversationHistory history,
        IMemoryContextProvider memoryContext)
    {
        ArgumentNullException.ThrowIfNull(languageModel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(memoryContext);
        options.Validate();
        _languageModel = languageModel;
        _options = options;
        _history = history;
        _memoryContext = memoryContext;
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
            var memoryContext = await _memoryContext.GetContextAsync(userText, cancellationToken).ConfigureAwait(false);
            var retained = await _history.GetTurnsAsync(
                userText.Length + memoryContext.CharacterCount, cancellationToken).ConfigureAwait(false);
            var messages = new List<ConversationMessage>(retained.Count * 2 + 3)
            {
                new(ConversationRole.System, _options.SystemPrompt + memoryContext.Instructions)
            };
            foreach (var turn in retained)
            {
                messages.Add(new(ConversationRole.User, turn.User));
                messages.Add(new(ConversationRole.Assistant, turn.Assistant));
            }
            if (memoryContext.Text.Length > 0)
            {
                messages.Add(new(ConversationRole.User, memoryContext.Text));
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

            if (memoryContext.Warning is not null)
            {
                reply = $"{memoryContext.Warning} {reply}";
            }
            await _history.AddTurnAsync(new(userText, reply), cancellationToken).ConfigureAwait(false);
            return reply;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _history.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessionLock.Release();
        }
    }
}
