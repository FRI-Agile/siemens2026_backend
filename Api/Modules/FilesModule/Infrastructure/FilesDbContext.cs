using FilesModule.Domain;
using FilesModule.Domain.Entity;
using Microsoft.EntityFrameworkCore;

namespace FilesModule.Infrastructure;

/// <summary>The Files module's context over its own schema, <c>files</c>.</summary>
public sealed class FilesDbContext(DbContextOptions<FilesDbContext> options) : DbContext(options)
{
    public DbSet<StoredFile> Files => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleTypes.ModuleName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FilesDbContext).Assembly);
    }
}
