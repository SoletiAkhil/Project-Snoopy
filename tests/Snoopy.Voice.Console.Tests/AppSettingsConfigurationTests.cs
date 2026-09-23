using System.Diagnostics;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Tests;

public sealed class AppSettingsConfigurationTests
{
    private const string CompleteSettings = """
        {
          "ConnectionStrings": {
            "SnoopyDatabase": "Host=localhost;Database=snoopy_tests;Username=test-user;Password=database-test-placeholder"
          },
          "AzureSpeech": {
            "ApiKey": "speech-test-placeholder",
            "Region": "centralindia",
            "VoiceName": "en-IN-NeerjaNeural"
          },
          "AzureOpenAI": {
            "Endpoint": "https://test-resource.openai.azure.com/",
            "ApiKey": "model-test-placeholder",
            "DeploymentName": "configurable-deployment",
            "RequestTimeoutSeconds": 60
          },
          "Snoopy": {
            "SystemPrompt": "Test prompt from appsettings."
          }
        }
        """;

    [Fact]
    public void ApplicationNoLongerReferencesUserSecrets()
    {
        var assembly = typeof(ApplicationConfiguration).Assembly;
        Assert.DoesNotContain(assembly.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Name == "UserSecretsIdAttribute");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Microsoft.Extensions.Configuration.UserSecrets");
    }

    [Fact]
    public void SingleAppSettingsFileConfiguresEveryService()
    {
        using var files = new SettingsFiles();
        files.WriteSettings(CompleteSettings);

        var configuration = files.Load();

        Assert.Equal("speech-test-placeholder", configuration.Speech.SubscriptionKey);
        Assert.Equal("centralindia", configuration.Speech.Region);
        Assert.Equal("en-IN-NeerjaNeural", configuration.Speech.VoiceName);
        Assert.NotNull(configuration.AzureOpenAI);
        Assert.Equal("https://test-resource.openai.azure.com/", configuration.AzureOpenAI.Endpoint);
        Assert.Equal("model-test-placeholder", configuration.AzureOpenAI.ApiKey);
        Assert.Equal("configurable-deployment", configuration.AzureOpenAI.DeploymentName);
        Assert.Equal(60, configuration.AzureOpenAI.RequestTimeoutSeconds);
        Assert.Equal("Test prompt from appsettings.", configuration.Snoopy.SystemPrompt);
        Assert.NotNull(configuration.Database);
        Assert.Contains("Database=snoopy_tests", configuration.Database.ConnectionString);
        Assert.DoesNotContain("database-test-placeholder", configuration.Database.ToString());
    }

    [Fact]
    public void MissingAppSettingsIsAnExplicitError()
    {
        using var files = new SettingsFiles();

        var exception = Assert.Throws<ArgumentException>(() => files.Load());

        Assert.Contains("Could not read appsettings.json", exception.Message);
        Assert.Contains("appsettings.example.json", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void UnrelatedSecretsFileIsNotUsedAsAFallback()
    {
        using var files = new SettingsFiles();
        files.WriteIgnoredSecrets(CompleteSettings);

        Assert.Throws<ArgumentException>(() => files.Load());
        files.WriteSettings("{}");
        var exception = Assert.Throws<ArgumentException>(() => files.Load());

        Assert.Contains("Azure Speech configuration is missing", exception.Message);
    }

    [Fact]
    public void UnrelatedSecretsFileDoesNotOverrideAppSettings()
    {
        using var files = new SettingsFiles();
        files.WriteSettings(CompleteSettings);
        files.WriteIgnoredSecrets("""{"AzureSpeech:ApiKey":"must-not-be-used"}""");

        Assert.Equal("speech-test-placeholder", files.Load().Speech.SubscriptionKey);
    }

    [Fact]
    public void MalformedJsonDoesNotExposeContents()
    {
        using var files = new SettingsFiles();
        files.WriteSettings("""{"do-not-expose-this-test-value": """);

        var exception = Assert.Throws<ArgumentException>(() => files.Load());

        Assert.Contains("JSON syntax", exception.Message);
        Assert.DoesNotContain("do-not-expose", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void SpeechTestModeUsesOnlySpeechSettings()
    {
        using var files = new SettingsFiles();
        files.WriteSettings("""
            {
              "AzureSpeech":{"ApiKey":"speech-test-placeholder","Region":"centralindia"},
              "AzureOpenAI":{"RequestTimeoutSeconds":"invalid"},
              "ConnectionStrings":{"SnoopyDatabase":"invalid"}
            }
            """);

        var configuration = files.Load(speechTests: true);

        Assert.Equal("speech-test-placeholder", configuration.Speech.SubscriptionKey);
        Assert.Null(configuration.AzureOpenAI);
        Assert.Null(configuration.Database);
    }

    [Fact]
    public void DatabaseInitializationUsesOnlyDatabaseSettings()
    {
        using var files = new SettingsFiles();
        files.WriteSettings("""
            {"ConnectionStrings:SnoopyDatabase":"Host=localhost;Database=snoopy_tests;Username=test-user;Password=database-test-placeholder"}
            """);

        var database = files.LoadDatabase();

        Assert.Contains("Database=snoopy_tests", database.ConnectionString);
        Assert.DoesNotContain("database-test-placeholder", database.ToString());
    }

    [Fact]
    public void ChangingFileIsReadOnNextLoad()
    {
        using var files = new SettingsFiles();
        files.WriteSettings(CompleteSettings);
        Assert.Equal("speech-test-placeholder", files.Load().Speech.SubscriptionKey);
        files.WriteSettings(CompleteSettings.Replace("speech-test-placeholder", "updated-placeholder"));

        Assert.Equal("updated-placeholder", files.Load().Speech.SubscriptionKey);
    }

    [Theory]
    [InlineData(false, "Could not read appsettings.json")]
    [InlineData(true, "PostgreSQL configuration is missing")]
    public async Task MigrationCommandIgnoresEnvironmentOverridesAndReportsMissingFileOrSetting(
        bool createSettings, string expectedError)
    {
        using var files = new SettingsFiles();
        if (createSettings)
        {
            files.WriteSettings("{}");
        }
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = files.DirectoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(ApplicationConfiguration).Assembly.Location);
        start.ArgumentList.Add("--migrate-database");
        start.Environment["SNOOPY_DATABASE_CONNECTION_STRING"] = "invalid-override-must-not-be-read";
        start.Environment["ConnectionStrings__SnoopyDatabase"] = "invalid-override-must-not-be-read";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the application.");
        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var output = await standardOutput + await standardError;

            Assert.Equal(1, process.ExitCode);
            Assert.Contains(expectedError, output);
            Assert.DoesNotContain("invalid-override-must-not-be-read", output);
            Assert.DoesNotContain("schema is up to date", output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private sealed class SettingsFiles : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("SnoopySettingsTests-");
        public string DirectoryPath => _directory.FullName;

        public void WriteSettings(string content) =>
            File.WriteAllText(Path.Combine(_directory.FullName, "appsettings.json"), content);

        public void WriteIgnoredSecrets(string content) =>
            File.WriteAllText(Path.Combine(_directory.FullName, "secrets.json"), content);

        public ApplicationConfiguration Load(bool speechTests = false) =>
            ApplicationConfiguration.Load(speechTests, _directory.FullName);

        public DatabaseOptions LoadDatabase() => ApplicationConfiguration.LoadDatabaseOptions(_directory.FullName);

        public void Dispose()
        {
            File.Delete(Path.Combine(_directory.FullName, "appsettings.json"));
            File.Delete(Path.Combine(_directory.FullName, "secrets.json"));
            _directory.Delete();
        }
    }
}
