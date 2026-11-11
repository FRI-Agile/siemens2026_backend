using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Files;
using Kernel.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Npgsql;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using RecruitmentModule.Infrastructure.Files;

namespace RecruitmentModule;

/// <summary>The Recruitment module's composition root: job offers, applications, CVs, the HR workflow and its staff.</summary>
public static class RecruitmentModule
{
    /// <summary>Registers the module's handlers and services. Call before <c>AddCqrsDecorators</c>.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRecruitmentModule(this IServiceCollection services)
    {
        // The host registers one NpgsqlDataSource ("healthyhires"); it carries the managed-identity sign-in in
        // production, so the context reuses it instead of a connection string of its own. Change capture writes the
        // module's outbox in the same save as each change (conventions.md § Outbox pattern), which the dispatcher drains.
        services.AddDbContextPool<RecruitmentDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    sp.GetRequiredService<NpgsqlDataSource>(),
                    npgsql => npgsql.EnableRetryOnFailure()) // ADR-A011 at the database layer
                .AddChangeCapture(sp));
        services.AddOutboxProducer<RecruitmentDbContext>();

        services.AddCqrsHandlers(typeof(RecruitmentModule).Assembly);

        // The kinds of file the module's records reference (Infrastructure/Files), enforced by the Files module, and the
        // columns that hold them, which the Files module follows through the outbox to confirm and release the files.
        services.AddFilePurposes(typeof(RecruitmentModule).Assembly);
        services.AddSingleton(new FileReference(ModuleTypes.ModuleName, "application_document",
            [new FileColumn(nameof(ApplicationDocument.FileId), [ApplicationDocumentPurpose.PurposeKey])]));

        // Staff accounts live in Entra (iam.md §2.4). Like sign-in, the settings are required outside Development.
        services.AddOptions<StaffDirectoryOptions>()
            .BindConfiguration(StaffDirectoryOptions.SectionName)
            .Validate<IHostEnvironment>(static (options, environment) => environment.IsDevelopment() || options.IsConfigured,
                $"{StaffDirectoryOptions.SectionName}:ApiClientId and {StaffDirectoryOptions.SectionName}:UserDomain must be set outside Development, and {StaffDirectoryOptions.SectionName}:TenantId with a GraphClientId.")
            .ValidateOnStart();

        // The VM's managed identity in production, the developer's Azure CLI sign-in locally (iam.md §4, §8).
        services.AddSingleton(static sp => new GraphServiceClient(GraphCredential.Create(
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<IOptions<StaffDirectoryOptions>>().Value)));
        services.AddScoped<IStaffDirectory, GraphStaffDirectory>();

        return services;
    }

    /// <summary>Maps the module's endpoints under <c>/recruitment</c> on the versioned API group.</summary>
    /// <param name="api">The versioned API group.</param>
    /// <returns>The same <paramref name="api"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapRecruitmentModule(this IEndpointRouteBuilder api)
    {
        api.MapGroup($"/{ModuleTypes.ModuleName}")
            .WithTags(ModuleTypes.ModuleName)
            .MapEndpoints(typeof(RecruitmentModule).Assembly);
        return api;
    }
}
