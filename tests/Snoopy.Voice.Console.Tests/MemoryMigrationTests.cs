using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Snoopy.Voice.Console.Persistence;

namespace Snoopy.Voice.Console.Tests;

public sealed class MemoryMigrationTests
{
    [Fact]
    public void InitialMigrationMatchesCurrentModelAndGeneratesOnlyMemorySchemaChanges()
    {
        var options = new DbContextOptionsBuilder<SnoopyDbContext>()
            .UseNpgsql("Host=localhost;Database=snoopy_tests;Username=offline-test")
            .Options;
        using var context = new SnoopyDbContext(options);

        Assert.Contains(context.Database.GetMigrations(), name => name.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("CREATE TABLE \"Memories\"", sql);
        Assert.Contains("\"Id\" uuid NOT NULL", sql);
        Assert.Contains("\"Content\" text NOT NULL", sql);
        Assert.Contains("\"Category\" text NOT NULL", sql);
        Assert.Contains("\"Importance\" integer NOT NULL", sql);
        Assert.Contains("\"CreatedAt\" timestamp with time zone NOT NULL", sql);
        Assert.Contains("\"UpdatedAt\" timestamp with time zone NOT NULL", sql);
        Assert.Contains("PRIMARY KEY (\"Id\")", sql);
        Assert.DoesNotContain("DROP ", sql);
        Assert.DoesNotContain("DELETE ", sql);
        Assert.DoesNotContain("Conversation", sql);
        Assert.DoesNotContain("vector", sql, StringComparison.OrdinalIgnoreCase);
    }
}
