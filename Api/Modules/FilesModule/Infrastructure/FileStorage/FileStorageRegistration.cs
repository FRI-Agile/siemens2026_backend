using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FilesModule.Infrastructure.FileStorage;

/// <summary>Registers the object store behind <see cref="IFileStorage"/> (docs/api/files/files.md §12.1).</summary>
public static class FileStorageRegistration
{
    /// <summary>The container the module's objects live in.</summary>
    public const string Container = "files";

    /// <summary>
    /// Stores files in Azure Blob Storage, in the <see cref="Container"/> container, through the
    /// <see cref="BlobServiceClient"/> the host registered (<c>AddAzureBlobServiceClient("blobs")</c>, which also adds the
    /// <c>/health</c> check): the storage account in production, Azurite locally and in the CI scan. In production the
    /// container is provisioned by infrastructure (<c>infra/main.bicep</c>); in Development an empty Azurite is prepared
    /// at startup (<see cref="AzuriteSetup"/>).
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="environment">The host environment, which decides whether Azurite is prepared.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAzureBlobFileStorage(this IServiceCollection services, IHostEnvironment environment)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFileStorage>(sp => new AzureBlobFileStorage(
            sp.GetRequiredService<BlobServiceClient>(), Container, sp.GetRequiredService<TimeProvider>()));

        if (environment.IsDevelopment())
            services.AddHostedService<AzuriteSetup>();

        return services;
    }
}

/// <summary>
/// Prepares Azurite before the API serves its first request: it starts empty on every AppHost run, with neither the
/// module's container nor the CORS rule the storage account gets from <c>infra/modules/storage.bicep</c>, without which
/// a browser's upload from the Vite dev server fails its preflight.
/// </summary>
/// <remarks>
/// Only for a client signed with an account key, which Azurite's connection string carries and the production account
/// refuses: a Development run pointed at a real account with a managed identity leaves it alone.
/// </remarks>
internal sealed class AzuriteSetup(BlobServiceClient service) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!service.CanGenerateAccountSasUri)
            return;

        await service.GetBlobContainerClient(FileStorageRegistration.Container)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        BlobServiceProperties properties = (await service.GetPropertiesAsync(cancellationToken)).Value;
        properties.Cors =
        [
            new BlobCorsRule
            {
                // Any local origin: the Vite dev server, the edge on :8088, a LAN address for a phone.
                AllowedOrigins = "*",
                AllowedMethods = "GET,HEAD,PUT,OPTIONS",
                AllowedHeaders = "*",
                ExposedHeaders = "*",
                MaxAgeInSeconds = 3600,
            },
        ];
        await service.SetPropertiesAsync(properties, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
