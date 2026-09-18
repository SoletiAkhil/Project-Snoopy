using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Tests;

public sealed class UserSecretsConfigurationTests
{
    private const string CompleteSecrets = """
        {
          "AzureSpeech": {
            "ApiKey": "speech-test-secret",
            "Region": "centralindia",
            "VoiceName": "en-IN-NeerjaNeural"
          },
          "AzureOpenAI": {
            "Endpoint": "https://test-resource.openai.azure.com/",
            "ApiKey": "model-test-secret",
            "DeploymentName": "configurable-deployment",
            "RequestTimeoutSeconds": 60
          },
          "Snoopy": {
            "SystemPrompt": "Test prompt from User Secrets."
          }
        }
        """;

    [Fact]
    public void ApplicationAssemblyHasSecretManagerId()
    {
        var attribute = typeof(ApplicationConfiguration).Assembly.GetCustomAttribute<UserSecretsIdAttribute>();
        Assert.NotNull(attribute);
        Assert.False(string.IsNullOrWhiteSpace(attribute.UserSecretsId));
    }

    [Fact]
    public void SingleSecretsFileConfiguresSpeechAndModelWithoutEnvironmentOrAppSettings()
    {
        using var store = new IsolatedStore();
        store.WriteSecrets(CompleteSecrets);

        var configuration = store.Load();

        Assert.Equal("speech-test-secret", configuration.Speech.SubscriptionKey);
        Assert.Equal("centralindia", configuration.Speech.Region);
        Assert.Equal("en-IN-NeerjaNeural", configuration.Speech.VoiceName);
        Assert.NotNull(configuration.AzureOpenAI);
        Assert.Equal("https://test-resource.openai.azure.com/", configuration.AzureOpenAI.Endpoint);
        Assert.Equal("model-test-secret", configuration.AzureOpenAI.ApiKey);
        Assert.Equal("configurable-deployment", configuration.AzureOpenAI.DeploymentName);
        Assert.Equal(60, configuration.AzureOpenAI.RequestTimeoutSeconds);
        Assert.Equal("Test prompt from User Secrets.", configuration.Snoopy.SystemPrompt);
    }

    [Fact]
    public void UserSecretsOverrideAppSettingsAndEnvironmentOverridesUserSecrets()
    {
        using var store = new IsolatedStore();
        store.WriteSettings(CompleteSecrets);
        store.WriteSecrets("""
            {
              "AzureSpeech:ApiKey": "user-secrets-speech",
              "AzureOpenAI:ApiKey": "user-secrets-model",
              "AzureOpenAI:DeploymentName": "user-secrets-deployment"
            }
            """);

        var fromSecrets = store.Load();
        Assert.Equal("user-secrets-speech", fromSecrets.Speech.SubscriptionKey);
        Assert.Equal("user-secrets-model", fromSecrets.AzureOpenAI!.ApiKey);
        Assert.Equal("user-secrets-deployment", fromSecrets.AzureOpenAI.DeploymentName);
        Assert.Equal(60, fromSecrets.AzureOpenAI.RequestTimeoutSeconds);

        var fromEnvironment = store.Load(name => name switch
        {
            "SNOOPY_SPEECH_KEY" => "environment-speech",
            "AZURE_OPENAI_API_KEY" => "environment-model",
            "AZURE_OPENAI_DEPLOYMENT" => "environment-deployment",
            _ => null
        });
        Assert.Equal("environment-speech", fromEnvironment.Speech.SubscriptionKey);
        Assert.Equal("environment-model", fromEnvironment.AzureOpenAI!.ApiKey);
        Assert.Equal("environment-deployment", fromEnvironment.AzureOpenAI.DeploymentName);
    }

    [Fact]
    public void MissingSecretsFileStillSupportsExistingAppSettings()
    {
        using var store = new IsolatedStore();
        store.WriteSettings(CompleteSecrets);
        Assert.Equal("speech-test-secret", store.Load().Speech.SubscriptionKey);
    }

