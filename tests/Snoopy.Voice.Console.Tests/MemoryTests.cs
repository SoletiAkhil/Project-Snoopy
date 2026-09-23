using Snoopy.Voice.Console.Memories;

namespace Snoopy.Voice.Console.Tests;

public sealed class MemoryTests
{
    [Fact]
    public void NewMemoriesHaveUniqueIdsUtcTimestampsAndSimpleDefaults()
    {
        var before = DateTimeOffset.UtcNow;
        var first = new Memory("Prefer .NET for backend development.");
        var second = new Memory("Prefer .NET for backend development.");
        var after = DateTimeOffset.UtcNow;

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Prefer .NET for backend development.", first.Content);
        Assert.Equal(MemoryCategory.Other, first.Category);
        Assert.Equal(0, first.Importance);
        foreach (var memory in new[] { first, second })
        {
            Assert.InRange(memory.CreatedAt, before, after);
            Assert.Equal(memory.CreatedAt, memory.UpdatedAt);
            Assert.Equal(TimeSpan.Zero, memory.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, memory.UpdatedAt.Offset);
        }
    }

    [Theory]
    [InlineData(-420)]
    [InlineData(0)]
    [InlineData(330)]
    public void SuppliedTimestampsAreNormalizedToUtcWithoutChangingTheirInstants(int offsetMinutes)
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.FromMinutes(offsetMinutes));
        var updatedAt = createdAt.AddMinutes(15).ToOffset(TimeSpan.FromHours(-4));

        var memory = new Memory(id, "  Keep this exact content.\r\n", MemoryCategory.Fact, 7, createdAt, updatedAt);

        Assert.Equal(id, memory.Id);
        Assert.Equal("  Keep this exact content.\r\n", memory.Content);
        Assert.Equal(MemoryCategory.Fact, memory.Category);
        Assert.Equal(7, memory.Importance);
        Assert.Equal(createdAt.ToUniversalTime(), memory.CreatedAt);
        Assert.Equal(updatedAt.ToUniversalTime(), memory.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, memory.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, memory.UpdatedAt.Offset);
    }

    [Fact]
    public void OmittedUpdatedAtUsesTheSuppliedCreationInstantInUtc()
    {
        var createdAt = new DateTimeOffset(2026, 9, 21, 1, 0, 0, TimeSpan.FromHours(5.5));

        var memory = new Memory(Guid.NewGuid(), "A fact.", MemoryCategory.Fact, 0, createdAt);

        Assert.Equal(new DateTimeOffset(2026, 9, 20, 19, 30, 0, TimeSpan.Zero), memory.CreatedAt);
        Assert.Equal(memory.CreatedAt, memory.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, memory.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, memory.UpdatedAt.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void EmptyContentIsRejected(string? content)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => new Memory(content!));
        Assert.Equal("content", exception.ParamName);
    }

    [Fact]
    public void EmptyIdIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Memory(Guid.Empty, "A fact.", MemoryCategory.Fact, 0, DateTimeOffset.UtcNow));
        Assert.Equal("id", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void UndefinedCategoriesAreRejected(int category)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Memory("A fact.", (MemoryCategory)category));
        Assert.Equal("category", exception.ParamName);
    }

    [Fact]
    public void UpdateBeforeCreationIsRejectedByInstantRatherThanLocalClockTime()
    {
        var createdAt = new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddTicks(-1).ToOffset(TimeSpan.FromHours(5.5));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Memory(Guid.NewGuid(), "A fact.", MemoryCategory.Fact, 0, createdAt, updatedAt));

        Assert.Equal("updatedAt", exception.ParamName);
    }
}
