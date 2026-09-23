namespace Snoopy.Voice.Console.Configuration;

public enum InstructionRole
{
    System,
    Developer
}

public sealed class AzureOpenAIOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string DeploymentName { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 45;
    public int MaxOutputTokens { get; init; } = 1024;
    public InstructionRole InstructionRole { get; init; } = InstructionRole.System;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Endpoint) ||
            string.IsNullOrWhiteSpace(ApiKey) ||
            string.IsNullOrWhiteSpace(DeploymentName))
        {
            throw new ArgumentException(
                "Azure OpenAI configuration is missing.\n\nSet AzureOpenAI:Endpoint, AzureOpenAI:ApiKey " +
                "and AzureOpenAI:DeploymentName in the local appsettings.json.\n\n" +
                "Use --speech-tests to run the independent Speech tests without an LLM.");
        }

        _ = GetApiEndpoint();
        if (RequestTimeoutSeconds is < 1 or > 300)
        {
            throw new ArgumentException("AzureOpenAI RequestTimeoutSeconds must be between 1 and 300.");
        }
        if (MaxOutputTokens is < 1 or > 32768)
        {
            throw new ArgumentException("AzureOpenAI MaxOutputTokens must be between 1 and 32768.");
        }
        if (!Enum.IsDefined(InstructionRole))
        {
            throw new ArgumentException("AzureOpenAI InstructionRole must be System or Developer.");
        }
    }

    public Uri GetApiEndpoint()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !(uri.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)) ||
            (uri.AbsolutePath != "/" && uri.AbsolutePath.TrimEnd('/') != "/openai/v1"))
        {
            throw new ArgumentException(
                "Azure OpenAI Endpoint must be an HTTPS Azure resource URL ending in " +
                ".openai.azure.com or .services.ai.azure.com, with no credentials, query or fragment. " +
                "Use the resource root or /openai/v1/, not a deployment-specific request URL.");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/openai/v1/");
    }
}
