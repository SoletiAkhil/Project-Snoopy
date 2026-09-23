using System.Text.RegularExpressions;

namespace Snoopy.Voice.Console.Memories;

public sealed partial class MemoryService : IMemoryService
{
    private readonly IMemoryStore _store;

    public MemoryService(IMemoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<Memory> RememberAsync(string content, CancellationToken cancellationToken = default)
    {
        ValidateContent(content);
        cancellationToken.ThrowIfCancellationRequested();
        var memory = new Memory(content.Trim(), DetectCategory(content));
        await _store.AddAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory;
    }

    public async Task<bool> ForgetAsync(string content, CancellationToken cancellationToken = default)
    {
        ValidateContent(content);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = MemoryText.NormalizeForComparison(content);
        var memories = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var removed = false;
        foreach (var memory in memories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (normalized.Equals(MemoryText.NormalizeForComparison(memory.Content), StringComparison.OrdinalIgnoreCase))
            {
                removed |= await _store.RemoveAsync(memory.Id, cancellationToken).ConfigureAwait(false);
            }
        }
        return removed;
    }

    public Task<IReadOnlyList<Memory>> GetMemoriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _store.GetAllAsync(cancellationToken);
    }

    public Task ClearMemoriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _store.ClearAsync(cancellationToken);
    }

    private static void ValidateContent(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (MemoryText.NormalizeForComparison(content).Length == 0)
        {
            throw new ArgumentException("A memory must contain text, not just sentence-ending punctuation.", nameof(content));
        }
    }

    private static MemoryCategory DetectCategory(string content)
    {
        var categories = new List<MemoryCategory>(3);
        if (PreferencePattern().IsMatch(content))
        {
            categories.Add(MemoryCategory.Preference);
        }
        if (GoalPattern().IsMatch(content))
        {
            categories.Add(MemoryCategory.Goal);
        }
        if (PersonPattern().IsMatch(content))
        {
            categories.Add(MemoryCategory.Person);
        }

        return categories.Count switch
        {
            0 => MemoryCategory.Fact,
            1 => categories[0],
            _ => MemoryCategory.Other
        };
    }

    [GeneratedRegex(@"\b(?:prefer|preference)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreferencePattern();

    [GeneratedRegex(@"\bgoal\b|\bwant\s+to\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoalPattern();

    [GeneratedRegex(@"\bmy\s+(?:wife|son|family)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonPattern();
}
