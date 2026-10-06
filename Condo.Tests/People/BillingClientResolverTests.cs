using Condo.Api.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Xunit;

namespace Condo.Tests.People;

// Cliente de la factura: a quien se le factura una unidad y como queda guardado en la factura emitida.
public class BillingClientResolverTests
{
    [Fact]
    public async Task Factura_a_nombre_del_propietario_principal_con_sus_datos_personales()
    {
        using var t = new TestDb();
        var company = t.AddCompany();
        var unit = t.AddUnit(t.AddBuilding(company));
        var owner = t.AddUser(company, name: "Ana");
        owner.DocumentType = "CedulaParaguaya";
        owner.DocumentNumber = "1234567";
        t.Db.SaveChanges();
        t.AddOwner(unit, owner);

        var client = await BillingClientResolver.LoadLiveAsync(t.Db, unit.Id, CancellationToken.None);

        Assert.NotNull(client);
        Assert.Equal("Ana Prueba", client!.Name);
        Assert.Equal("1234567", client.Document);
    }

    [Fact]
    public async Task Los_datos_de_facturacion_propios_reemplazan_a_los_personales()
    {
        using var t = new TestDb();
        var company = t.AddCompany();
        var unit = t.AddUnit(t.AddBuilding(company));
        var owner = t.AddUser(company, name: "Ana");
        owner.DocumentNumber = "1234567";
        owner.InvoiceName = "Inversiones Ana S.A.";
        owner.InvoiceDocumentType = "RUC";
        owner.InvoiceDocument = "80012345-1";
        owner.InvoiceEmail = "facturas@ana.com";
        t.Db.SaveChanges();
        t.AddOwner(unit, owner);

        var client = await BillingClientResolver.LoadLiveAsync(t.Db, unit.Id, CancellationToken.None);

        Assert.Equal("Inversiones Ana S.A.", client!.Name);
        Assert.Equal("80012345-1", client.Document);
        Assert.Equal("RUC", client.DocumentType);
        Assert.Equal("facturas@ana.com", client.Email);
    }

    [Fact]
    public async Task Persona_juridica_se_factura_por_su_razon_social()
    {
        using var t = new TestDb();
        var company = t.AddCompany();
        var unit = t.AddUnit(t.AddBuilding(company));
        var owner = t.AddUser(company, name: "Luis");
        owner.PersonType = PersonType.Legal;
        owner.LegalName = "Luis y Hnos. S.R.L.";
        t.Db.SaveChanges();
        t.AddOwner(unit, owner);

        var client = await BillingClientResolver.LoadLiveAsync(t.Db, unit.Id, CancellationToken.None);

        Assert.Equal("Luis y Hnos. S.R.L.", client!.Name);
    }

    [Fact]
    public async Task Entre_varios_propietarios_se_elige_el_principal_y_sin_propietario_el_residente_actual()
    {
        using var t = new TestDb();
        var company = t.AddCompany();
        var building = t.AddBuilding(company);
        var withOwners = t.AddUnit(building);
        var onlyResident = t.AddUnit(building);
        var empty = t.AddUnit(building);

        t.AddOwner(withOwners, t.AddUser(company, name: "Secundario"), primary: false);
        t.AddOwner(withOwners, t.AddUser(company, name: "Principal"), primary: true);
        t.AddResident(onlyResident, t.AddUser(company, UserRole.Resident, "Inquilino"));

        var clients = await BillingClientResolver.LoadLiveAsync(t.Db, new[] { withOwners.Id, onlyResident.Id, empty.Id }, CancellationToken.None);

        Assert.Equal("Principal Prueba", clients[withOwners.Id].Name);
        Assert.Equal("Inquilino Prueba", clients[onlyResident.Id].Name);
        Assert.False(clients.ContainsKey(empty.Id));
    }

    [Fact]
    public void La_factura_conserva_el_cliente_guardado_aunque_cambie_el_propietario()
    {
        var invoice = new Invoice();
        Assert.Null(BillingClientResolver.FromSnapshot(invoice.ClientName, null, null, null, null));

        BillingClientResolver.ApplySnapshot(invoice, new BillingClient("Ana Prueba", "CedulaParaguaya", "1234567", "Calle 1", "ana@test.local"));

        var saved = BillingClientResolver.FromSnapshot(invoice.ClientName, invoice.ClientDocumentType, invoice.ClientDocument, invoice.ClientAddress, invoice.ClientEmail);
        Assert.Equal("Ana Prueba", saved!.Name);
        Assert.Equal("1234567", saved.Document);
        Assert.False(invoice.ClientReconstructed);
    }

    [Theory]
    [InlineData("RUC", "80012345-0", false)]
    [InlineData("RUC", "80012345-1", true)]
    [InlineData("RUC", "80012345", true)]
    [InlineData("CedulaParaguaya", "1.234.567", false)]
    [InlineData("CedulaParaguaya", "12A4567", true)]
    [InlineData("Pasaporte", "AB-123456", false)]
    [InlineData(null, "AB 123", true)]
    public void El_documento_se_valida_segun_su_tipo(string? type, string number, bool invalid)
    {
        // 80012345: pesos 2..9 de derecha a izquierda suman 122; 122 % 11 = 1, asi que el digito verificador es 0.
        var error = PersonRules.ValidateDocument(type, number);
        Assert.Equal(invalid, error is not null);
    }
}
