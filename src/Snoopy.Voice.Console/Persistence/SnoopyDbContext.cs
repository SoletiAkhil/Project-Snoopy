using Microsoft.EntityFrameworkCore;

namespace Snoopy.Voice.Console.Persistence;

public sealed class SnoopyDbContext(DbContextOptions<SnoopyDbContext> options) : DbContext(options)
{
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var memory = modelBuilder.Entity<MemoryEntity>();
        memory.ToTable("Memories");
        memory.HasKey(item => item.Id);
        memory.Property(item => item.Id).HasColumnType("uuid").ValueGeneratedNever();
        memory.Property(item => item.Content).HasColumnType("text").IsRequired();
        memory.Property(item => item.Category).HasColumnType("text").IsRequired();
        memory.Property(item => item.Importance).HasColumnType("integer");
        memory.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
        memory.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone");
    }
}
