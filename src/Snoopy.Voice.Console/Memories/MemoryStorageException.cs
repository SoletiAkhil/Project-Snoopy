namespace Snoopy.Voice.Console.Memories;

public enum MemoryStorageOperation
{
    Save,
    Read,
    Remove,
    Clear,
    Migrate
}

public sealed class MemoryStorageException(MemoryStorageOperation operation) : Exception(GetMessage(operation))
{
    public MemoryStorageOperation Operation { get; } = operation;

    private static string GetMessage(MemoryStorageOperation operation) => operation switch
    {
        MemoryStorageOperation.Migrate =>
            "Unable to apply memory database migrations. Check local PostgreSQL availability, connection settings " +
            "and schema permissions. Database details are not displayed.",
        _ =>
            $"Unable to complete the persistent memory {operation.ToString().ToLowerInvariant()} operation. " +
            "Check local PostgreSQL availability, connection settings and that --migrate-database has completed. " +
            "Database details are not displayed."
    };
}