    [Fact]
    public void MissingFilesStillSupportExistingEnvironment()
    {
        using var store = new IsolatedStore();
        var configuration = store.Load(name => name switch
        {
            "SNOOPY_SPEECH_KEY" => "speech-environment",
            "SNOOPY_SPEECH_REGION" => "centralindia",
            "AZURE_OPENAI_ENDPOINT" => "https://test-resource.openai.azure.com/",
            "AZURE_OPENAI_API_KEY" => "model-environment",
            "AZURE_OPENAI_DEPLOYMENT" => "environment-deployment",
            _ => null
        });
        Assert.Equal("speech-environment", configuration.Speech.SubscriptionKey);
        Assert.Equal("model-environment", configuration.AzureOpenAI!.ApiKey);
    }

    [Fact]
    public void BlankSecretDoesNotFallBackToAnAppSettingsKey()
    {
        using var store = new IsolatedStore();
        store.WriteSettings(CompleteSecrets);
        store.WriteSecrets("""{"AzureSpeech:ApiKey": ""}""");
        var exception = Assert.Throws<ArgumentException>(() => store.Load());
        Assert.Contains("User Secrets", exception.Message);
        Assert.DoesNotContain("speech-test-secret", exception.ToString());
    }

    [Fact]
    public void BlankEnvironmentValueDoesNotFallBackToUserSecrets()
    {
        using var store = new IsolatedStore();
        store.WriteSecrets(CompleteSecrets);
        var exception = Assert.Throws<ArgumentException>(() =>
            store.Load(name => name == "AZURE_OPENAI_API_KEY" ? "" : null));
        Assert.Contains("AzureOpenAI:ApiKey", exception.Message);
        Assert.DoesNotContain("model-test-secret", exception.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MalformedConfigurationIsReportedWithoutExposingContents(bool malformedSecrets)
    {
        using var store = new IsolatedStore();
        const string malformed = """{"do-not-expose-this-test-value": """;
        if (malformedSecrets)
        {
            store.WriteSecrets(malformed);
        }
        else
        {
            store.WriteSettings(malformed);
        }

        var exception = Assert.Throws<ArgumentException>(() => store.Load());
        Assert.Contains("Check JSON syntax", exception.Message);
        Assert.Contains("secrets.json", exception.Message);
        Assert.DoesNotContain("do-not-expose", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void SpeechTestModeCanUseOnlySpeechUserSecrets()
    {
        using var store = new IsolatedStore();
        store.WriteSecrets("""
            {"AzureSpeech":{"ApiKey":"speech-test-secret","Region":"centralindia"}}
            """);
        var configuration = store.Load(speechTests: true);
        Assert.Equal("speech-test-secret", configuration.Speech.SubscriptionKey);
        Assert.Null(configuration.AzureOpenAI);
    }

    private sealed class IsolatedStore : IDisposable
    {
        private readonly string id = $"snoopy-tests-{Guid.NewGuid():N}";
        private readonly DirectoryInfo workingDirectory = Directory.CreateTempSubdirectory("SnoopyConfigTests-");
        private readonly string secretsPath;

        public IsolatedStore()
        {
            secretsPath = PathHelper.GetSecretsPathFromSecretsId(id);
        }

        public void WriteSecrets(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
            File.WriteAllText(secretsPath, content);
        }

        public void WriteSettings(string content) =>
            File.WriteAllText(Path.Combine(workingDirectory.FullName, "appsettings.json"), content);

        public ApplicationConfiguration Load(Func<string, string?>? environment = null, bool speechTests = false) =>
            ApplicationConfiguration.Load(speechTests, workingDirectory.FullName, environment ?? (_ => null),
                builder => builder.AddUserSecrets(id, reloadOnChange: false));

        public void Dispose()
        {
            var secretsDirectory = Path.GetDirectoryName(secretsPath)!;
            if (Directory.Exists(secretsDirectory))
            {
                File.Delete(secretsPath);
                Directory.Delete(secretsDirectory);
            }
            File.Delete(Path.Combine(workingDirectory.FullName, "appsettings.json"));
            workingDirectory.Delete();
        }
    }
}
