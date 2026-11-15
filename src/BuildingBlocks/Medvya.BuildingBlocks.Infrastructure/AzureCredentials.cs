using Azure.Core;
using Azure.Identity;

namespace Medvya.BuildingBlocks.Infrastructure;

public static class AzureCredentials
{
    /// <summary>
    /// The one default credential of the process (the managed identity in Azure, the developer's az login locally). Shared, so the credential
    /// chain is walked once and every Blob client reuses the same cached token instead of each asking the identity endpoint for its own.
    /// </summary>
    public static readonly TokenCredential Shared = new DefaultAzureCredential();
}
