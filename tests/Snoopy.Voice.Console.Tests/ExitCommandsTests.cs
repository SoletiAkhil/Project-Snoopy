using Snoopy.Voice.Console.AI;

namespace Snoopy.Voice.Console.Tests;

public sealed class ExitCommandsTests
{
    [Theory]
    [InlineData("exit")]
    [InlineData("quit")]
    [InlineData("goodbye")]
    [InlineData("stop")]
    [InlineData("EXIT")]
    [InlineData("QuIt")]
    [InlineData("GOODBYE")]
    [InlineData("StOp")]
    [InlineData("Snoopy exit")]
    [InlineData("Snoopy quit")]
    [InlineData("Snoopy goodbye")]
    [InlineData("Snoopy stop")]
    [InlineData("exit Snoopy")]
    [InlineData("quit Snoopy")]
    [InlineData("goodbye Snoopy")]
    [InlineData("stop Snoopy")]
    [InlineData("Goodbye, Snoopy!")]
    [InlineData("SNOOPY, STOP.")]
    [InlineData("  Snoopy...QUIT!?  ")]
    [InlineData(" \t(Exit!)\r\n")]
    [InlineData("\"goodbye\"")]
    [InlineData("Snoopy,stop")]
    [InlineData("stop,Snoopy")]
    [InlineData("\u201cGoodbye, Snoopy!\u201d")]
    [InlineData("Snoopy\u00a0stop")]
    [InlineData("\tSnoopy,\nexit.\r\n")]
    [InlineData("Snoopy\u2014stop.")]
    [InlineData("stop\u2014Snoopy.")]
    public void RecognizesWholeCommandsWithOptionalNameAndSttPunctuation(string text)
    {
        Assert.True(ExitCommands.IsExit(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("!!!")]
    [InlineData("Snoopy")]
    [InlineData("Hello")]
    [InlineData("what is my name?")]
    [InlineData("My name is Akhil.")]
    [InlineData("how do I quit smoking")]
    [InlineData("do not stop")]
    [InlineData("stop the music")]
    [InlineData("please exit")]
    [InlineData("Goodbye for now")]
    [InlineData("please stop Snoopy")]
    [InlineData("Snoopy stop the music")]
    [InlineData("Snoopy do not stop")]
    [InlineData("Can you say goodbye?")]
    [InlineData("what does exit mean?")]
    [InlineData("exit the application")]
    [InlineData("I quit")]
    [InlineData("don't stop")]
    [InlineData("nonstop")]
    [InlineData("stopping")]
    [InlineData("quitters")]
    [InlineData("goodbyes")]
    [InlineData("exitSnoopy")]
    [InlineData("Snoopygoodbye")]
    [InlineData("good bye")]
    [InlineData("ex-it")]
    [InlineData("s.t.o.p.")]
    [InlineData("Snoopy Snoopy stop")]
    [InlineData("Snoopy stop Snoopy")]
    [InlineData("stop quit")]
    [InlineData("stop 123")]
    [InlineData("$stop")]
    [InlineData("st\u043ep")]
    public void RejectsSentencesContainingCommandsAndUnrelatedPhrases(string? text)
    {
        Assert.False(ExitCommands.IsExit(text));
    }
}
