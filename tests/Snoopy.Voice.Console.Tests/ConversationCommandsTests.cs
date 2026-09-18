using Snoopy.Voice.Console.AI;

namespace Snoopy.Voice.Console.Tests;

public sealed class ConversationCommandsTests
{
    [Theory]
    [InlineData("reset conversation")]
    [InlineData("new conversation")]
    [InlineData("RESET CONVERSATION")]
    [InlineData("New Conversation.")]
    [InlineData("  reset   conversation  ")]
    [InlineData("new\tconversation")]
    [InlineData("Reset, conversation!")]
    [InlineData("\u201cNew conversation.\u201d")]
    public void RecognizesOnlyWholeResetCommandsIgnoringCaseAndSttPunctuation(string text)
    {
        Assert.True(ConversationCommands.IsReset(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("!!!")]
    [InlineData("reset")]
    [InlineData("conversation")]
    [InlineData("reset conversations")]
    [InlineData("resetconversation")]
    [InlineData("newconversation")]
    [InlineData("please reset conversation")]
    [InlineData("how do I reset conversation?")]
    [InlineData("do not reset conversation")]
    [InlineData("start a new conversation")]
    [InlineData("what is a new conversation?")]
    [InlineData("reset conversation later")]
    [InlineData("exit")]
    [InlineData("quit")]
    [InlineData("goodbye")]
    [InlineData("stop")]
    [InlineData("My name is Akhil.")]
    public void OrdinarySentencesAndExitCommandsDoNotResetHistory(string? text)
    {
        Assert.False(ConversationCommands.IsReset(text));
    }
}
