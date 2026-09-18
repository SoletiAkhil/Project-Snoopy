namespace Snoopy.Voice.Console.AI;

public enum LanguageModelFailure
{
    Authentication,
    DeploymentNotFound,
    RequestRejected,
    ContextLimit,
    Timeout,
    RateLimited,
    Network,
    Service,
    InvalidResponse,
    OutputLimit,
    InputTooLong
}

public sealed class LanguageModelException(LanguageModelFailure failure) : Exception(GetMessage(failure))
{
    public LanguageModelFailure Failure { get; } = failure;

    private static string GetMessage(LanguageModelFailure failure) => failure switch
    {
        LanguageModelFailure.Authentication =>
            "Azure OpenAI authentication/access failed. Check the API key and resource permissions locally.",
        LanguageModelFailure.DeploymentNotFound =>
            "Azure OpenAI deployment was not found. Check the endpoint and deployment name.",
        LanguageModelFailure.RequestRejected =>
            "Azure OpenAI rejected the request. Check model/API compatibility or rephrase the request.",
        LanguageModelFailure.ContextLimit =>
            "Azure OpenAI context limit was exceeded. Reduce the configured history/input limits.",
        LanguageModelFailure.Timeout =>
            "Azure OpenAI request timed out. Check connectivity or increase the request timeout.",
        LanguageModelFailure.RateLimited =>
            "Azure OpenAI is rate-limited. Wait before trying again and check the deployment quota.",
        LanguageModelFailure.Network =>
            "Could not reach Azure OpenAI. Check the endpoint, network, proxy and firewall.",
        LanguageModelFailure.Service =>
            "Azure OpenAI is temporarily unavailable. Please try again later.",
        LanguageModelFailure.InvalidResponse =>
            "Azure OpenAI returned an empty or unsupported response. Please try again.",
        LanguageModelFailure.OutputLimit =>
            "Azure OpenAI reached the response token limit. Request a shorter answer or increase MaxOutputTokens.",
        LanguageModelFailure.InputTooLong =>
            "The request exceeds Snoopy's input limit. Please use a shorter question.",
        _ => "Azure OpenAI could not complete the request."
    };
}
