using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console;

internal static class ApplicationServices
{
    public static ServiceProvider CreateProvider(
        ApplicationConfiguration configuration, TextWriter output, IMemoryStore? memoryStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration.Speech);
        services.AddSingleton(output);
        services.AddSingleton<ISpeechToTextService, SpeechToTextService>();
        services.AddSingleton<ITextToSpeechService, TextToSpeechService>();
        services.AddSingleton<Func<CancellationToken, Task<string?>>>(ConsoleInput.ReadLineAsync);
        services.AddSingleton<VoiceConsoleApplication>();
        if (memoryStore is not null)
        {
            services.AddSingleton(memoryStore);
        }
        else if (configuration.AzureOpenAI is not null)
        {
            AddPersistentMemory(services, configuration.Database ??
                throw new ArgumentException("PostgreSQL configuration is required for persistent memory."));
        }
        if (memoryStore is not null || configuration.AzureOpenAI is not null)
        {
            services.AddSingleton<IMemoryCommandParser, MemoryCommandParser>();
            services.AddSingleton<IMemoryService, MemoryService>();
        }

        if (configuration.AzureOpenAI is not null)
        {
            services.AddSingleton(configuration.AzureOpenAI);
            services.AddSingleton(configuration.Snoopy);
            services.AddSingleton<ILanguageModelClient, AzureOpenAILanguageModelClient>();
            services.AddSingleton<IConversationHistory, InMemoryConversationHistory>();
            services.AddSingleton<IMemoryContextProvider, MemoryContextProvider>();
            services.AddSingleton<IConversationService, ConversationService>();
            services.AddSingleton<VoiceConversationApplication>();
        }

        return BuildProvider(services);
    }

    public static ServiceProvider CreateDatabaseProvider(DatabaseOptions database)
    {
        var services = new ServiceCollection();
        AddPersistentMemory(services, database);
        return BuildProvider(services);
    }

    private static void AddPersistentMemory(IServiceCollection services, DatabaseOptions database)
    {
        var connectionString = database.GetConnectionString();
        services.AddDbContextFactory<SnoopyDbContext>(options =>
            options.UseNpgsql(connectionString).EnableSensitiveDataLogging(false));
        services.AddSingleton<IMemoryStore, PostgreSQLMemoryStore>();
        services.AddSingleton<MemoryDatabaseInitializer>();
    }

    private static ServiceProvider BuildProvider(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
}
