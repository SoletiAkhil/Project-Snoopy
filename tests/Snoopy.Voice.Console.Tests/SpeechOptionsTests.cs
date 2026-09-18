using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Tests;

public sealed class SpeechOptionsTests
{
    [Fact]
    public void ProgrammaticOptionsHaveDocumentedDefaults()
    {
        var options = new SpeechOptions { SubscriptionKey = "test-placeholder" };
        options.Validate();
        Assert.Equal("centralindia", options.Region);
        Assert.Equal("en-IN-NeerjaNeural", options.VoiceName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingKeyIsRejectedWithoutExposingValues(string? key)
    {
        var exception = Assert.Throws<ArgumentException>(() => Load(key: key));
        Assert.Equal(SpeechOptions.MissingConfigurationMessage, exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void MissingRegionIsRejectedWithoutExposingKey(string? region)
    {
        var exception = Assert.Throws<ArgumentException>(() => Load(region: region));
        Assert.Equal(SpeechOptions.MissingConfigurationMessage, exception.Message);
        Assert.DoesNotContain("test-placeholder", exception.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingVoiceUsesDefault(string? voice)
    {
        Assert.Equal("en-IN-NeerjaNeural", Load(voice: voice).VoiceName);
    }

    [Fact]
    public void CustomConfigurationIsReadAndTrimmed()
    {
        var options = Load(" test-placeholder ", " eastus ", " en-US-JennyNeural ");
        Assert.Equal("test-placeholder", options.SubscriptionKey);
        Assert.Equal("eastus", options.Region);
        Assert.Equal("en-US-JennyNeural", options.VoiceName);
        Assert.DoesNotContain("test-placeholder", options.ToString());
    }

    [Theory]
    [InlineData("Central India")]
    [InlineData("https://centralindia.api.cognitive.microsoft.com")]
    [InlineData("centralindia/")]
    public void InvalidRegionFormatIsRejected(string region)
    {
        var exception = Assert.Throws<ArgumentException>(() => Load(region: region));
        Assert.Contains("identifier", exception.Message);
        Assert.DoesNotContain("test-placeholder", exception.ToString());
    }

    [Fact]
    public void ExplicitEmptyVoiceIsInvalid()
    {
        Assert.Throws<ArgumentException>(() => new SpeechOptions
        {
            SubscriptionKey = "test-placeholder",
            VoiceName = " "
        }.Validate());
    }

    private static SpeechOptions Load(
        string? key = "test-placeholder", string? region = "centralindia", string? voice = null) =>
        SpeechOptions.FromEnvironment(name => name switch
        {
            "SNOOPY_SPEECH_KEY" => key,
            "SNOOPY_SPEECH_REGION" => region,
            "SNOOPY_SPEECH_VOICE" => voice,
            _ => throw new InvalidOperationException("Unexpected environment variable requested.")
        });
}
