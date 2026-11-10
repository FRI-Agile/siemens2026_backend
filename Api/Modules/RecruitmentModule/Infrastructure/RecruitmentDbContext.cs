using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure;

/// <summary>
/// The Recruitment module's context over its own schema, <c>recruitment</c>, and its outbox: the changes of its
/// <see cref="ITrackedEntity"/> entities and the events it publishes, for other modules (conventions.md § Outbox pattern).
/// </summary>
public sealed class RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<JobPost> JobPosts => Set<JobPost>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleTypes.ModuleName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecruitmentDbContext).Assembly);
        modelBuilder.AddSearchFunctions();
        modelBuilder.AddOutbox();
    }
}
