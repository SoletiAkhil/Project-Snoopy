namespace Snoopy.Voice.Console.Memories;

public sealed record Memory
{
    public Memory(string content, MemoryCategory category = MemoryCategory.Other, int importance = 0)
        : this(Guid.NewGuid(), content, category, importance, DateTimeOffset.UtcNow)
    {
    }

    public Memory(
        Guid id, string content, MemoryCategory category, int importance,
        DateTimeOffset createdAt, DateTimeOffset? updatedAt = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A memory ID must not be empty.", nameof(id));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), "The memory category is not supported.");
        }

        var lastUpdated = updatedAt ?? createdAt;
        if (lastUpdated < createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAt), "A memory cannot be updated before it was created.");
        }

        Id = id;
        Content = content;
        Category = category;
        Importance = importance;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = lastUpdated.ToUniversalTime();
    }

    public Guid Id { get; }
    public string Content { get; }
    public MemoryCategory Category { get; }
    public int Importance { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
}
