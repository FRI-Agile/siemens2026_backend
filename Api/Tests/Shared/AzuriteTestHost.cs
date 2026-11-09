using Azure.Storage.Blobs;
using Testcontainers.Azurite;

namespace Tests.Shared;

/// <summary>
/// One Azurite container for the whole test run, standing in for the production Azure Blob Storage behind the Files
/// module. Its well-known account key lets the client sign SAS itself, where production signs with a user delegation
/// key; the URLs grant the same. Started on first use; each test makes its own container.
/// </summary>
internal sealed class AzuriteTestHost
{
    private const string Image = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    private static readonly Lazy<AzuriteTestHost> Instance = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    private AzuriteTestHost(BlobServiceClient client) => Client = client;

    /// <summary>The shared host, started on first use.</summary>
    public static AzuriteTestHost Current => Instance.Value;

    /// <summary>An account client built from the emulator's connection string.</summary>
    public BlobServiceClient Client { get; }

    /// <summary>Creates a container with a unique name and returns it.</summary>
    /// <param name="ct">Token to cancel the call.</param>
    public async Task<string> NewContainerAsync(CancellationToken ct)
    {
        string name = $"test-{Guid.NewGuid():N}";
        await Client.CreateBlobContainerAsync(name, cancellationToken: ct);
        return name;
    }

    private static AzuriteTestHost Start()
    {
        // In memory, and without the API version check, so the SDK may be newer than the emulator.
        AzuriteContainer container = new AzuriteBuilder(Image)
            .WithInMemoryPersistence()
            .WithCommand("--skipApiVersionCheck")
            .Build();
        container.StartAsync().GetAwaiter().GetResult();
        return new AzuriteTestHost(new BlobServiceClient(container.GetConnectionString()));
    }
}
