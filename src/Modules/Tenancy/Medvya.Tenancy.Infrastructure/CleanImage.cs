using SkiaSharp;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Uploaded images (logo, doctor signature) are decoded and saved again as a plain PNG before they are stored (MDV-P1-005, #211):
/// only pixels survive, so metadata, trailing bytes and polyglot payloads never reach Blob storage or a printed slip. The size is read
/// from the header first and anything over <see cref="MaxSide"/> is refused before decoding (a small file can declare a huge image).
/// </summary>
public static class CleanImage
{
    public const int MaxSide = 4096;

    /// <summary>The PNG bytes, or null when the content is not a decodable image or is too large.</summary>
    public static byte[]? ToPng(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width is < 1 or > MaxSide || codec.Info.Height is < 1 or > MaxSide) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null) return null;
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }
}
