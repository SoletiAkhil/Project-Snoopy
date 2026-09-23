using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console.Tests;

public sealed class ApplicationConfigurationTests
{
    [Fact]
    public void MinimalSettingsUseDefaultsWithoutPrintingSecrets()
    {
        var configuration = Load(ValidSettings());
        Assert.Equal("centralindia", configuration.Speech.Region);
        Assert.Equal("en-IN-NeerjaNeural", configuration.Speech.VoiceName);
        Assert.NotNull(configuration.AzureOpenAI);
        Assert.Equal("my-configurable-deployment", configuration.AzureOpenAI.DeploymentName);
        Assert.Equal(45, configuration.AzureOpenAI.RequestTimeoutSeconds);
        Assert.Equal(1024, configuration.AzureOpenAI.MaxOutputTokens);
        Assert.Equal(InstructionRole.System, configuration.AzureOpenAI.InstructionRole);
        Assert.Equal(SnoopyOptions.DefaultSystemPrompt, configuration.Snoopy.SystemPrompt);
        Assert.False(configuration.Snoopy.EnableMemoryContext);
        Assert.Equal(20, configuration.Snoopy.MaxMemoryContextItems);
        Assert.Equal(4000, configuration.Snoopy.MaxMemoryContextCharacters);
        Assert.DoesNotContain("not-a-real-key", configuration.ToString());
        Assert.DoesNotContain("not-a-real-key", configuration.AzureOpenAI.ToString());
        Assert.NotNull(configuration.Database);
        Assert.DoesNotContain("not-a-real-key", configuration.Database.ToString());
    }

    [Fact]
    public void JsonSettingsConfigureAllServicesAndOptionalTuning()
    {
        var settings = new Dictionary<string, string?>
        {
            ["AzureSpeech:ApiKey"] = "speech-file-placeholder",
            ["AzureSpeech:Region"] = "centralindia",
            ["AzureSpeech:VoiceName"] = "en-IN-NeerjaNeural",
            ["AzureOpenAI:Endpoint"] = "https://file-resource.openai.azure.com/",
            ["AzureOpenAI:ApiKey"] = "model-file-placeholder",
            ["AzureOpenAI:DeploymentName"] = "file-deployment",
            ["AzureOpenAI:RequestTimeoutSeconds"] = "70",
            ["AzureOpenAI:MaxOutputTokens"] = "2048",
            ["AzureOpenAI:InstructionRole"] = "Developer",
            ["ConnectionStrings:SnoopyDatabase"] =
                "Host=localhost;Database=snoopy_tests;Username=test-user;Password=database-file-placeholder",
            ["Snoopy:SystemPrompt"] = "Configured prompt",
            ["Snoopy:MaxHistoryTurns"] = "5",
            ["Snoopy:MaxHistoryCharacters"] = "6000",
            ["Snoopy:MaxInputCharacters"] = "1000",
            ["Snoopy:EnableMemoryContext"] = "true",
            ["Snoopy:MaxMemoryContextItems"] = "7",
            ["Snoopy:MaxMemoryContextCharacters"] = "2048"
        };
        var configured = Load(settings);
        Assert.NotNull(configured.AzureOpenAI);
        Assert.Equal("file-deployment", configured.AzureOpenAI.DeploymentName);
        Assert.Equal("model-file-placeholder", configured.AzureOpenAI.ApiKey);
        Assert.Equal(70, configured.AzureOpenAI.RequestTimeoutSeconds);
        Assert.Equal(2048, configured.AzureOpenAI.MaxOutputTokens);
        Assert.Equal(InstructionRole.Developer, configured.AzureOpenAI.InstructionRole);
        Assert.Equal("speech-file-placeholder", configured.Speech.SubscriptionKey);
        Assert.Equal("Configured prompt", configured.Snoopy.SystemPrompt);
        Assert.Equal(5, configured.Snoopy.MaxHistoryTurns);
        Assert.Equal(6000, configured.Snoopy.MaxHistoryCharacters);
        Assert.Equal(1000, configured.Snoopy.MaxInputCharacters);
        Assert.True(configured.Snoopy.EnableMemoryContext);
        Assert.Equal(7, configured.Snoopy.MaxMemoryContextItems);
        Assert.Equal(2048, configured.Snoopy.MaxMemoryContextCharacters);
    }

