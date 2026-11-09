using QRCoder;

namespace Condo.Api.Services;

/// <summary>Dibuja el codigo QR de una direccion. Vectorial (SVG) para que salga nitido al imprimir a cualquier tamano.</summary>
public static class QrCodeImage
{
    // Margen blanco estandar de un QR: 4 modulos por lado.
    public const int StandardQuietModules = 4;

    // Correccion de errores M (15 %): aguanta una mancha o un doblez del papel sin hacer el codigo demasiado denso.
    // quietModules = margen blanco alrededor, en modulos (un modulo es el cuadradito del QR). El estandar pide 4, pero en un papel con poco
    // lugar ese borde le saca casi la cuarta parte al codigo: con 1 o 2 sobre fondo claro los celulares lo leen igual y el QR sale mas grande
    // en el mismo cuadro. El margen siempre es blanco y opaco, asi tapa las lineas del papel que pasen cerca.
    public static string Svg(string content, int quietModules = StandardQuietModules)
    {
        quietModules = Math.Clamp(quietModules, 0, StandardQuietModules);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);

        // ModuleMatrix de QRCoder ya incluye 4 modulos de margen por lado: se recorta al nucleo y se vuelve a dibujar con el margen pedido.
        var matrix = data.ModuleMatrix;
        var core = matrix.Count - 2 * StandardQuietModules;
        var total = core + 2 * quietModules;
        var path = new System.Text.StringBuilder();
        for (var y = 0; y < core; y++)
        for (var x = 0; x < core; x++)
            if (matrix[y + StandardQuietModules][x + StandardQuietModules])
                path.Append($"M{x + quietModules},{y + quietModules}h1v1h-1z");

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total} {total}\" shape-rendering=\"crispEdges\">"
               + $"<rect width=\"{total}\" height=\"{total}\" fill=\"#FFFFFF\"/><path fill=\"#000000\" d=\"{path}\"/></svg>";
    }

    // Direccion sin "https://" para imprimirla bajo el QR (se puede teclear si no se puede escanear).
    public static string ShortText(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url[8..] : url;
}
