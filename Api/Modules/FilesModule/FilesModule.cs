using FilesModule.Domain;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.Scanning;
using FilesModule.Infrastructure.Scheduling;
using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Files;
using Kernel.Outbox;
using Kernel.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace FilesModule;

/// <summary>
/// The Files module's composition root: uploads and downloads straight between the client and object storage on
/// presigned URLs, the metadata of every uploaded file, its malware scan and its cleanup (docs/api/files/files.md).
/// </summary>
public static class FilesModule
{
    /// <summary>
    /// Registers the module's handlers and services. Call before <c>AddCqrsDecorators</c>. The object store is registered
    /// separately by the host, with <c>AddAzureBlobFileStorage</c>.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">The host's configuration, for clamd's address.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFilesModule(this IServiceCollection services, IConfiguration configuration)
    {
        // The host registers one NpgsqlDataSource ("healthyhires"); it carries the managed-identity sign-in in
        // production, so the context reuses it instead of a connection string of its own.
        services.AddDbContextPool<FilesDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<NpgsqlDataSource>(),
                npgsql => npgsql.EnableRetryOnFailure())); // ADR-A011 at the database layer

        services.AddCqrsHandlers(typeof(FilesModule).Assembly);
        services.TryAddSingleton(TimeProvider.System);

        // Every module's IFilePurpose (each registers its own with AddFilePurposes), looked up by key.
        services.AddSingleton<FilePurposeRegistry>();

        // What a module owning a referencing row checks before saving it (Kernel.Files)...
        services.AddScoped<IFileReferences, FileReferences>();

        // ...and what confirms and releases its files once the row's change reaches the outbox: FileReferenceHandler
        // (the modules' FileReference descriptors) and FileReleaseHandler (the files_released event).
        services.AddEntityChangeHandlers(typeof(FilesModule).Assembly);

        // clamd in the pod on the VM, and in the Aspire stack through service discovery (services__clamav__tcp__0).
        Uri clamd = new(configuration["services:clamav:tcp:0"] is { Length: > 0 } discovered
            ? discovered
            : "tcp://localhost:3310");
        services.AddSingleton<IMalwareScanner>(new ClamAvScanner(clamd));

        // Staff wait for the scan, so it runs often; the janitor only reclaims storage, so hourly is plenty.
        services.AddPeriodicWorker<FileScanner>(TimeSpan.FromSeconds(30));
        services.AddPeriodicWorker<FileJanitor>(TimeSpan.FromHours(1));

        return services;
    }

    /// <summary>Maps the module's endpoints under <c>/files</c> on the versioned API group.</summary>
    /// <param name="api">The versioned API group.</param>
    /// <returns>The same <paramref name="api"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapFilesModule(this IEndpointRouteBuilder api)
    {
        api.MapGroup($"/{ModuleTypes.ModuleName}")
            .WithTags(ModuleTypes.ModuleName)
            .MapEndpoints(typeof(FilesModule).Assembly);
        return api;
    }
}
