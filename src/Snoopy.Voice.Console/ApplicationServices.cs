using Microsoft.Extensions.DependencyInjection;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console;

internal static class ApplicationServices
{
    public static ServiceProvider CreateProvider(ApplicationConfiguration configuration, TextWriter output)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration.Speech);
        services.AddSingleton(output);
        services.AddSingleton<ISpeechToTextService, SpeechToTextService>();
        services.AddSingleton<ITextToSpeechService, TextToSpeechService>();
        services.AddSingleton<Func<CancellationToken, Task<string?>>>(ConsoleInput.ReadLineAsync);
        services.AddSingleton<VoiceConsoleApplication>();

        if (configuration.AzureOpenAI is not null)
        {
            services.AddSingleton(configuration.AzureOpenAI);
            services.AddSingleton(configuration.Snoopy);
            services.AddSingleton<ILanguageModelClient, AzureOpenAILanguageModelClient>();
            services.AddSingleton<IConversationHistory, InMemoryConversationHistory>();
            services.AddSingleton<IConversationService, ConversationService>();
            services.AddSingleton<VoiceConversationApplication>();
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
