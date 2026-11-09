using QRCoder;

namespace Condo.Api.Services;

/// <summary>Dibuja el codigo QR de una direccion. Vectorial (SVG) para que salga nitido al imprimir a cualquier tamano.</summary>
public static class QrCodeImage
{
    // Correccion de errores M (15 %): aguanta una mancha o un doblez del papel sin hacer el codigo demasiado denso.
    public static string Svg(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(10, "#000000", "#FFFFFF", true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    // Direccion sin "https://" para imprimirla bajo el QR (se puede teclear si no se puede escanear).
    public static string ShortText(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url[8..] : url;
}