    [Theory]
    [InlineData("AzureOpenAI:Endpoint")]
    [InlineData("AzureOpenAI:ApiKey")]
    [InlineData("AzureOpenAI:DeploymentName")]
    public void MissingModelSettingFailsWithNamesButNotValues(string name)
    {
        var settings = ValidSettings();
        settings.Remove(name);
        var exception = Assert.Throws<ArgumentException>(() => Load(settings));
        Assert.Contains("Azure OpenAI configuration is missing", exception.Message);
        Assert.DoesNotContain("not-a-real-key", exception.ToString());
    }

    [Fact]
    public void EmptyConfiguredModelKeyIsRejected()
    {
        var settings = ValidSettings();
        settings["AzureOpenAI:ApiKey"] = "";
        Assert.Throws<ArgumentException>(() => Load(settings));
    }

    [Fact]
    public void NormalModeRequiresDatabaseSettingsWithoutChangingSpeechOnlyMode()
    {
        var settings = ValidSettings();
        settings.Remove("ConnectionStrings:SnoopyDatabase");

        var exception = Assert.Throws<ArgumentException>(() => Load(settings));

        Assert.Contains("PostgreSQL configuration is missing", exception.Message);
        Assert.Null(Load(settings, speechTests: true).Database);
    }

    [Theory]
    [InlineData("AzureOpenAI:RequestTimeoutSeconds", "0")]
    [InlineData("AzureOpenAI:RequestTimeoutSeconds", "301")]
    [InlineData("AzureOpenAI:RequestTimeoutSeconds", "not-a-real-key")]
    [InlineData("AzureOpenAI:MaxOutputTokens", "0")]
    [InlineData("AzureOpenAI:InstructionRole", "unknown")]
    [InlineData("Snoopy:MaxHistoryTurns", "0")]
    [InlineData("Snoopy:MaxHistoryTurns", "101")]
    [InlineData("Snoopy:MaxHistoryCharacters", "10")]
    [InlineData("Snoopy:MaxInputCharacters", "24000")]
    [InlineData("Snoopy:SystemPrompt", " ")]
    [InlineData("Snoopy:EnableMemoryContext", "not-a-real-key")]
    [InlineData("Snoopy:MaxMemoryContextItems", "0")]
    [InlineData("Snoopy:MaxMemoryContextItems", "101")]
    [InlineData("Snoopy:MaxMemoryContextItems", "not-a-real-key")]
    [InlineData("Snoopy:MaxMemoryContextCharacters", "511")]
    [InlineData("Snoopy:MaxMemoryContextCharacters", "32001")]
    [InlineData("Snoopy:MaxMemoryContextCharacters", "not-a-real-key")]
    public void InvalidSettingsAreRejectedWithoutEchoingTheirValues(string name, string value)
    {
        var settings = ValidSettings();
        settings[name] = value;
        var exception = Assert.Throws<ArgumentException>(() => Load(settings));
        Assert.DoesNotContain("not-a-real-key", exception.ToString());
    }

    [Theory]
    [InlineData("https://my-resource.openai.azure.com", "https://my-resource.openai.azure.com/openai/v1/")]
    [InlineData("https://my-resource.openai.azure.com/", "https://my-resource.openai.azure.com/openai/v1/")]
    [InlineData("https://my-resource.openai.azure.com/openai/v1", "https://my-resource.openai.azure.com/openai/v1/")]
    [InlineData("https://my-resource.services.ai.azure.com/openai/v1/", "https://my-resource.services.ai.azure.com/openai/v1/")]
    public void AzureResourceAndV1EndpointsAreAccepted(string endpoint, string expected)
    {
        var options = new AzureOpenAIOptions { Endpoint = endpoint };
        Assert.Equal(expected, options.GetApiEndpoint().AbsoluteUri);
    }

