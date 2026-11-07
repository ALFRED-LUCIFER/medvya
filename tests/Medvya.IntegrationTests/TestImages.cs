using SkiaSharp;

namespace Medvya.IntegrationTests;

/// <summary>Real, tiny images for upload tests (uploads are decoded and re-encoded, so header-only bytes no longer pass).</summary>
static class TestImages
{
    public static readonly byte[] Png = Make(4, 4, SKEncodedImageFormat.Png);
    public static readonly byte[] Jpeg = Make(4, 4, SKEncodedImageFormat.Jpeg);

    public static byte[] Make(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
