using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Snoopy.Voice.Console.Configuration;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; init; } = string.Empty;

    public string GetConnectionString()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException(
                "PostgreSQL configuration is missing. Set ConnectionStrings:SnoopyDatabase in the local appsettings.json.");
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
            if (string.IsNullOrWhiteSpace(builder.Host) ||
                string.IsNullOrWhiteSpace(builder.Database) ||
                string.IsNullOrWhiteSpace(builder.Username))
            {
                throw new ArgumentException("Required connection settings are missing.");
            }

            builder.IncludeErrorDetail = false;
            builder.LogParameters = false;
            builder.PersistSecurityInfo = false;
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                "PostgreSQL connection string is invalid. Check ConnectionStrings:SnoopyDatabase locally; " +
                "Host, Database and Username are required. Connection details are not displayed.");
        }
    }

    internal static DatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new DatabaseOptions
        {
            ConnectionString = configuration.GetConnectionString("SnoopyDatabase") ?? string.Empty
        };
        options.GetConnectionString();
        return options;
    }

    public override string ToString() => "DatabaseOptions { ConnectionString = [REDACTED] }";
}
