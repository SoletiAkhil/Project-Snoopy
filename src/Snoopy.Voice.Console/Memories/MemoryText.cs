namespace Snoopy.Voice.Console.Memories;

internal static class MemoryText
{
    // Keep internal punctuation and symbols so .NET, C#, and C++ remain distinct.
    public static string NormalizeForComparison(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd(' ', '.', '!', '?');
}
