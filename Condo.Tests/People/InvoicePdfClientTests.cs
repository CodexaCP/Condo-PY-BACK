using Condo.Api.Documents;
using Condo.Application.Models;
using Condo.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Condo.Tests.People;

// La factura y la nota de credito se generan con cada tipo de documento del cliente y con la actividad economica del emisor.
public class InvoicePdfClientTests
{
    static InvoicePdfClientTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static InvoiceDto Sample(string? documentType, string document, bool hideFrame = false, string? fieldPositionsJson = null) => new()
    {
        BuildingName = "EDIFICIO DE EJEMPLO",
        UnitCode = "01-01",
        SeriesRazonSocial = "Consorcio de Ejemplo",
        SeriesRuc = "80012345-0",
        SeriesNumeroTimbrado = "12345678",
        SeriesEstablecimiento = "001",
        SeriesPuntoExpedicion = "001",
        SeriesVigenciaDesde = new DateOnly(2026, 1, 1),
        SeriesVigenciaHasta = new DateOnly(2027, 1, 1),
        SeriesDireccionEstablecimiento = "Av. España 123, Asunción",
        SeriesActividadEconomica = "Administración de condominios",
        BuildingAddress = "Otra dirección",
        ClienteNombre = "CLIENTE DE EJEMPLO",
        ClienteDocumento = document,
        ClienteTipoDocumento = documentType,
        HideFrame = hideFrame,
        FieldPositionsJson = fieldPositionsJson,
        Status = InvoiceStatus.Issued,
        Numero = 1,
        NumeroFormateado = "001-001-0000001",
        MontoTotal = 576_802m,
        PeriodYear = 2026,
        PeriodMonth = 9,
        Detalle = [new InvoiceLineDto { Concepto = "Expensa ordinaria", ChargeType = ExpenseChargeType.Ordinary, Monto = 576_802m }],
        FechaEmisionUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow
    };

    [Theory]
    [InlineData("CedulaParaguaya", "1234567")]
    [InlineData("RUC", "80012345-0")]
    [InlineData("Pasaporte", "AB-123456")]
    [InlineData("DocumentoExtranjero", "X123456")]
    [InlineData(null, "80012345-0")]
    public void La_factura_se_genera_con_cada_tipo_de_documento(string? type, string document)
    {
        var bytes = new InvoicePdfDocument(Sample(type, document), standardTemplate: true).GeneratePdf();
        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public void La_actividad_economica_se_puede_mostrar_u_ocultar_desde_la_calibracion()
    {
        // Papel con marco propio: arranca sin dibujar; con la decision guardada se dibuja.
        var hidden = new InvoicePdfDocument(Sample("RUC", "80012345-0", hideFrame: true)).GeneratePdf();
        var shown = new InvoicePdfDocument(Sample("RUC", "80012345-0", hideFrame: true,
            fieldPositionsJson: "{\"headerActividad\":{\"dx\":0,\"dy\":0,\"hidden\":false}}")).GeneratePdf();

        Assert.True(hidden.Length > 1000);
        Assert.True(shown.Length > 1000);
        Assert.Contains("headerActividad", InvoicePdfDocument.CalibratableKeys);
    }

    [Theory]
    [InlineData("RUC", "80012345-0")]
    [InlineData("Pasaporte", "AB-123456")]
    [InlineData(null, "1234567")]
    public void La_nota_de_credito_se_genera_con_cada_tipo_de_documento(string? type, string document)
    {
        var data = new CreditNotePdfData(
            "EDIFICIO DE EJEMPLO", "Av. España 123", "021 000 000", "Consorcio de Ejemplo", "80012345-0", "12345678",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "001-001-0000001", DateTime.UtcNow, DateTime.UtcNow,
            "001-001-0000009", DateTime.UtcNow, "CLIENTE DE EJEMPLO", document, type, "01-01",
            "Ajuste de ejemplo", 100_000m, CreditNoteStatus.Approved, null, null, null, null,
            [new CreditNotePdfLine("Expensa", 100_000m)]);

        var bytes = new CreditNotePdfDocument(data).GeneratePdf();
        Assert.True(bytes.Length > 1000);
    }
}
