using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.AI;

public sealed class AzureOpenAILanguageModelClient : ILanguageModelClient
{
    private readonly AzureOpenAIOptions options;
    private readonly ChatClient client;

    public AzureOpenAILanguageModelClient(AzureOpenAIOptions options) : this(options, CreateClient(options))
    {
    }

    internal AzureOpenAILanguageModelClient(AzureOpenAIOptions options, ChatClient client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        options.Validate();
        this.options = options;
        this.client = client;
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<ConversationMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        var chat = messages.Select<ConversationMessage, ChatMessage>(message => message.Role switch
        {
#pragma warning disable OPENAI001 // Developer-role messages are an explicit optional SDK feature.
            ConversationRole.System when options.InstructionRole == InstructionRole.Developer =>
                new DeveloperChatMessage(message.Text),
#pragma warning restore OPENAI001
            ConversationRole.System => new SystemChatMessage(message.Text),
            ConversationRole.User => new UserChatMessage(message.Text),
            ConversationRole.Assistant => new AssistantChatMessage(message.Text),
            _ => throw new ArgumentException("Unsupported conversation message role.", nameof(messages))
        }).ToArray();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        try
        {
            var completion = await client.CompleteChatAsync(chat, new ChatCompletionOptions
            {
                MaxOutputTokenCount = options.MaxOutputTokens,
                StoredOutputEnabled = false
            }, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            ValidateResponseShape(completion.GetRawResponse().Content);
            return ReadResponse(completion.Value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanguageModelException(LanguageModelFailure.Timeout);
        }
        catch (ClientResultException exception)
        {
            throw new LanguageModelException(exception.Status switch
            {
                401 or 403 => LanguageModelFailure.Authentication,
                404 => LanguageModelFailure.DeploymentNotFound,
                408 or 504 => LanguageModelFailure.Timeout,
                429 => LanguageModelFailure.RateLimited,
                400 when IsContextLimit(exception) => LanguageModelFailure.ContextLimit,
                >= 400 and < 500 => LanguageModelFailure.RequestRejected,
                >= 500 => LanguageModelFailure.Service,
                _ => LanguageModelFailure.Network
            });
        }
        catch (HttpRequestException)
        {
            throw new LanguageModelException(LanguageModelFailure.Network);
        }
        catch (JsonException)
        {
            throw new LanguageModelException(LanguageModelFailure.InvalidResponse);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            // SDK deserialization may reject malformed response fields without a JsonException.
            throw new LanguageModelException(LanguageModelFailure.InvalidResponse);
        }
    }

    private static ChatClient CreateClient(AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return new ChatClient(options.DeploymentName, new ApiKeyCredential(options.ApiKey), new OpenAIClientOptions
        {
            Endpoint = options.GetApiEndpoint(),
            NetworkTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 2)
        });
    }

    private static string ReadResponse(ChatCompletion completion)
    {
        if (completion.FinishReason == ChatFinishReason.Length)
        {
            throw new LanguageModelException(LanguageModelFailure.OutputLimit);
        }
        if (completion.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw new LanguageModelException(LanguageModelFailure.RequestRejected);
        }
        if (completion.FinishReason != ChatFinishReason.Stop || completion.ToolCalls.Count > 0)
        {
            throw new LanguageModelException(LanguageModelFailure.InvalidResponse);
        }

        var text = string.Join("\n", completion.Content
            .Where(part => part.Kind == ChatMessageContentPartKind.Text)
            .Select(part => part.Text)).Trim();
        if (text.Length == 0)
        {
            text = completion.Refusal?.Trim() ?? string.Empty;
        }
        if (text.Length == 0)
        {
            throw new LanguageModelException(LanguageModelFailure.InvalidResponse);
        }
        return text;
    }

    private static void ValidateResponseShape(BinaryData content)
    {
        // SDK convenience properties index the first choice without checking that it exists.
        using var document = JsonDocument.Parse(content.ToMemory());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            choices[0].ValueKind != JsonValueKind.Object ||
            !choices[0].TryGetProperty("finish_reason", out var finish) || finish.ValueKind != JsonValueKind.String ||
            !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String ||
            role.GetString() != "assistant")
        {
            throw new LanguageModelException(LanguageModelFailure.InvalidResponse);
        }
    }

    private static bool IsContextLimit(ClientResultException exception)
    {
        var content = exception.GetRawResponse()?.Content;
        if (content is null)
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(content.ToMemory());
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("error", out var error) &&
                   error.ValueKind == JsonValueKind.Object &&
                   error.TryGetProperty("code", out var code) &&
                   code.ValueKind == JsonValueKind.String &&
                   code.GetString() == "context_length_exceeded";
        }
        catch (JsonException)
        {
            // An unparseable error body still surfaces as a failed request, never as success.
            return false;
        }
    }
}
