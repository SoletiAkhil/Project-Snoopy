using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Snoopy.Voice.Console.Configuration;

public sealed class ApplicationConfiguration
{
    public required SpeechOptions Speech { get; init; }
    public AzureOpenAIOptions? AzureOpenAI { get; init; }
    public SnoopyOptions Snoopy { get; init; } = new();
    public DatabaseOptions? Database { get; init; }

    public static ApplicationConfiguration Load(bool speechTests) =>
        Load(speechTests, Directory.GetCurrentDirectory());

    internal static ApplicationConfiguration Load(bool speechTests, string basePath) =>
        LoadSettings(basePath, configuration => FromConfiguration(configuration, speechTests));

    public static DatabaseOptions LoadDatabaseOptions() =>
        LoadDatabaseOptions(Directory.GetCurrentDirectory());

    internal static DatabaseOptions LoadDatabaseOptions(string basePath) =>
        LoadSettings(basePath, DatabaseOptions.FromConfiguration);

    private static T LoadSettings<T>(string basePath, Func<IConfiguration, T> read)
    {
        using var configuration = new ConfigurationManager();
        try
        {
            configuration.SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or FormatException or UnauthorizedAccessException)
        {
            throw new ArgumentException(
                "Could not read appsettings.json in the working directory. " +
                "Create your local copy from appsettings.example.json and check JSON syntax and file permissions. " +
                "Configuration contents are not displayed because they may contain secrets.");
        }

        return read(configuration);
    }

    internal static ApplicationConfiguration FromConfiguration(
        IConfiguration configuration, bool speechTests = false)
    {
        var speech = SpeechOptions.FromConfiguration(configuration);
        if (speechTests)
        {
            return new ApplicationConfiguration { Speech = speech };
        }

        var roleText = configuration["AzureOpenAI:InstructionRole"] ?? "System";
        if (!Enum.TryParse<InstructionRole>(roleText, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            throw new ArgumentException("AzureOpenAI InstructionRole must be System or Developer.");
        }

        var model = new AzureOpenAIOptions
        {
            Endpoint = configuration["AzureOpenAI:Endpoint"]?.Trim() ?? string.Empty,
            ApiKey = configuration["AzureOpenAI:ApiKey"]?.Trim() ?? string.Empty,
            DeploymentName = configuration["AzureOpenAI:DeploymentName"]?.Trim() ?? string.Empty,
            RequestTimeoutSeconds = ReadInteger(
                configuration["AzureOpenAI:RequestTimeoutSeconds"],
                45, "AzureOpenAI RequestTimeoutSeconds"),
            MaxOutputTokens = ReadInteger(
                configuration["AzureOpenAI:MaxOutputTokens"],
                1024, "AzureOpenAI MaxOutputTokens"),
            InstructionRole = role
        };
        model.Validate();

        var snoopy = new SnoopyOptions
        {
            SystemPrompt = configuration["Snoopy:SystemPrompt"] ?? SnoopyOptions.DefaultSystemPrompt,
            MaxHistoryTurns = ReadInteger(
                configuration["Snoopy:MaxHistoryTurns"], 12, "Snoopy MaxHistoryTurns"),
            MaxHistoryCharacters = ReadInteger(
                configuration["Snoopy:MaxHistoryCharacters"], 24000,
                "Snoopy MaxHistoryCharacters"),
            MaxInputCharacters = ReadInteger(
                configuration["Snoopy:MaxInputCharacters"], 4000, "Snoopy MaxInputCharacters"),
            EnableMemoryContext = ReadBoolean(
                configuration["Snoopy:EnableMemoryContext"], false, "Snoopy EnableMemoryContext"),
            MaxMemoryContextItems = ReadInteger(
                configuration["Snoopy:MaxMemoryContextItems"], 20, "Snoopy MaxMemoryContextItems"),
            MaxMemoryContextCharacters = ReadInteger(
                configuration["Snoopy:MaxMemoryContextCharacters"], 4000, "Snoopy MaxMemoryContextCharacters")
        };
        snoopy.Validate();
        return new ApplicationConfiguration
        {
            Speech = speech,
            AzureOpenAI = model,
            Snoopy = snoopy,
            Database = DatabaseOptions.FromConfiguration(configuration)
        };
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

    private static bool ReadBoolean(string? value, bool defaultValue, string setting)
    {
        if (value is null)
        {
            return defaultValue;
        }
        if (!bool.TryParse(value, out var result))
        {
            throw new ArgumentException($"{setting} must be true or false.");
        }
        return result;
    }
}
