using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Persistence;

public sealed class MemoryEntity
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Importance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    internal static MemoryEntity FromMemory(Memory memory) => new()
    {
        Id = memory.Id,
        Content = memory.Content,
        Category = memory.Category.ToString(),
        Importance = memory.Importance,
        CreatedAt = memory.CreatedAt,
        UpdatedAt = memory.UpdatedAt
    };

    internal Memory ToMemory()
    {
        if (!Enum.TryParse<MemoryCategory>(Category, out var category) ||
            !Enum.IsDefined(category) || category.ToString() != Category)
        {
            throw new MemoryStorageException(MemoryStorageOperation.Read);
        }

        try
        {
            return new Memory(Id, Content, category, Importance, CreatedAt, UpdatedAt);
        }
        catch (ArgumentException)
        {
            throw new MemoryStorageException(MemoryStorageOperation.Read);
        }
    }
}
