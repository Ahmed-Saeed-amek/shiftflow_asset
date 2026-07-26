using QRCoder;
using SkiaSharp;
using ZXing;
using ZXing.SkiaSharp.Rendering;

namespace ShiftFlow.Web.Services;

/// <summary>
/// Generates QR codes (linking straight to an asset's Details page — scan with any phone camera)
/// and Code128 barcodes (encoding the asset tag — readable by handheld warehouse/maintenance
/// scanners) as PNG bytes, for printing on physical asset labels.
/// </summary>
public static class AssetCodeGenerator
{
    public static byte[] GenerateQrPng(string content, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(pixelsPerModule);
    }

    public static byte[] GenerateBarcodePng(string content, int width = 300, int height = 100)
    {
        var writer = new BarcodeWriter<SKBitmap>
        {
            Format = BarcodeFormat.CODE_128,
            Options = new ZXing.Common.EncodingOptions { Width = width, Height = height, Margin = 5, PureBarcode = false },
            Renderer = new SKBitmapRenderer(),
        };
        using var bitmap = writer.Write(content);
        using var image = SKImage.FromBitmap(bitmap);
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        return pngData.ToArray();
    }
}
