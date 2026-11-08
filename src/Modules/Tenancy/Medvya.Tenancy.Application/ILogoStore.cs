namespace Medvya.Tenancy.Application;

/// <summary>Private object storage for clinic logos. Nothing in it is public: a logo is reachable only through a short-lived signed link.</summary>
public interface ILogoStore
{
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>A read-only link to the object that expires after <paramref name="lifetime"/>.</summary>
    Task<Uri> LinkForAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a logo upload may be. The type comes from the bytes (never the file name or the client's header); SVG is refused because
/// it can carry script.
/// </summary>
public static class LogoImage
{
    public const int MaxBytes = 512 * 1024;

    /// <summary>"image/png" or "image/jpeg" from the file signature; null for anything else.</summary>
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], Jpeg = [0xFF, 0xD8, 0xFF];

    public static string? ContentTypeOf(ReadOnlySpan<byte> content) =>
        content.StartsWith(Png) ? "image/png" : content.StartsWith(Jpeg) ? "image/jpeg" : null;
}
