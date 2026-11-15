using QRCoder;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>QR codes as module rows ('1' dark), so a PDF or a web page draws them as one vector path.</summary>
public static class QrRows
{
    /// <summary>The QR code's modules, row by row, with its quiet zone; error correction M survives a crease, a stamp or thermal blur.</summary>
    public static string[] Of(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return [.. data.ModuleMatrix.Select(row => string.Concat(row.Cast<bool>().Select(b => b ? '1' : '0')))];
    }
}
