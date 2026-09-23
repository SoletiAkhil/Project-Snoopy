using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Snoopy.Voice.Console.Memories;
using Snoopy.Voice.Console.Persistence;

namespace Snoopy.Voice.Console.Tests;

public sealed class SnoopyDbContextTests
{
    private const string PersistedContent = "  private-memory-content-marker\r\nKeep whitespace and punctuation!\t";
    private static readonly Guid MemoryId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly DateTimeOffset CreatedAt =
        new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero).AddTicks(1234);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddDays(30).AddTicks(1);

    [Fact]
    public void ModelContainsOnlyTheMemoryTableAndNoConversationHistoryEntity()
    {
        using var context = CreateContext();
        var model = context.Model;
        var entityType = Assert.Single(model.GetEntityTypes());
        var table = Assert.Single(model.GetRelationalModel().Tables);
        string[] expectedProperties =
        [
            nameof(MemoryEntity.Category),
            nameof(MemoryEntity.Content),
            nameof(MemoryEntity.CreatedAt),
            nameof(MemoryEntity.Id),
            nameof(MemoryEntity.Importance),
            nameof(MemoryEntity.UpdatedAt)
        ];

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal(typeof(MemoryEntity), entityType.ClrType);
        Assert.Null(model.FindEntityType(typeof(Memory)));
        Assert.Equal("Memories", entityType.GetTableName());
        Assert.Null(entityType.GetSchema());
        Assert.Equal("Memories", table.Name);
        Assert.Null(table.Schema);
        Assert.Same(context.Set<MemoryEntity>(), context.Memories);
        Assert.Equal(expectedProperties,
            entityType.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(expectedProperties,
            table.Columns.Select(column => column.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty(entityType.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
        Assert.Empty(entityType.GetSkipNavigations());
    }

    [Fact]
    public void IdIsTheOnlyKeyIsNeverGeneratedAndHasNoSecondaryIndexes()
    {
        using var context = CreateContext();
        var entityType = Assert.Single(context.Model.GetEntityTypes());
        var key = Assert.Single(entityType.GetKeys());
        var id = Assert.Single(key.Properties);
        var table = Assert.Single(context.Model.GetRelationalModel().Tables);
        var relationalKey = table.PrimaryKey;

        Assert.Same(key, entityType.FindPrimaryKey());
        Assert.Equal("PK_Memories", key.GetName());
        Assert.Equal(nameof(MemoryEntity.Id), id.Name);
        Assert.Equal(typeof(Guid), id.ClrType);
        Assert.Equal("uuid", id.GetColumnType());
        Assert.False(id.IsNullable);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
        Assert.Null(id.GetDefaultValueSql());
        Assert.Null(id.GetComputedColumnSql());
        Assert.NotNull(relationalKey);
        Assert.Equal("PK_Memories", relationalKey.Name);
        Assert.Equal(nameof(MemoryEntity.Id), Assert.Single(relationalKey.Columns).Name);
        Assert.Empty(entityType.GetIndexes());
        Assert.Empty(table.Indexes);
    }

    [Theory]
    [InlineData(nameof(MemoryEntity.Id), typeof(Guid), "uuid")]
    [InlineData(nameof(MemoryEntity.Content), typeof(string), "text")]
    [InlineData(nameof(MemoryEntity.Category), typeof(string), "text")]
    [InlineData(nameof(MemoryEntity.Importance), typeof(int), "integer")]
    [InlineData(nameof(MemoryEntity.CreatedAt), typeof(DateTimeOffset), "timestamp with time zone")]
    [InlineData(nameof(MemoryEntity.UpdatedAt), typeof(DateTimeOffset), "timestamp with time zone")]
    public void EveryPropertyHasTheRequiredRelationalColumnMapping(
        string propertyName, Type clrType, string columnType)
    {
        using var context = CreateContext();
        var entityType = Assert.Single(context.Model.GetEntityTypes());
        var table = Assert.Single(context.Model.GetRelationalModel().Tables);
        var storeObject = StoreObjectIdentifier.Table(table.Name, table.Schema);
        var property = entityType.FindProperty(propertyName);
        var column = table.FindColumn(propertyName);

        Assert.NotNull(property);
        Assert.Equal(clrType, property.ClrType);
        Assert.Equal(propertyName, property.GetColumnName(storeObject));
        Assert.Equal(columnType, property.GetColumnType());
        Assert.False(property.IsNullable);
        Assert.NotNull(column);
        Assert.Equal(columnType, column.StoreType);
        Assert.False(column.IsNullable);
    }

    [Fact]
    public void EveryCategoryIsStoredAsItsExactEnumNameAndRoundTripsEveryMemoryProperty()
    {
        foreach (var category in Enum.GetValues<MemoryCategory>())
        {
            var memory = new Memory(MemoryId, PersistedContent, category, 7, CreatedAt, UpdatedAt);

            var entity = MemoryEntity.FromMemory(memory);
            var restored = entity.ToMemory();

            Assert.Equal(memory.Id, entity.Id);
            Assert.Equal(memory.Content, entity.Content);
            Assert.Equal(category.ToString(), entity.Category);
            Assert.Equal(memory.Importance, entity.Importance);
            Assert.Equal(CreatedAt, entity.CreatedAt);
            Assert.Equal(UpdatedAt, entity.UpdatedAt);
            Assert.Equal(TimeSpan.Zero, entity.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, entity.UpdatedAt.Offset);
            Assert.Equal(memory, restored);
            Assert.Equal(TimeSpan.Zero, restored.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, restored.UpdatedAt.Offset);
        }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("00112233-4455-6677-8899-aabbccddeeff")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void SuppliedIdsAreNeverRegeneratedDuringMapping(string value)
    {
        var id = Guid.Parse(value);
        var memory = new Memory(id, PersistedContent, MemoryCategory.Fact, 0, CreatedAt, UpdatedAt);

        var entity = MemoryEntity.FromMemory(memory);

        Assert.Equal(id, entity.Id);
        Assert.Equal(id, entity.ToMemory().Id);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("  Keep leading and trailing spaces.  ")]
    [InlineData("\tFirst line\r\nSecond line\n\"quoted\" \\ slash\t")]
    [InlineData("\u00E9 \u03A9 \u4F60\u597D \uD83D\uDE80")]
    public void ContentIsPreservedWithoutTrimmingOrRewriting(string content)
    {
        var memory = new Memory(MemoryId, content, MemoryCategory.Other, 0, CreatedAt, UpdatedAt);

        var entity = MemoryEntity.FromMemory(memory);

        Assert.Equal(content, entity.Content);
        Assert.Equal(content, entity.ToMemory().Content);
    }

    [Fact]
    public void LongContentIsNotTruncatedDuringMapping()
    {
        var content = new string('x', 65_536);
        var memory = new Memory(MemoryId, content, MemoryCategory.Fact, 0, CreatedAt, UpdatedAt);

        var entity = MemoryEntity.FromMemory(memory);

        Assert.Equal(content, entity.Content);
        Assert.Equal(content, entity.ToMemory().Content);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void ImportanceIsPreservedAcrossTheEntireIntegerRange(int importance)
    {
        var memory = new Memory(MemoryId, PersistedContent, MemoryCategory.Goal, importance, CreatedAt, UpdatedAt);

        var entity = MemoryEntity.FromMemory(memory);

        Assert.Equal(importance, entity.Importance);
        Assert.Equal(importance, entity.ToMemory().Importance);
    }

    [Theory]
    [InlineData(-840)]
    [InlineData(-420)]
    [InlineData(0)]
    [InlineData(330)]
    [InlineData(840)]
    public void PersistedOffsetsNormalizeToUtcWithoutChangingInstantsOrMutatingTheEntity(int offsetMinutes)
    {
        var createdAt = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.FromMinutes(offsetMinutes)).AddTicks(1234);
        var updatedAt = createdAt.AddMinutes(15).ToOffset(TimeSpan.FromHours(-4));
        var entity = CreateValidEntity();
        entity.CreatedAt = createdAt;
        entity.UpdatedAt = updatedAt;

        var memory = entity.ToMemory();
        var remapped = MemoryEntity.FromMemory(memory);

        Assert.Equal(createdAt.ToUniversalTime(), memory.CreatedAt);
        Assert.Equal(updatedAt.ToUniversalTime(), memory.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, memory.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, memory.UpdatedAt.Offset);
        Assert.True(createdAt.EqualsExact(entity.CreatedAt));
        Assert.True(updatedAt.EqualsExact(entity.UpdatedAt));
        Assert.Equal(memory.CreatedAt, remapped.CreatedAt);
        Assert.Equal(memory.UpdatedAt, remapped.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, remapped.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, remapped.UpdatedAt.Offset);
    }

    [Fact]
    public void EqualTimestampsAndDateTimeOffsetExtremesArePreservedWithoutRestamping()
    {
        foreach (var (createdAt, updatedAt) in new[]
        {
            (DateTimeOffset.MinValue, DateTimeOffset.MinValue),
            (DateTimeOffset.MinValue, DateTimeOffset.MaxValue),
            (DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
            (CreatedAt, CreatedAt)
        })
        {
            var memory = new Memory(MemoryId, PersistedContent, MemoryCategory.Event, 0, createdAt, updatedAt);

            var entity = MemoryEntity.FromMemory(memory);
            var restored = entity.ToMemory();

            Assert.True(createdAt.EqualsExact(entity.CreatedAt));
            Assert.True(updatedAt.EqualsExact(entity.UpdatedAt));
            Assert.True(createdAt.EqualsExact(restored.CreatedAt));
            Assert.True(updatedAt.EqualsExact(restored.UpdatedAt));
            Assert.Equal(memory, restored);
        }
    }

    [Fact]
    public void InvalidPersistedIdProducesASanitizedReadFailure()
    {
        var entity = CreateValidEntity();
        entity.Id = Guid.Empty;

        AssertSanitizedReadFailure(entity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void InvalidPersistedContentProducesASanitizedReadFailure(string? content)
    {
        var entity = CreateValidEntity();
        entity.Content = content!;

        AssertSanitizedReadFailure(entity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("private-category-marker")]
    [InlineData("fact")]
    [InlineData("FACT")]
    [InlineData(" Fact")]
    [InlineData("Fact ")]
    [InlineData("\tFact\r\n")]
    [InlineData("Fact, Preference")]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("-1")]
    [InlineData("2147483647")]
    public void InvalidOrNonCanonicalPersistedCategoriesProduceASanitizedReadFailure(string? category)
    {
        var entity = CreateValidEntity();
        entity.Category = category!;

        var exception = AssertSanitizedReadFailure(entity);

        if (!string.IsNullOrWhiteSpace(category))
        {
            Assert.DoesNotContain(category, exception.Message);
        }
    }

    [Theory]
    [InlineData(-840)]
    [InlineData(-420)]
    [InlineData(0)]
    [InlineData(330)]
    [InlineData(840)]
    public void PersistedUpdateBeforeCreationIsSanitizedUsingInstantsRatherThanLocalClockTimes(int offsetMinutes)
    {
        var entity = CreateValidEntity();
        entity.UpdatedAt = entity.CreatedAt.AddTicks(-1).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

        AssertSanitizedReadFailure(entity);
    }

    private static SnoopyDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SnoopyDbContext>()
            .UseNpgsql("Host=localhost;Database=snoopy_metadata_only;Username=offline-test")
            .Options;
        return new SnoopyDbContext(options);
    }

    private static MemoryEntity CreateValidEntity() => new()
    {
        Id = MemoryId,
        Content = PersistedContent,
        Category = nameof(MemoryCategory.Fact),
        Importance = 7,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };

    private static MemoryStorageException AssertSanitizedReadFailure(MemoryEntity entity)
    {
        var exception = Assert.Throws<MemoryStorageException>(() => entity.ToMemory());

        Assert.Equal(MemoryStorageOperation.Read, exception.Operation);
        Assert.Equal(new MemoryStorageException(MemoryStorageOperation.Read).Message, exception.Message);
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(PersistedContent, exception.ToString());
        Assert.DoesNotContain("private-category-marker", exception.ToString());
        Assert.DoesNotContain(entity.Id.ToString("D"), exception.ToString());
        Assert.DoesNotContain(entity.CreatedAt.ToString("O"), exception.ToString());
        Assert.DoesNotContain(entity.UpdatedAt.ToString("O"), exception.ToString());
        return exception;
    }
}
