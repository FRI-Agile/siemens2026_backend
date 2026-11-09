using Azure.Storage.Blobs;
using FilesModule.Infrastructure.FileStorage;
using Tests.Shared;

namespace Tests.FilesModule.Infrastructure.FileStorage;

/// <summary>An empty Azurite gets the module's container and a CORS rule a browser's upload preflight passes.</summary>
/// <remarks>
/// Sets account-wide properties on the shared emulator, which is what every other test may rely on too; it only ever
/// adds what the production account also has.
/// </remarks>
public class AzuriteSetupTests
{
    [Fact]
    public async Task Creates_the_container_and_allows_a_cross_origin_upload_preflight()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        BlobServiceClient service = AzuriteTestHost.Current.Client;

        await new AzuriteSetup(service).StartAsync(ct);
        await new AzuriteSetup(service).StartAsync(ct); // a second run finds everything in place

        Assert.True(await service.GetBlobContainerClient(FileStorageRegistration.Container).ExistsAsync(ct));

        using HttpRequestMessage preflight = new(
            HttpMethod.Options, service.GetBlobContainerClient(FileStorageRegistration.Container).GetBlobClient("uploads/x.pdf").Uri);
        preflight.Headers.Add("Origin", "http://localhost:5173");
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-ms-blob-type");
        using HttpClient http = new();
        using HttpResponseMessage response = await http.SendAsync(preflight, ct);

        Assert.True(response.IsSuccessStatusCode, $"Preflight answered {(int)response.StatusCode}.");
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
