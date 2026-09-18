using Microsoft.CognitiveServices.Speech;
using System.Runtime.InteropServices;

namespace Snoopy.Voice.Console.Services;

public sealed class SpeechServiceException(string message) : Exception(message);

internal static class SpeechSdkErrors
{
    public static SpeechServiceException FromCancellation(CancellationErrorCode code) => new(code switch
    {
        CancellationErrorCode.AuthenticationFailure =>
            "Azure Speech authentication failed. Check SNOOPY_SPEECH_KEY and its resource region locally.",
        CancellationErrorCode.ConnectionFailure =>
            "Azure Speech connection failed. Check the region identifier, internet connection, proxy and firewall.",
        CancellationErrorCode.BadRequest =>
            "Azure Speech rejected the request. Check the configured region and voice.",
        CancellationErrorCode.Forbidden =>
            "Azure Speech access was denied. Check resource access and subscription status.",
        CancellationErrorCode.TooManyRequests =>
            "Azure Speech is rate-limited. Wait before retrying and check the resource quota.",
        CancellationErrorCode.ServiceTimeout =>
            "Azure Speech timed out. Check the network and retry.",
        CancellationErrorCode.ServiceUnavailable =>
            "Azure Speech is temporarily unavailable. Retry later.",
        CancellationErrorCode.NoError =>
            "Azure Speech canceled the operation before it completed.",
        _ => "Azure Speech failed. Check resource health, configuration and network connectivity."
    });

    public static T Run<T>(Func<T> action, string operation)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (IsSdkFailure(exception))
        {
            throw FromSdkFailure(exception, operation);
        }
    }

    public static async Task RunAsync(Func<Task> action, string operation)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsSdkFailure(exception))
        {
            throw FromSdkFailure(exception, operation);
        }
    }

    private static bool IsSdkFailure(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or ExternalException
            or IOException or DllNotFoundException or BadImageFormatException;

    private static SpeechServiceException FromSdkFailure(Exception exception, string operation)
    {
        // SDK exception details can contain credentials or endpoints; never forward raw diagnostics.
        if (exception is DllNotFoundException or BadImageFormatException)
        {
            return new SpeechServiceException(
                "Azure Speech native runtime could not load. Use Windows x64/ARM64 with matching .NET " +
                "and install the Microsoft Visual C++ 2015-2022 Redistributable.");
        }

        return new SpeechServiceException(operation == "STT"
            ? "Azure Speech recognition failed. Check the default microphone, Windows microphone permissions, " +
              "Speech key, region and network connection."
            : "Azure Speech synthesis or audio output failed. Check the default speaker, volume, " +
              "Speech key, region, voice and network connection.");
    }
}
