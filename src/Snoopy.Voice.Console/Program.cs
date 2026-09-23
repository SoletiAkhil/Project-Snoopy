using Microsoft.Extensions.DependencyInjection;
using Snoopy.Voice.Console;
using Snoopy.Voice.Console.Configuration;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("Snoopy Voice Assistant: run without arguments for continuous voice conversation.");
    Console.WriteLine("Use --speech-tests for the original independent STT, TTS and echo menu.");
    Console.WriteLine("Use --migrate-database to apply memory migrations to your configured local PostgreSQL database.");
    Console.WriteLine("Configure AzureSpeech, AzureOpenAI and ConnectionStrings:SnoopyDatabase in local appsettings.json.");
    Console.WriteLine("Only appsettings.json in the working directory is loaded. User Secrets and environment overrides are not used.");
    Console.WriteLine("Copy appsettings.example.json to appsettings.json, which is ignored by Git, and edit it locally.");
    return 0;
}
if (args.Length > 0 && args is not ["--speech-tests"] and not ["--migrate-database"])
{
    Console.Error.WriteLine("[ERROR] Usage: Snoopy.Voice.Console [--speech-tests | --migrate-database | --help]");
    return 1;
}

var speechTests = args is ["--speech-tests"];
var migrateDatabase = args is ["--migrate-database"];
ApplicationConfiguration? configuration = null;
DatabaseOptions? database = null;
try
{
    if (migrateDatabase)
    {
        database = ApplicationConfiguration.LoadDatabaseOptions();
    }
    else
    {
        configuration = ApplicationConfiguration.Load(speechTests);
    }
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"[ERROR] {exception.Message}");
    return 1;
}

if (!migrateDatabase && !OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("[ERROR] Snoopy requires Windows and default microphone/speaker devices.");
    return 1;
}

using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancel;
try
{
    if (database is not null)
    {
        await using var databaseServices = ApplicationServices.CreateDatabaseProvider(database);
        Console.WriteLine("[MEMORY] Applying database migrations...");
        await databaseServices.GetRequiredService<MemoryDatabaseInitializer>().MigrateAsync(shutdown.Token);
        Console.WriteLine("[MEMORY] Memory database schema is up to date.");
        return 0;
    }

    ArgumentNullException.ThrowIfNull(configuration);
    await using var services = ApplicationServices.CreateProvider(configuration, Console.Out);
    if (speechTests)
    {
        await services.GetRequiredService<VoiceConsoleApplication>().RunAsync(shutdown.Token);
    }
    else
    {
        await services.GetRequiredService<VoiceConversationApplication>().RunAsync(shutdown.Token);
    }
    return 0;
}
catch (MemoryStorageException exception)
{
    Console.Error.WriteLine($"[ERROR] {exception.Message}");
    return 1;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    Console.WriteLine("\n[INFO] Snoopy stopped.");
    return 0;
}
catch (IOException)
{
    Console.Error.WriteLine("[ERROR] Console input/output is unavailable. Run Snoopy in an interactive Windows terminal.");
    return 1;
}
finally
{
    Console.CancelKeyPress -= cancel;
}
