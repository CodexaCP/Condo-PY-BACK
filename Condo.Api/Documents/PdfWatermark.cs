using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Condo.Api.Documents;

public static class PdfWatermark
{
    /// <summary>
    /// Franja legal para comprobantes internos generados desde la app:
    /// deja claro que el documento no tiene validez fiscal. Se muestra
    /// sobre el pie de página en todas las hojas.
    /// </summary>
    public static void ComposeNonFiscal(IContainer container)
    {
        container
            .Background("#FEF3C7")
            .Border(1)
            .BorderColor("#F59E0B")
            .Padding(8)
            .AlignCenter()
            .Text("ESTE DOCUMENTO NO ES UN COMPROBANTE FISCAL — Sin validez tributaria. Constancia interna de gestión.")
            .FontSize(10).Bold().FontColor("#92400E");
    }
}
