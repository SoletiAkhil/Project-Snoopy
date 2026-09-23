using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Tests;

public sealed class MemoryCommandParserTests
{
    private readonly IMemoryCommandParser _parser = new MemoryCommandParser();

    [Theory]
    [InlineData("remember that I prefer .NET", MemoryCommandType.Remember, "I prefer .NET")]
    [InlineData("remember I prefer .NET", MemoryCommandType.Remember, "I prefer .NET")]
    [InlineData("please remember that I like C#", MemoryCommandType.Remember, "I like C#")]
    [InlineData("please remember I like C#", MemoryCommandType.Remember, "I like C#")]
    [InlineData("forget that I prefer .NET", MemoryCommandType.Forget, "I prefer .NET")]
    [InlineData("forget I prefer .NET", MemoryCommandType.Forget, "I prefer .NET")]
    [InlineData("please forget that I like C#", MemoryCommandType.Forget, "I like C#")]
    [InlineData("please forget I like C#", MemoryCommandType.Forget, "I like C#")]
    [InlineData("PLEASE ReMeMbEr THAT I prefer C++.", MemoryCommandType.Remember, "I prefer C++.")]
    [InlineData("Please FORGET That I prefer .NET.", MemoryCommandType.Forget, "I prefer .NET.")]
    [InlineData(" \t please \n remember \t that  I  prefer .NET.\r\n", MemoryCommandType.Remember, "I  prefer .NET.")]
    [InlineData(" \r\n forget \t I prefer C#.  ", MemoryCommandType.Forget, "I prefer C#.")]
    [InlineData("Please, remember that .NET and C# matter.", MemoryCommandType.Remember, ".NET and C# matter.")]
    [InlineData("remember thatched roofs", MemoryCommandType.Remember, "thatched roofs")]
    [InlineData("remember clear my memories", MemoryCommandType.Remember, "clear my memories")]
    [InlineData("remember forget everything you remember.", MemoryCommandType.Remember, "forget everything you remember.")]
    [InlineData("forget everything you remember about .NET.", MemoryCommandType.Forget, "everything you remember about .NET.")]
    public void RecognizesExplicitPrefixesAndPreservesContent(
        string text, MemoryCommandType commandType, string content)
    {
        Assert.Equal(new MemoryCommandResult(commandType, content), _parser.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData("I prefer .NET for backend development.")]
    [InlineData("My favorite programming language is C#.")]
    [InlineData("I want to remember this.")]
    [InlineData("Can you remember that I like C#?")]
    [InlineData("Do not forget my name.")]
    [InlineData("remembering that I like C#")]
    [InlineData("remember-me")]
    [InlineData("forgetful")]
    [InlineData("Please")]
    [InlineData("please remember-me")]
    [InlineData("What do you remember about my work?")]
    [InlineData("What do you remember about me today?")]
    [InlineData("Clear my memories after lunch.")]
    public void NonCommandsAndEmptyInputReturnNone(string? text)
    {
        Assert.Equal(new MemoryCommandResult(MemoryCommandType.None), _parser.Parse(text));
    }

    [Theory]
    [InlineData("What do you remember about me?", MemoryCommandType.Recall)]
    [InlineData("What do you remember?", MemoryCommandType.Recall)]
    [InlineData(" \t WHAT  DO YOU \r\n REMEMBER ABOUT ME?! ", MemoryCommandType.Recall)]
    [InlineData("Please, what do you remember?", MemoryCommandType.Recall)]
    [InlineData("Forget everything you remember.", MemoryCommandType.Clear)]
    [InlineData("Clear my memories.", MemoryCommandType.Clear)]
    [InlineData(" \n FORGET  EVERYTHING YOU\tREMEMBER! ", MemoryCommandType.Clear)]
    [InlineData("Please clear my memories.", MemoryCommandType.Clear)]
    [InlineData("please forget everything you remember", MemoryCommandType.Clear)]
    public void RecognizesWholeRecallAndClearCommandsBeforeForgetPrefix(string text, MemoryCommandType commandType)
    {
        Assert.Equal(new MemoryCommandResult(commandType), _parser.Parse(text));
    }

    [Theory]
    [InlineData("remember", MemoryCommandType.Remember, "")]
    [InlineData("remember!", MemoryCommandType.Remember, "")]
    [InlineData("please remember that", MemoryCommandType.Remember, "")]
    [InlineData("remember that...", MemoryCommandType.Remember, "")]
    [InlineData("remember .?!", MemoryCommandType.Remember, ".?!")]
    [InlineData("forget", MemoryCommandType.Forget, "")]
    [InlineData("please forget that.", MemoryCommandType.Forget, "")]
    [InlineData("forget that .!?", MemoryCommandType.Forget, ".!?")]
    public void IncompleteCommandsStayLocalSoApplicationCanAskForContent(
        string text, MemoryCommandType commandType, string content)
    {
        Assert.Equal(new MemoryCommandResult(commandType, content), _parser.Parse(text));
    }
}