    [Theory]
    [InlineData("not a URL")]
    [InlineData("http://my-resource.openai.azure.com/")]
    [InlineData("https://api.openai.com/v1/")]
    [InlineData("https://my-resource.openai.azure.com.attacker.example/")]
    [InlineData("https://not-a-real-key@my-resource.openai.azure.com/")]
    [InlineData("https://my-resource.openai.azure.com/?api-key=not-a-real-key")]
    [InlineData("https://my-resource.openai.azure.com/#not-a-real-key")]
    [InlineData("https://my-resource.openai.azure.com/openai/deployments/example/chat/completions")]
    [InlineData("https://my-resource.openai.azure.com:8443/")]
    public void UnsafeOrWrongEndpointIsRejectedWithoutPrintingIt(string endpoint)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AzureOpenAIOptions { Endpoint = endpoint }.GetApiEndpoint());
        Assert.DoesNotContain("not-a-real-key", exception.ToString());
    }

    [Fact]
    public void SpeechTestModeDoesNotRequireOrValidateModelConfiguration()
    {
        var settings = new Dictionary<string, string?>
        {
            ["AzureSpeech:ApiKey"] = "not-a-real-key",
            ["AzureSpeech:Region"] = "centralindia",
            ["AzureOpenAI:RequestTimeoutSeconds"] = "invalid",
            ["ConnectionStrings:SnoopyDatabase"] = "invalid",
            ["Snoopy:EnableMemoryContext"] = "not-a-real-key"
        };
        var configuration = Load(settings, speechTests: true);
        Assert.Null(configuration.AzureOpenAI);
        Assert.Null(configuration.Database);
        using var provider = ApplicationServices.CreateProvider(configuration, new StringWriter());
        Assert.IsType<SpeechToTextService>(provider.GetRequiredService<ISpeechToTextService>());
        Assert.IsType<TextToSpeechService>(provider.GetRequiredService<ITextToSpeechService>());
        Assert.NotNull(provider.GetRequiredService<VoiceConsoleApplication>());
        Assert.Null(provider.GetService<ILanguageModelClient>());
        Assert.Null(provider.GetService<IMemoryStore>());
    }

    [Fact]
    public void ConversationCompositionResolvesWithoutOpeningDevicesOrConnecting()
    {
        using var provider = ApplicationServices.CreateProvider(Load(ValidSettings()), new StringWriter());
        Assert.IsType<AzureOpenAILanguageModelClient>(provider.GetRequiredService<ILanguageModelClient>());
        Assert.IsType<ConversationService>(provider.GetRequiredService<IConversationService>());
        Assert.IsType<InMemoryConversationHistory>(provider.GetRequiredService<IConversationHistory>());
        Assert.IsType<MemoryContextProvider>(provider.GetRequiredService<IMemoryContextProvider>());
        Assert.Same(provider.GetRequiredService<IConversationHistory>(), provider.GetRequiredService<IConversationHistory>());
        Assert.NotNull(provider.GetRequiredService<VoiceConversationApplication>());
        Assert.Same(provider.GetRequiredService<ILanguageModelClient>(), provider.GetRequiredService<ILanguageModelClient>());
        Assert.IsType<PostgreSQLMemoryStore>(Assert.Single(provider.GetServices<IMemoryStore>()));
    }

    [Theory]
    [InlineData(false, 1523, true)]
    [InlineData(true, 1523, false)]
    [InlineData(true, 1524, true)]
    public void MemoryContextMustFitTheTotalRequestBudgetWhenEnabled(bool enabled, int budget, bool valid)
    {
        var options = new SnoopyOptions
        {
            SystemPrompt = new string('s', 100),
            MaxHistoryCharacters = budget,
            MaxInputCharacters = 400,
            EnableMemoryContext = enabled,
            MaxMemoryContextCharacters = 1024
        };

        if (valid)
        {
            options.Validate();
        }
        else
        {
            var exception = Assert.Throws<ArgumentException>(options.Validate);
            Assert.Contains("MaxMemoryContextCharacters must fit", exception.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemoryServiceAndParserShareApplicationLifetimeAndRegisteredStore(bool speechTests)
    {
        var configuration = Load(ValidSettings(), speechTests: speechTests);
        using var provider = ApplicationServices.CreateProvider(configuration, new StringWriter(), new InMemoryMemoryStore());
        using var otherApplication = ApplicationServices.CreateProvider(
            configuration, new StringWriter(), new InMemoryMemoryStore());
        var service = provider.GetRequiredService<IMemoryService>();
        var parser = provider.GetRequiredService<IMemoryCommandParser>();
        Assert.IsType<MemoryService>(service);
        Assert.IsType<MemoryCommandParser>(parser);
        using var scope = provider.CreateScope();
        Assert.Same(service, scope.ServiceProvider.GetRequiredService<IMemoryService>());
        Assert.Same(parser, scope.ServiceProvider.GetRequiredService<IMemoryCommandParser>());
        Assert.NotSame(service, otherApplication.GetRequiredService<IMemoryService>());
        Assert.NotSame(parser, otherApplication.GetRequiredService<IMemoryCommandParser>());

        var command = parser.Parse("Remember that I prefer .NET.");
        Assert.Equal(MemoryCommandType.Remember, command.CommandType);
        var memory = await service.RememberAsync(command.Content);

        Assert.Equal(memory, Assert.Single(await provider.GetRequiredService<IMemoryStore>().GetAllAsync()));
        Assert.Empty(await otherApplication.GetRequiredService<IMemoryService>().GetMemoriesAsync());
        await service.ClearMemoriesAsync();
        Assert.Empty(await provider.GetRequiredService<IMemoryStore>().GetAllAsync());
    }

    [Fact]
    public async Task RegisteredHistoryIsSharedWithConversationServiceButNotOtherSessions()
    {
        using var provider = ApplicationServices.CreateProvider(
            Load(ValidSettings()), new StringWriter(), new InMemoryMemoryStore());
        using var otherSession = ApplicationServices.CreateProvider(
            Load(ValidSettings()), new StringWriter(), new InMemoryMemoryStore());
        var history = provider.GetRequiredService<IConversationHistory>();
        await history.AddTurnAsync(new("My name is Akhil.", "Hello Akhil."));

        Assert.NotSame(history, otherSession.GetRequiredService<IConversationHistory>());
        Assert.Empty(await otherSession.GetRequiredService<IConversationHistory>().GetTurnsAsync());

        await provider.GetRequiredService<IConversationService>().ClearAsync();
        Assert.Empty(await history.GetTurnsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemoryStoreIsSingletonAcrossScopesButNotSharedByApplications(bool speechTests)
    {
        var configuration = Load(ValidSettings(), speechTests: speechTests);
        using var provider = ApplicationServices.CreateProvider(configuration, new StringWriter(), new InMemoryMemoryStore());
        using var otherApplication = ApplicationServices.CreateProvider(
            configuration, new StringWriter(), new InMemoryMemoryStore());
        var store = provider.GetRequiredService<IMemoryStore>();
        Assert.IsType<InMemoryMemoryStore>(store);
        Assert.Same(store, provider.GetRequiredService<IMemoryStore>());
        var memory = new Memory("Prefer .NET for backend development.", MemoryCategory.Preference);

        using (var scope = provider.CreateScope())
        {
            var scopedStore = scope.ServiceProvider.GetRequiredService<IMemoryStore>();
            Assert.Same(store, scopedStore);
            await scopedStore.AddAsync(memory);
        }

        using var nextScope = provider.CreateScope();
        var nextStore = nextScope.ServiceProvider.GetRequiredService<IMemoryStore>();
        Assert.Same(store, nextStore);
        Assert.Equal(memory, await nextStore.GetByIdAsync(memory.Id));
        var otherStore = otherApplication.GetRequiredService<IMemoryStore>();
        Assert.NotSame(store, otherStore);
        Assert.Empty(await otherStore.GetAllAsync());
    }

    [Fact]
    public async Task RegisteredHistoryAndMemoryCanBeClearedIndependently()
    {
        using var provider = ApplicationServices.CreateProvider(
            Load(ValidSettings()), new StringWriter(), new InMemoryMemoryStore());
        var history = provider.GetRequiredService<IConversationHistory>();
        var store = provider.GetRequiredService<IMemoryStore>();
        var turn = new ConversationTurn("My name is Akhil.", "Hello Akhil.");
        await history.AddTurnAsync(turn);
        Assert.Empty(await store.GetAllAsync());
        var memory = new Memory("Prefer .NET for backend development.", MemoryCategory.Preference);
        await store.AddAsync(memory);
        Assert.Equal(turn, Assert.Single(await history.GetTurnsAsync()));

        await provider.GetRequiredService<IConversationService>().ClearAsync();

        Assert.Empty(await history.GetTurnsAsync());
        Assert.Equal(memory, Assert.Single(await store.GetAllAsync()));
        await history.AddTurnAsync(turn);
        await store.ClearAsync();
        Assert.Empty(await store.GetAllAsync());
        Assert.Equal(turn, Assert.Single(await history.GetTurnsAsync()));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["AzureSpeech:ApiKey"] = "speech-not-a-real-key",
        ["AzureSpeech:Region"] = "centralindia",
        ["AzureOpenAI:Endpoint"] = "https://unit-test.openai.azure.com/",
        ["AzureOpenAI:ApiKey"] = "model-not-a-real-key",
        ["AzureOpenAI:DeploymentName"] = "my-configurable-deployment",
        ["ConnectionStrings:SnoopyDatabase"] =
            "Host=localhost;Database=snoopy_tests;Username=test-user;Password=database-not-a-real-key"
    };

    private static ApplicationConfiguration Load(
        Dictionary<string, string?> settings, bool speechTests = false)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(settings);
        return ApplicationConfiguration.FromConfiguration(configuration, speechTests);
    }
}
