using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console.Tests;

public sealed class ApplicationConfigurationTests
{
    [Fact]
    public void EnvironmentOnlyConfigurationUsesDefaultsWithoutPrintingSecrets()
    {
        var configuration = Load(ValidEnvironment());
        Assert.Equal("centralindia", configuration.Speech.Region);
        Assert.Equal("en-IN-NeerjaNeural", configuration.Speech.VoiceName);
        Assert.NotNull(configuration.AzureOpenAI);
        Assert.Equal("my-configurable-deployment", configuration.AzureOpenAI.DeploymentName);
        Assert.Equal(45, configuration.AzureOpenAI.RequestTimeoutSeconds);
        Assert.Equal(1024, configuration.AzureOpenAI.MaxOutputTokens);
        Assert.Equal(InstructionRole.System, configuration.AzureOpenAI.InstructionRole);
        Assert.Equal(SnoopyOptions.DefaultSystemPrompt, configuration.Snoopy.SystemPrompt);
        Assert.DoesNotContain("not-a-real-key", configuration.ToString());
        Assert.DoesNotContain("not-a-real-key", configuration.AzureOpenAI.ToString());
    }

    [Fact]
    public void JsonSettingsWorkAndEnvironmentOverridesThem()
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
            ["Snoopy:SystemPrompt"] = "Configured prompt",
            ["Snoopy:MaxHistoryTurns"] = "5",
            ["Snoopy:MaxHistoryCharacters"] = "6000",
            ["Snoopy:MaxInputCharacters"] = "1000"
        };
        var fromFile = Load(new Dictionary<string, string?>(), settings);
        Assert.NotNull(fromFile.AzureOpenAI);
        Assert.Equal("file-deployment", fromFile.AzureOpenAI.DeploymentName);
        Assert.Equal("speech-file-placeholder", fromFile.Speech.SubscriptionKey);

        var environment = new Dictionary<string, string?>
        {
            ["AZURE_OPENAI_DEPLOYMENT"] = "future-deployment",
            ["AZURE_OPENAI_API_KEY"] = "environment-placeholder",
            ["SNOOPY_SPEECH_KEY"] = "speech-environment-placeholder",
            ["SNOOPY_SYSTEM_PROMPT"] = "Environment prompt",
            ["SNOOPY_MAX_HISTORY_TURNS"] = "3"
        };
        var configured = Load(environment, settings);
        Assert.NotNull(configured.AzureOpenAI);
        Assert.Equal("future-deployment", configured.AzureOpenAI.DeploymentName);
        Assert.Equal("environment-placeholder", configured.AzureOpenAI.ApiKey);
        Assert.Equal(70, configured.AzureOpenAI.RequestTimeoutSeconds);
        Assert.Equal(2048, configured.AzureOpenAI.MaxOutputTokens);
        Assert.Equal(InstructionRole.Developer, configured.AzureOpenAI.InstructionRole);
        Assert.Equal("speech-environment-placeholder", configured.Speech.SubscriptionKey);
        Assert.Equal("Environment prompt", configured.Snoopy.SystemPrompt);
        Assert.Equal(3, configured.Snoopy.MaxHistoryTurns);
        Assert.Equal(6000, configured.Snoopy.MaxHistoryCharacters);
        Assert.Equal(1000, configured.Snoopy.MaxInputCharacters);
    }

    [Theory]
    [InlineData("AZURE_OPENAI_ENDPOINT")]
    [InlineData("AZURE_OPENAI_API_KEY")]
    [InlineData("AZURE_OPENAI_DEPLOYMENT")]
    public void MissingModelSettingFailsWithNamesButNotValues(string name)
    {
        var environment = ValidEnvironment();
        environment.Remove(name);
        var exception = Assert.Throws<ArgumentException>(() => Load(environment));
        Assert.Contains("Azure OpenAI configuration is missing", exception.Message);
        Assert.DoesNotContain("not-a-real-key", exception.ToString());
    }

    [Fact]
    public void EmptyEnvironmentSecretDoesNotFallBackToFileSecret()
    {
        var environment = ValidEnvironment();
        environment["AZURE_OPENAI_API_KEY"] = "";
        Assert.Throws<ArgumentException>(() => Load(environment,
            new Dictionary<string, string?> { ["AzureOpenAI:ApiKey"] = "file-placeholder" }));
    }

    [Theory]
    [InlineData("AZURE_OPENAI_TIMEOUT_SECONDS", "0")]
    [InlineData("AZURE_OPENAI_TIMEOUT_SECONDS", "301")]
    [InlineData("AZURE_OPENAI_TIMEOUT_SECONDS", "not-a-real-key")]
    [InlineData("AZURE_OPENAI_MAX_OUTPUT_TOKENS", "0")]
    [InlineData("AZURE_OPENAI_INSTRUCTION_ROLE", "unknown")]
    [InlineData("SNOOPY_MAX_HISTORY_TURNS", "0")]
    [InlineData("SNOOPY_MAX_HISTORY_TURNS", "101")]
    [InlineData("SNOOPY_MAX_HISTORY_CHARACTERS", "10")]
    [InlineData("SNOOPY_MAX_INPUT_CHARACTERS", "24000")]
    [InlineData("SNOOPY_SYSTEM_PROMPT", " ")]
    public void InvalidSettingsAreRejectedWithoutEchoingTheirValues(string name, string value)
    {
        var environment = ValidEnvironment();
        environment[name] = value;
        var exception = Assert.Throws<ArgumentException>(() => Load(environment));
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
        var environment = new Dictionary<string, string?>
        {
            ["SNOOPY_SPEECH_KEY"] = "not-a-real-key",
            ["SNOOPY_SPEECH_REGION"] = "centralindia",
            ["AZURE_OPENAI_TIMEOUT_SECONDS"] = "invalid"
        };
        var configuration = Load(environment, speechTests: true);
        Assert.Null(configuration.AzureOpenAI);
        using var provider = ApplicationServices.CreateProvider(configuration, new StringWriter());
        Assert.IsType<SpeechToTextService>(provider.GetRequiredService<ISpeechToTextService>());
        Assert.IsType<TextToSpeechService>(provider.GetRequiredService<ITextToSpeechService>());
        Assert.NotNull(provider.GetRequiredService<VoiceConsoleApplication>());
        Assert.Null(provider.GetService<ILanguageModelClient>());
    }

    [Fact]
    public void ConversationCompositionResolvesWithoutOpeningDevicesOrConnecting()
    {
        using var provider = ApplicationServices.CreateProvider(Load(ValidEnvironment()), new StringWriter());
        Assert.IsType<AzureOpenAILanguageModelClient>(provider.GetRequiredService<ILanguageModelClient>());
        Assert.IsType<ConversationService>(provider.GetRequiredService<IConversationService>());
        Assert.NotNull(provider.GetRequiredService<VoiceConversationApplication>());
        Assert.Same(provider.GetRequiredService<ILanguageModelClient>(), provider.GetRequiredService<ILanguageModelClient>());
    }

    private static Dictionary<string, string?> ValidEnvironment() => new()
    {
        ["SNOOPY_SPEECH_KEY"] = "speech-not-a-real-key",
        ["SNOOPY_SPEECH_REGION"] = "centralindia",
        ["AZURE_OPENAI_ENDPOINT"] = "https://unit-test.openai.azure.com/",
        ["AZURE_OPENAI_API_KEY"] = "model-not-a-real-key",
        ["AZURE_OPENAI_DEPLOYMENT"] = "my-configurable-deployment"
    };

    private static ApplicationConfiguration Load(
        Dictionary<string, string?> environment,
        Dictionary<string, string?>? settings = null,
        bool speechTests = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return ApplicationConfiguration.FromSources(
            configuration, name => environment.GetValueOrDefault(name), speechTests);
    }
}
