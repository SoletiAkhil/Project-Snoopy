using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Tests;

public sealed class AzureOpenAILanguageModelClientTests
{
    private const string ValidResponse = """
        {
          "id": "chatcmpl-test",
          "object": "chat.completion",
          "created": 1,
          "model": "configured-deployment",
          "choices": [
            { "index": 0, "finish_reason": "stop", "message": { "role": "assistant", "content": "Hello Akhil." } }
          ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 3, "total_tokens": 13 }
        }
        """;

    [Theory]
    [InlineData(InstructionRole.System, "system", "first-deployment")]
    [InlineData(InstructionRole.Developer, "developer", "future-deployment")]
    public async Task UsesAzureV1AndConfiguredDeploymentWithOrderedHistory(
        InstructionRole instructionRole, string expectedRole, string deployment)
    {
        var options = Options(instructionRole, deployment);
        using var fixture = new ClientFixture(options, (_, _) => Task.FromResult(Response(200, ValidResponse)));
        var reply = await fixture.Client.CompleteAsync(
        [
            new(ConversationRole.System, "Be concise."),
            new(ConversationRole.User, "My name is Akhil."),
            new(ConversationRole.Assistant, "Nice to meet you, Akhil."),
            new(ConversationRole.User, "What is my name?")
        ]);
        Assert.Equal("Hello Akhil.", reply);
        Assert.Equal("https://unit-test.openai.azure.com/openai/v1/chat/completions", fixture.Handler.RequestUri);
        using var request = JsonDocument.Parse(Assert.IsType<string>(fixture.Handler.RequestBody));
        Assert.Equal(deployment, request.RootElement.GetProperty("model").GetString());
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal(expectedRole, messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
        Assert.Equal("user", messages[3].GetProperty("role").GetString());
        Assert.Equal(1024, request.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.False(request.RootElement.TryGetProperty("temperature", out _));
    }

    [Theory]
    [InlineData(401, LanguageModelFailure.Authentication)]
    [InlineData(403, LanguageModelFailure.Authentication)]
    [InlineData(404, LanguageModelFailure.DeploymentNotFound)]
    [InlineData(408, LanguageModelFailure.Timeout)]
    [InlineData(429, LanguageModelFailure.RateLimited)]
    [InlineData(500, LanguageModelFailure.Service)]
    [InlineData(503, LanguageModelFailure.Service)]
    [InlineData(504, LanguageModelFailure.Timeout)]
    [InlineData(400, LanguageModelFailure.RequestRejected)]
    public async Task HttpErrorsBecomeSafeFailures(int status, LanguageModelFailure expected)
    {
        using var fixture = new ClientFixture(Options(), (_, _) => Task.FromResult(Response(status,
            """{"error":{"code":"error","message":"do-not-print-this-key-or-response"}}""")));
        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => fixture.Client.CompleteAsync(Messages()));
        Assert.Equal(expected, exception.Failure);
        Assert.DoesNotContain("do-not-print", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ContextLimitHasSpecificActionableFailure()
    {
        using var fixture = new ClientFixture(Options(), (_, _) => Task.FromResult(Response(400,
            """{"error":{"code":"context_length_exceeded","message":"do-not-print"}}""")));
        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => fixture.Client.CompleteAsync(Messages()));
        Assert.Equal(LanguageModelFailure.ContextLimit, exception.Failure);
        Assert.DoesNotContain("do-not-print", exception.ToString());
    }

    [Theory]
    [InlineData("this is not JSON")]
    [InlineData("{}")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":"stop"}]}""")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":"stop","message":null}]}""")]
    [InlineData("""{"choices":[{"index":0,"message":{"role":"assistant","content":"Hello"}}]}""")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":"stop","message":{"role":"user","content":"Hello"}}]}""")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":""}}]}""")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":" "}}]}""")]
    public async Task MalformedOrEmptyResponseDoesNotEscapeAsRawException(string payload)
    {
        using var fixture = new ClientFixture(Options(), (_, _) => Task.FromResult(Response(200, payload)));
        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => fixture.Client.CompleteAsync(Messages()));
        Assert.Equal(LanguageModelFailure.InvalidResponse, exception.Failure);
    }

    [Theory]
    [InlineData("length", LanguageModelFailure.OutputLimit)]
    [InlineData("content_filter", LanguageModelFailure.RequestRejected)]
    [InlineData("tool_calls", LanguageModelFailure.InvalidResponse)]
    public async Task UnsupportedCompletionIsNotSpokenAsASuccess(string finishReason, LanguageModelFailure expected)
    {
        using var fixture = new ClientFixture(Options(), (_, _) => Task.FromResult(Response(200,
            ValidResponse.Replace("\"stop\"", $"\"{finishReason}\"", StringComparison.Ordinal))));
        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => fixture.Client.CompleteAsync(Messages()));
        Assert.Equal(expected, exception.Failure);
    }

    [Fact]
    public async Task ModelRefusalCanBeSpokenNaturally()
    {
        using var fixture = new ClientFixture(Options(), (_, _) => Task.FromResult(Response(200,
            """{"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":null,"refusal":"I cannot help with that request."}}]}""")));
        Assert.Equal("I cannot help with that request.", await fixture.Client.CompleteAsync(Messages()));
    }

    [Fact]
    public async Task NetworkErrorDoesNotExposeSdkDetails()
    {
        using var fixture = new ClientFixture(Options(),
            (_, _) => throw new HttpRequestException("do-not-print-this-key"));
        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => fixture.Client.CompleteAsync(Messages()));
        Assert.Equal(LanguageModelFailure.Network, exception.Failure);
        Assert.DoesNotContain("do-not-print", exception.ToString());
    }

    [Fact]
    public async Task RequestHasTotalTimeout()
    {
        using var fixture = new ClientFixture(Options(timeoutSeconds: 1), async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Response(200, ValidResponse);
        });
        var exception = await Assert.ThrowsAsync<LanguageModelException>(() =>
            fixture.Client.CompleteAsync(Messages()).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(LanguageModelFailure.Timeout, exception.Failure);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedToModelFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new ClientFixture(Options(), async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Response(200, ValidResponse);
        });
        var request = fixture.Client.CompleteAsync(Messages(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PreCancellationDoesNotSendRequest()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var fixture = new ClientFixture(Options(), (_, _) => Task.FromResult(Response(200, ValidResponse)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Client.CompleteAsync(Messages(), cancellation.Token));
        Assert.Equal(0, fixture.Handler.Calls);
    }

    private static ConversationMessage[] Messages() =>
    [
        new(ConversationRole.System, "Be concise."),
        new(ConversationRole.User, "Hello")
    ];

    private static AzureOpenAIOptions Options(
        InstructionRole role = InstructionRole.System, string deployment = "configured-deployment",
        int timeoutSeconds = 45) => new()
    {
        Endpoint = "https://unit-test.openai.azure.com/",
        ApiKey = "not-a-real-api-key",
        DeploymentName = deployment,
        InstructionRole = role,
        RequestTimeoutSeconds = timeoutSeconds
    };

    private static HttpResponseMessage Response(int status, string content) => new((HttpStatusCode)status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class ClientFixture : IDisposable
    {
        private readonly HttpClient http;
        public StubHandler Handler { get; }
        public AzureOpenAILanguageModelClient Client { get; }

        public ClientFixture(
            AzureOpenAIOptions options,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            Handler = new StubHandler(send);
            http = new HttpClient(Handler);
            var sdk = new ChatClient(options.DeploymentName, new ApiKeyCredential(options.ApiKey),
                new OpenAIClientOptions
                {
                    Endpoint = options.GetApiEndpoint(),
                    Transport = new HttpClientPipelineTransport(http),
                    RetryPolicy = new ClientRetryPolicy(0)
                });
            Client = new AzureOpenAILanguageModelClient(options, sdk);
        }

        public void Dispose() => http.Dispose();
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri?.AbsoluteUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await send(request, cancellationToken);
        }
    }
}
