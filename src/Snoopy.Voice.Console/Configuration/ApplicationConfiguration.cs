using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Snoopy.Voice.Console.Configuration;

public sealed class ApplicationConfiguration
{
    public required SpeechOptions Speech { get; init; }
    public AzureOpenAIOptions? AzureOpenAI { get; init; }
    public SnoopyOptions Snoopy { get; init; } = new();

    public static ApplicationConfiguration Load(bool speechTests) =>
        Load(speechTests, Directory.GetCurrentDirectory(), Environment.GetEnvironmentVariable,
            builder => builder.AddUserSecrets<ApplicationConfiguration>(optional: true, reloadOnChange: false));

    internal static ApplicationConfiguration Load(
        bool speechTests,
        string basePath,
        Func<string, string?> environment,
        Action<IConfigurationBuilder> addUserSecrets)
    {
        using var configuration = new ConfigurationManager();
        try
        {
            configuration.SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            addUserSecrets(configuration);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or FormatException or UnauthorizedAccessException)
        {
            throw new ArgumentException(
                "Could not read appsettings.json or .NET User Secrets (secrets.json). " +
                "Check JSON syntax and file permissions. " +
                "Configuration contents are not displayed because they may contain secrets.");
        }

        return FromSources(configuration, environment, speechTests);
    }

    internal static ApplicationConfiguration FromSources(
        IConfiguration configuration, Func<string, string?> environment, bool speechTests = false)
    {
        string? Read(string variable, string setting) => environment(variable) ?? configuration[setting];

        var speech = SpeechOptions.FromEnvironment(name => name switch
        {
            "SNOOPY_SPEECH_KEY" => Read(name, "AzureSpeech:ApiKey"),
            "SNOOPY_SPEECH_REGION" => Read(name, "AzureSpeech:Region"),
            "SNOOPY_SPEECH_VOICE" => Read(name, "AzureSpeech:VoiceName"),
            _ => null
        });
        if (speechTests)
        {
            return new ApplicationConfiguration { Speech = speech };
        }

        var roleText = Read("AZURE_OPENAI_INSTRUCTION_ROLE", "AzureOpenAI:InstructionRole") ?? "System";
        if (!Enum.TryParse<InstructionRole>(roleText, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            throw new ArgumentException("AzureOpenAI InstructionRole must be System or Developer.");
        }

        var model = new AzureOpenAIOptions
        {
            Endpoint = Read("AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint")?.Trim() ?? string.Empty,
            ApiKey = Read("AZURE_OPENAI_API_KEY", "AzureOpenAI:ApiKey")?.Trim() ?? string.Empty,
            DeploymentName = Read("AZURE_OPENAI_DEPLOYMENT", "AzureOpenAI:DeploymentName")?.Trim() ?? string.Empty,
            RequestTimeoutSeconds = ReadInteger(
                Read("AZURE_OPENAI_TIMEOUT_SECONDS", "AzureOpenAI:RequestTimeoutSeconds"),
                45, "AzureOpenAI RequestTimeoutSeconds"),
            MaxOutputTokens = ReadInteger(
                Read("AZURE_OPENAI_MAX_OUTPUT_TOKENS", "AzureOpenAI:MaxOutputTokens"),
                1024, "AzureOpenAI MaxOutputTokens"),
            InstructionRole = role
        };
        model.Validate();

        var snoopy = new SnoopyOptions
        {
            SystemPrompt = Read("SNOOPY_SYSTEM_PROMPT", "Snoopy:SystemPrompt") ?? SnoopyOptions.DefaultSystemPrompt,
            MaxHistoryTurns = ReadInteger(
                Read("SNOOPY_MAX_HISTORY_TURNS", "Snoopy:MaxHistoryTurns"), 12, "Snoopy MaxHistoryTurns"),
            MaxHistoryCharacters = ReadInteger(
                Read("SNOOPY_MAX_HISTORY_CHARACTERS", "Snoopy:MaxHistoryCharacters"), 24000,
                "Snoopy MaxHistoryCharacters"),
            MaxInputCharacters = ReadInteger(
                Read("SNOOPY_MAX_INPUT_CHARACTERS", "Snoopy:MaxInputCharacters"), 4000, "Snoopy MaxInputCharacters")
        };
        snoopy.Validate();
        return new ApplicationConfiguration { Speech = speech, AzureOpenAI = model, Snoopy = snoopy };
    }

    private static int ReadInteger(string? value, int defaultValue, string setting)
    {
        if (value is null)
        {
            return defaultValue;
        }
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"{setting} must be a whole number.");
        }
        return result;
    }
}
