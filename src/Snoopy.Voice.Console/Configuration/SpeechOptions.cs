namespace Snoopy.Voice.Console.Configuration;

public sealed class SpeechOptions
{
    public const string DefaultRegion = "centralindia";
    public const string DefaultVoiceName = "en-IN-NeerjaNeural";
    public const string MissingConfigurationMessage =
        "Azure Speech configuration is missing.\n\n" +
        "Set AzureSpeech:ApiKey and AzureSpeech:Region in .NET User Secrets (secrets.json) " +
        "or appsettings.json.\n\n" +
        "Environment alternatives:\nSNOOPY_SPEECH_KEY\nSNOOPY_SPEECH_REGION";

    public string SubscriptionKey { get; init; } = string.Empty;
    public string Region { get; init; } = DefaultRegion;
    public string VoiceName { get; init; } = DefaultVoiceName;

    public static SpeechOptions FromEnvironment(Func<string, string?>? readVariable = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;
        var voice = readVariable("SNOOPY_SPEECH_VOICE");
        var options = new SpeechOptions
        {
            SubscriptionKey = readVariable("SNOOPY_SPEECH_KEY")?.Trim() ?? string.Empty,
            // Environment configuration deliberately requires an explicit resource region.
            Region = readVariable("SNOOPY_SPEECH_REGION")?.Trim() ?? string.Empty,
            VoiceName = string.IsNullOrWhiteSpace(voice) ? DefaultVoiceName : voice.Trim()
        };
        options.Validate();
        return options;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SubscriptionKey) || string.IsNullOrWhiteSpace(Region))
        {
            throw new ArgumentException(MissingConfigurationMessage);
        }

        if (Region.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException(
                "Azure Speech region must be an identifier such as centralindia, not a display name or URL.");
        }

        if (string.IsNullOrWhiteSpace(VoiceName))
        {
            throw new ArgumentException("Azure Speech voice must not be empty.");
        }
    }
}
