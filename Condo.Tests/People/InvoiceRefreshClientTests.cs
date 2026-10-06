using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Condo.Tests.People;

// "Actualizar cliente": solo para facturas emitidas cuyo cliente se completo al migrar; las demas conservan el cliente de la emision.
public class InvoiceRefreshClientTests
{
    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Scenario : IDisposable
    {
        public TestDb T { get; } = new();
        public InvoicesController Controller { get; }
        public Invoice Invoice { get; }
        public ApplicationUser Owner { get; }

        public Scenario(InvoiceStatus status = InvoiceStatus.Issued, bool reconstructed = true)
        {
            var company = T.AddCompany();
            var building = T.AddBuilding(company);
            var unit = T.AddUnit(building);
            Owner = T.AddUser(company, name: "Alfredo");
            Owner.DocumentType = "CedulaParaguaya";
            Owner.DocumentNumber = "123456";
            Owner.InvoiceName = "Alfredito el ninja";
            Owner.InvoiceDocumentType = "RUC";
            Owner.InvoiceDocument = "80012345-0";
            T.Db.SaveChanges();
            T.AddOwner(unit, Owner);

            var period = new ExpensePeriod
            {
                CompanyId = company.Id, BuildingId = building.Id, Year = 2026, Month = 9, Name = "Septiembre 2026",
                StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), DueDate = new DateOnly(2026, 10, 10)
            };
            T.Db.ExpensePeriods.Add(period);
            T.Db.SaveChanges();
            var payment = new Payment { CompanyId = company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, Amount = 100m, Reference = "P1" };
            T.Db.Payments.Add(payment);
            T.Db.SaveChanges();

            Invoice = new Invoice
            {
                CompanyId = company.Id, BuildingId = building.Id, UnitId = unit.Id, PaymentId = payment.Id,
                Status = status, Numero = 1, NumeroFormateado = "001-001-0000001", MontoTotal = 100m,
                ClientName = "Alfredo Prueba", ClientDocumentType = "CedulaParaguaya", ClientDocument = "123456",
                ClientReconstructed = reconstructed
            };
            T.Db.Invoices.Add(Invoice);
            T.Db.SaveChanges();

            var tenant = new FakeTenantContext { CompanyId = company.Id, Role = "CompanyAdmin" };
            var scope = new FakeAccessScope(tenant);
            scope.Buildings.Add(building.Id);
            Controller = new InvoicesController(
                T.Db, scope, tenant, new FakeEnv(),
                new InvoiceDraftService(T.Db, scope),
                new PushDispatcher(T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));
        }

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public async Task Una_factura_reconstruida_toma_los_datos_de_facturacion_actuales_y_queda_auditada()
    {
        using var s = new Scenario();

        var result = await s.Controller.RefreshClient(s.Invoice.Id, CancellationToken.None);

        var dto = Assert.IsType<OkObjectResult>(result.Result).Value as InvoiceClientDto;
        Assert.Equal("Alfredito el ninja", dto!.ClienteNombre);
        Assert.Equal("80012345-0", dto.ClienteDocumento);
        Assert.Equal("RUC", dto.ClienteTipoDocumento);
        Assert.False(dto.ClienteReconstruido);

        using var check = s.T.NewContext();
        var saved = await check.Invoices.AsNoTracking().FirstAsync(x => x.Id == s.Invoice.Id);
        Assert.Equal("Alfredito el ninja", saved.ClientName);
        Assert.False(saved.ClientReconstructed);
        Assert.True(await check.InvoiceAuditLogs.AnyAsync(x => x.InvoiceId == s.Invoice.Id && x.Action == InvoiceAuditAction.ClientRefreshed));
    }

    [Fact]
    public async Task No_se_puede_repetir_ni_cambiar_una_factura_emitida_con_el_cliente_real()
    {
        using var s = new Scenario();
        await s.Controller.RefreshClient(s.Invoice.Id, CancellationToken.None);

        var again = await s.Controller.RefreshClient(s.Invoice.Id, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(again.Result);

        using var real = new Scenario(reconstructed: false);
        var notAllowed = await real.Controller.RefreshClient(real.Invoice.Id, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(notAllowed.Result);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Voided)]
    public async Task Solo_se_actualiza_una_factura_emitida(InvoiceStatus status)
    {
        using var s = new Scenario(status);

        var result = await s.Controller.RefreshClient(s.Invoice.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }
}
