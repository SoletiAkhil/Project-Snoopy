using Microsoft.Extensions.DependencyInjection;
using Snoopy.Voice.Console;
using Snoopy.Voice.Console.Configuration;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("Snoopy Voice Assistant: run without arguments for continuous voice conversation.");
    Console.WriteLine("Use --speech-tests for the original independent STT, TTS and echo menu.");
    Console.WriteLine("Configure AzureSpeech and AzureOpenAI in .NET User Secrets (secrets.json).");
    Console.WriteLine("User Secrets are loaded for this local console app in both Debug and Release.");
    Console.WriteLine("Environment variables remain supported: SNOOPY_SPEECH_KEY, SNOOPY_SPEECH_REGION,");
    Console.WriteLine("SNOOPY_SPEECH_VOICE, AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY and AZURE_OPENAI_DEPLOYMENT.");
    Console.WriteLine("Priority: environment variables > User Secrets > optional appsettings.json in the working directory.");
    return 0;
}
if (args.Length > 0 && args is not ["--speech-tests"])
{
    Console.Error.WriteLine("[ERROR] Usage: Snoopy.Voice.Console [--speech-tests | --help]");
    return 1;
}

var speechTests = args is ["--speech-tests"];
ApplicationConfiguration configuration;
try
{
    configuration = ApplicationConfiguration.Load(speechTests);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"[ERROR] {exception.Message}");
    return 1;
}

if (!OperatingSystem.IsWindows())
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
