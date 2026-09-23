using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Snoopy.Voice.Console.AI;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;
using Snoopy.Voice.Console.Services;

namespace Snoopy.Voice.Console.Tests;

public sealed class DatabaseOptionsTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=snoopy_tests;Username=test-user;Password=not-a-real-database-password";

    [Fact]
    public void DatabaseConnectionIsReadFromConfigurationWithoutPrintingSecrets()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SnoopyDatabase"] = ConnectionString
        });

        var fromSettings = DatabaseOptions.FromConfiguration(configuration);

        Assert.Equal(ConnectionString, fromSettings.ConnectionString);
        Assert.Equal(5432, new NpgsqlConnectionStringBuilder(fromSettings.GetConnectionString()).Port);
        Assert.DoesNotContain("not-a-real-database-password", fromSettings.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void MissingConfigurationFailsExplicitlyWithoutFallingBackToInMemory(string? value)
    {
        using var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:SnoopyDatabase"] = value;

        var exception = Assert.Throws<ArgumentException>(() =>
            DatabaseOptions.FromConfiguration(configuration));

        Assert.Contains("PostgreSQL configuration is missing", exception.Message);
        Assert.Contains("ConnectionStrings:SnoopyDatabase", exception.Message);
        Assert.Contains("appsettings.json", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("not-a-real-database-password")]
    [InlineData("Host=localhost;Database=snoopy_tests;Username=test-user;Unknown=not-a-real-database-password")]
    [InlineData("Host=localhost;Database=snoopy_tests;Username=test-user;Port=not-a-real-database-password")]
    [InlineData("Host=localhost;Database=snoopy_tests;Username=test-user;Password='not-a-real-database-password")]
    [InlineData("Database=snoopy_tests;Username=test-user;Password=not-a-real-database-password")]
    [InlineData("Host=localhost;Username=test-user;Password=not-a-real-database-password")]
    [InlineData("Host=localhost;Database=snoopy_tests;Password=not-a-real-database-password")]
    public void InvalidConnectionStringsAreRejectedWithoutEchoingSensitiveValues(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new DatabaseOptions { ConnectionString = value }.GetConnectionString());

        Assert.Contains("connection string is invalid", exception.Message);
        Assert.DoesNotContain("not-a-real-database-password", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ValidatedConnectionDisablesSensitiveProviderDiagnosticsWithoutChangingCredentials()
    {
        var options = new DatabaseOptions
        {
            ConnectionString = ConnectionString + ";Include Error Detail=true;Log Parameters=true;Persist Security Info=true"
        };

        var validated = new NpgsqlConnectionStringBuilder(options.GetConnectionString());

        Assert.Equal("not-a-real-database-password", validated.Password);
        Assert.Equal("test-user", validated.Username);
        Assert.Equal("snoopy_tests", validated.Database);
        Assert.False(validated.IncludeErrorDetail);
        Assert.False(validated.LogParameters);
        Assert.False(validated.PersistSecurityInfo);
        Assert.DoesNotContain("not-a-real-database-password", options.ToString());
    }

    [Fact]
    public async Task DatabaseOnlyCompositionUsesPostgreSQLAndFreshContextsWithoutAzureOrAudio()
    {
        await using var provider = ApplicationServices.CreateDatabaseProvider(
            new DatabaseOptions { ConnectionString = ConnectionString });
        var store = Assert.Single(provider.GetServices<IMemoryStore>());
        Assert.IsType<PostgreSQLMemoryStore>(store);
        Assert.Same(store, provider.GetRequiredService<IMemoryStore>());
        Assert.NotNull(provider.GetRequiredService<MemoryDatabaseInitializer>());
        Assert.Null(provider.GetService<IConversationHistory>());
        Assert.Null(provider.GetService<ILanguageModelClient>());
        Assert.Null(provider.GetService<ISpeechToTextService>());
        Assert.Null(provider.GetService<ITextToSpeechService>());
        var factory = provider.GetRequiredService<IDbContextFactory<SnoopyDbContext>>();

        await using var first = await factory.CreateDbContextAsync();
        await using var second = await factory.CreateDbContextAsync();

        Assert.NotSame(first, second);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", first.Database.ProviderName);
        Assert.False(first.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled);
        Assert.Empty(first.ChangeTracker.Entries());
    }

    [Fact]
    public void NormalApplicationCannotBeComposedWithoutDatabaseConfigurationUnlessStoreIsExplicitlySupplied()
    {
        var configuration = new ApplicationConfiguration
        {
            Speech = new SpeechOptions { SubscriptionKey = "not-a-real-key", Region = "centralindia" },
            AzureOpenAI = new AzureOpenAIOptions
            {
                Endpoint = "https://unit-test.openai.azure.com/",
                ApiKey = "not-a-real-key",
                DeploymentName = "test-deployment"
            }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            ApplicationServices.CreateProvider(configuration, new StringWriter()));
        Assert.Contains("PostgreSQL configuration is required", exception.Message);

        var store = new InMemoryMemoryStore();
        using var provider = ApplicationServices.CreateProvider(configuration, new StringWriter(), store);
        Assert.Same(store, Assert.Single(provider.GetServices<IMemoryStore>()));
        Assert.Null(provider.GetService<IDbContextFactory<SnoopyDbContext>>());
    }
}
