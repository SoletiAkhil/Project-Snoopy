using Microsoft.Extensions.Configuration;

namespace Snoopy.Voice.Console.Configuration;

public sealed class SpeechOptions
{
    public const string DefaultRegion = "centralindia";
    public const string DefaultVoiceName = "en-IN-NeerjaNeural";
    public const string MissingConfigurationMessage =
        "Azure Speech configuration is missing.\n\n" +
        "Set AzureSpeech:ApiKey and AzureSpeech:Region in the local appsettings.json.";

    public string SubscriptionKey { get; init; } = string.Empty;
    public string Region { get; init; } = DefaultRegion;
    public string VoiceName { get; init; } = DefaultVoiceName;

    public static SpeechOptions FromConfiguration(IConfiguration configuration)
    {
        var voice = configuration["AzureSpeech:VoiceName"];
        var options = new SpeechOptions
        {
            SubscriptionKey = configuration["AzureSpeech:ApiKey"]?.Trim() ?? string.Empty,
            Region = configuration["AzureSpeech:Region"]?.Trim() ?? string.Empty,
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
