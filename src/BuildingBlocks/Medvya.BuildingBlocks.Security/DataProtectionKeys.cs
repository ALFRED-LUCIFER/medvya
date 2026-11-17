using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Medvya.BuildingBlocks.Security;

/// <summary>
/// The data-protection key ring that signs and encrypts staff cookies (and their step-up time), app bearer tokens and link tokens. Outside
/// Development it lives in Blob (container <c>data-protection-{applicationName}</c>, one per host) and every key is wrapped by the host's own Key Vault key
/// <c>DataProtection:KeyVaultKeyId</c> (infra/bicep/main.bicep: one host's identity cannot reach the other's ring), both with the app's managed identity: a copy of the files alone forges nothing, and every instance
/// shares one ring. A deployed host (<paramref name="deployed"/>: it has its database) without them does not start, never falling back to keys
/// on the local disk. Development keeps the default.
/// </summary>
public static class DataProtectionKeys
{
    /// <param name="deployed">The host has its database configured. Hosts started without one (tests, the build-time OpenAPI export) serve nothing worth a key ring.</param>
    public static IHostApplicationBuilder AddMedvyaDataProtection(this IHostApplicationBuilder builder, string applicationName, bool deployed)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var dp = builder.Services.AddDataProtection().SetApplicationName(applicationName);
        if (builder.Environment.IsDevelopment() || !deployed) return builder;
        if (!Uri.TryCreate(builder.Configuration["Storage:BlobEndpoint"], UriKind.Absolute, out var blob) || !Uri.TryCreate(builder.Configuration["DataProtection:KeyVaultKeyId"], UriKind.Absolute, out var key))
            throw new InvalidOperationException("Storage:BlobEndpoint and DataProtection:KeyVaultKeyId are required outside Development (the data-protection key ring).");
        var credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        dp.PersistKeysToAzureBlobStorage(new Uri(blob, $"data-protection-{applicationName}/keys.xml"), credential).ProtectKeysWithAzureKeyVault(key, credential);
        return builder;
    }
}
