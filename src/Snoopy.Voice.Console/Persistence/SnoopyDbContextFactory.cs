using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Snoopy.Voice.Console.Configuration;

namespace Snoopy.Voice.Console.Persistence;

public sealed class SnoopyDbContextFactory : IDesignTimeDbContextFactory<SnoopyDbContext>
{
    public SnoopyDbContext CreateDbContext(string[] args)
    {
        var database = ApplicationConfiguration.LoadDatabaseOptions();
        var options = new DbContextOptionsBuilder<SnoopyDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .EnableSensitiveDataLogging(false)
            .Options;
        return new SnoopyDbContext(options);
    }
}
