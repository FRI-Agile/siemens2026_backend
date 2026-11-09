using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Infrastructure;

/// <summary>The Recruitment module's context over its own schema, <c>recruitment</c>.</summary>
public sealed class RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options) : DbContext(options)
{
    public DbSet<JobPost> JobPosts => Set<JobPost>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleTypes.ModuleName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecruitmentDbContext).Assembly);
        modelBuilder.AddSearchFunctions();
    }
}
