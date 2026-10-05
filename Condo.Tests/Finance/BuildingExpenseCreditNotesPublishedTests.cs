using Condo.Api.Controllers;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>Nota de credito del proveedor, fase 2: periodo ya publicado. Se prorratea entre las unidades y se acredita saldo a favor.</summary>
public partial class BuildingExpenseCreditNotesTests
{
    private sealed record Published(BuildingExpense Expense, Unit A, Unit B, ApplicationUser OwnerA, ApplicationUser OwnerB);

    // Periodo publicado con un gasto de 1.000.000 por coeficiente (A 60 %, B 40 %), ya cobrado a las dos unidades.
    private Published PublishedExpense(bool ownersAssigned = true)
    {
        var a = _env.T.AddUnit(_env.Building, "A");
        var b = _env.T.AddUnit(_env.Building, "B");
        a.Coefficient = 0.6m;
        b.Coefficient = 0.4m;
        var ownerA = _env.T.AddUser(_env.Company, UserRole.Owner, "Ana");
        var ownerB = _env.T.AddUser(_env.Company, UserRole.Owner, "Beto");
        if (ownersAssigned)
        {
            _env.T.AddOwner(a, ownerA);
            _env.T.AddOwner(b, ownerB);
        }
        var expense = _env.AddExpense(null, 1_000_000m);
        AddCharge(expense, a, 600_000m);
        AddCharge(expense, b, 400_000m);
        SetPeriodStatus(ExpensePeriodStatus.Published);
        return new Published(expense, a, b, ownerA, ownerB);
    }

    private void AddCharge(BuildingExpense expense, Unit unit, decimal amount)
    {
        _env.T.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = _env.Period.Id, UnitId = unit.Id, ChargeType = ExpenseChargeType.Ordinary,
            SourceBuildingExpenseId = expense.Id, Concept = expense.Description, Amount = amount
        });
        _env.T.Db.SaveChanges();
    }

    private decimal CreditOf(Guid ownerId) =>
        _env.T.NewContext().OwnerCredits.Where(x => x.OwnerId == ownerId).Select(x => x.Amount).ToList().Sum();

    [Fact]
    public async Task Publicado_RepartePorLoCobradoYAcreditaSaldoAFavorSinTocarElGasto()
    {
        var p = PublishedExpense();

        var result = FinanceEnv.Ok(await Create(p.Expense, Req(200_000m)));

        Assert.Equal(BuildingExpenseCreditNoteMode.Credited, result.CreditNote.Mode);
        Assert.Equal(200_000m, result.CreditedToOwners);
        Assert.Equal(1_000_000m, result.Expense.Amount); // lo repartido no cambia
        Assert.Equal(0m, result.Expense.CreditedAmount);
        Assert.False(result.SettlementNeedsRecalculation);
        Assert.Equal(2, result.CreditNote.Allocations.Count);
        Assert.Equal(120_000m, result.CreditNote.Allocations.Single(a => a.UnitCode == "A").Amount);
        Assert.Equal(80_000m, result.CreditNote.Allocations.Single(a => a.UnitCode == "B").Amount);
        Assert.Equal("Ana Prueba", result.CreditNote.Allocations.Single(a => a.UnitCode == "A").OwnerName);

        Assert.Equal(120_000m, CreditOf(p.OwnerA.Id));
        Assert.Equal(80_000m, CreditOf(p.OwnerB.Id));
        var saved = _env.T.NewContext().BuildingExpenses.Single(x => x.Id == p.Expense.Id);
        Assert.Equal(1_000_000m, saved.Amount);
        Assert.Null(saved.OriginalAmount);
    }

    [Fact]
    public async Task Publicado_ElLoteQuedaTrazableYSeConsumeComoCualquierOtro()
    {
        var p = PublishedExpense();
        var result = FinanceEnv.Ok(await Create(p.Expense, Req(200_000m, "NC-77")));

        using var db = _env.T.NewContext();
        var lot = db.OwnerCreditMovements.Single(x => x.OwnerId == p.OwnerA.Id);

        Assert.Equal(OwnerCreditMovementKind.Generated, lot.Kind); // mismo tipo que los demas lotes: lo consume el flujo de hoy
        Assert.Equal(120_000m, lot.Amount);
        Assert.Equal(120_000m, lot.RemainingAmount);
        Assert.Equal(result.CreditNote.Id, lot.SupplierCreditNoteId);
        Assert.Equal(_env.Building.Id, lot.BuildingId);
        Assert.Equal(p.A.Id, lot.UnitId);
        Assert.Contains("NC-77", lot.SourceReference);
        Assert.Contains("Proveedor", lot.SourceReference);

        var allocation = db.BuildingExpenseCreditNoteAllocations.Single(x => x.UnitId == p.A.Id);
        Assert.Equal(lot.Id, allocation.OwnerCreditMovementId);
    }

    [Fact]
    public async Task Publicado_SeSumaAlSaldoQueYaTenia()
    {
        var p = PublishedExpense();
        _env.T.Db.OwnerCredits.Add(new OwnerCredit { CompanyId = _env.Company.Id, OwnerId = p.OwnerA.Id, Amount = 50_000m });
        _env.T.Db.SaveChanges();

        FinanceEnv.Ok(await Create(p.Expense, Req(200_000m)));

        Assert.Equal(170_000m, CreditOf(p.OwnerA.Id));
        Assert.Single(_env.T.NewContext().OwnerCredits.Where(x => x.OwnerId == p.OwnerA.Id).ToList()); // un solo saldo por propietario
    }

    [Fact]
    public async Task Publicado_LaSumaDeLosCreditosEsExactaAunConRedondeo()
    {
        var units = Enumerable.Range(1, 3).Select(i => _env.T.AddUnit(_env.Building, $"T{i}")).ToList();
        foreach (var u in units) _env.T.AddOwner(u, _env.T.AddUser(_env.Company, UserRole.Owner));
        var expense = _env.AddExpense(null, 300_000m);
        foreach (var u in units) AddCharge(expense, u, 100_000m);
        SetPeriodStatus(ExpensePeriodStatus.Published);

        var result = FinanceEnv.Ok(await Create(expense, Req(100_000m))); // 33.333,33 x 3 = 99.999,99: falta 0,01

        Assert.Equal(100_000m, result.CreditNote.Allocations.Sum(a => a.Amount));
        Assert.Equal(100_000m, result.CreditedToOwners);
        Assert.All(result.CreditNote.Allocations, a => Assert.InRange(a.Amount, 33_333.33m, 33_333.34m));
    }

    [Fact]
    public async Task Publicado_DosNotasSeRepartenConLaMismaProporcion()
    {
        var p = PublishedExpense();

        FinanceEnv.Ok(await Create(p.Expense, Req(200_000m, "NC-1")));
        FinanceEnv.Ok(await Create(p.Expense, Req(100_000m, "NC-2")));

        Assert.Equal(180_000m, CreditOf(p.OwnerA.Id)); // 60 % de 300.000
        Assert.Equal(120_000m, CreditOf(p.OwnerB.Id)); // 40 %
    }

    [Fact]
    public async Task Publicado_PuedeAcreditarseElGastoCompletoPeroNoMas()
    {
        var p = PublishedExpense();

        Assert.Contains("todavía se puede acreditar", FinanceEnv.BadRequestText(await Create(p.Expense, Req(1_000_001m, "NC-X"))));
        FinanceEnv.Ok(await Create(p.Expense, Req(700_000m, "NC-1")));
        Assert.Contains("Gs. 300", FinanceEnv.BadRequestText(await Create(p.Expense, Req(400_000m, "NC-2")))); // quedan 300.000
        FinanceEnv.Ok(await Create(p.Expense, Req(300_000m, "NC-3")));                                           // justo el resto
        Assert.Contains("todo su monto", FinanceEnv.BadRequestText(await Create(p.Expense, Req(1m, "NC-4"))));

        Assert.Equal(600_000m, CreditOf(p.OwnerA.Id));
        Assert.Equal(400_000m, CreditOf(p.OwnerB.Id));
    }

    [Fact]
    public async Task Publicado_ConUnaUnidadSinPropietario_SeRechazaTodoYNoSeGuardaNada()
    {
        var p = PublishedExpense(ownersAssigned: false);
        _env.T.AddOwner(p.A, p.OwnerA); // solo A tiene propietario principal

        var text = FinanceEnv.BadRequestText(await Create(p.Expense, Req(200_000m)));

        Assert.Contains("Asigná un propietario principal a las unidades B", text);
        Assert.Empty(_env.T.NewContext().BuildingExpenseCreditNotes);
        Assert.Empty(_env.T.NewContext().OwnerCreditMovements);
        Assert.Equal(0m, CreditOf(p.OwnerA.Id));
        Assert.Empty(_env.T.NewContext().Notifications);
    }

    [Fact]
    public async Task Publicado_ElPropietarioNoPrincipalNoRecibeElCredito()
    {
        var p = PublishedExpense();
        var coOwner = _env.T.AddUser(_env.Company, UserRole.Owner, "Copropietario");
        _env.T.AddOwner(p.A, coOwner, primary: false);

        FinanceEnv.Ok(await Create(p.Expense, Req(200_000m)));

        Assert.Equal(0m, CreditOf(coOwner.Id));
        Assert.Equal(120_000m, CreditOf(p.OwnerA.Id));
    }

    [Fact]
    public async Task Publicado_AvisaACadaPropietarioUnaVezConLaSuma()
    {
        var p = PublishedExpense();
        // Un mismo propietario tiene las dos unidades: un solo aviso con la suma.
        var shared = _env.T.AddUser(_env.Company, UserRole.Owner, "Doble");
        foreach (var link in _env.T.Db.UnitOwners.Where(x => x.UnitId == p.A.Id || x.UnitId == p.B.Id).ToList()) link.OwnerId = shared.Id;
        _env.T.Db.SaveChanges();

        FinanceEnv.Ok(await Create(p.Expense, Req(200_000m)));

        var notices = _env.T.NewContext().Notifications.Where(x => x.Type == NotificationType.SupplierCreditApplied).ToList();
        var notice = Assert.Single(notices);
        Assert.Equal(shared.Id, notice.RecipientId);
        Assert.Equal("ExpensePeriod", notice.EntityType);
        Assert.Equal(_env.Period.Id, notice.EntityId);
        Assert.Contains("200", notice.Body);
        Assert.Contains("A, B", notice.Body);
        Assert.Equal(200_000m, CreditOf(shared.Id));
    }

    [Fact]
    public async Task Publicado_ElGastoQueNoSeReparte_SoloSeRegistraSinSaldoAFavor()
    {
        var expense = _env.AddExpense(null, 500_000m);
        expense.PaidByReserveFund = true; // sin cargos para las unidades
        _env.T.Db.SaveChanges();
        SetPeriodStatus(ExpensePeriodStatus.Published);

        var result = FinanceEnv.Ok(await Create(expense, Req(100_000m)));

        Assert.Equal(BuildingExpenseCreditNoteMode.Credited, result.CreditNote.Mode);
        Assert.Equal(0m, result.CreditedToOwners);
        Assert.Empty(result.CreditNote.Allocations);
        Assert.Empty(_env.T.NewContext().OwnerCreditMovements);
        Assert.Equal(500_000m, result.Expense.Amount);
    }

    [Fact]
    public async Task Publicado_NoSeRegistraDosVecesLaMismaNota()
    {
        var p = PublishedExpense();
        FinanceEnv.Ok(await Create(p.Expense, Req(100_000m, "NC-5")));

        Assert.IsType<ConflictObjectResult>((await Create(p.Expense, Req(100_000m, "nc 5"))).Result);

        Assert.Equal(60_000m, CreditOf(p.OwnerA.Id)); // sin segundo credito
        Assert.Single(_env.T.NewContext().BuildingExpenseCreditNotes.ToList());
    }

    // ── Vista previa ──────────────────────────────────────────────────────────

    [Fact]
    public async Task VistaPrevia_EnPublicado_MuestraElRepartoYNoGuardaNada()
    {
        var p = PublishedExpense();

        var preview = FinanceEnv.Ok(await _api.Preview(p.Expense.Id, new BuildingExpenseCreditNotePreviewRequest { Amount = 200_000m }, default));

        Assert.Equal(BuildingExpenseCreditNoteMode.Credited, preview.Mode);
        Assert.Equal(1_000_000m, preview.ChargedTotal);
        Assert.Equal(1_000_000m, preview.MaxAmount);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal(120_000m, preview.Rows.Single(r => r.UnitCode == "A").CreditAmount);
        Assert.Equal(600_000m, preview.Rows.Single(r => r.UnitCode == "A").ChargeAmount);
        Assert.Equal("Ana Prueba", preview.Rows.Single(r => r.UnitCode == "A").OwnerName);
        Assert.Empty(preview.UnitsWithoutOwner);
        Assert.Null(preview.Message);
        Assert.Empty(_env.T.NewContext().BuildingExpenseCreditNotes);
        Assert.Empty(_env.T.NewContext().OwnerCreditMovements);
    }

    [Fact]
    public async Task VistaPrevia_AvisaLasUnidadesSinPropietario()
    {
        var p = PublishedExpense(ownersAssigned: false);
        _env.T.AddOwner(p.A, p.OwnerA);

        var preview = FinanceEnv.Ok(await _api.Preview(p.Expense.Id, new BuildingExpenseCreditNotePreviewRequest { Amount = 200_000m }, default));

        Assert.Equal(new[] { "B" }, preview.UnitsWithoutOwner.ToArray());
        Assert.Contains("Asigná un propietario principal", preview.Message);
    }

    [Fact]
    public async Task VistaPrevia_EnBorrador_DiceComoQuedaElGasto()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        var preview = FinanceEnv.Ok(await _api.Preview(expense.Id, new BuildingExpenseCreditNotePreviewRequest { Amount = 250_000m }, default));

        Assert.Equal(BuildingExpenseCreditNoteMode.Netted, preview.Mode);
        Assert.Equal(750_000m, preview.NewExpenseAmount);
        Assert.Empty(preview.Rows);
        Assert.Equal(1_000_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    [Fact]
    public async Task VistaPrevia_EnCerradoOConMontoInvalido_SeRechaza()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        Assert.Contains("mayor que cero", FinanceEnv.BadRequestText(await _api.Preview(expense.Id, new BuildingExpenseCreditNotePreviewRequest { Amount = 0m }, default)));
        Assert.Contains("igualar ni superar", FinanceEnv.BadRequestText(await _api.Preview(expense.Id, new BuildingExpenseCreditNotePreviewRequest { Amount = 1_000_000m }, default)));

        SetPeriodStatus(ExpensePeriodStatus.Closed);
        Assert.Contains("cerrado", FinanceEnv.BadRequestText(await _api.Preview(expense.Id, new BuildingExpenseCreditNotePreviewRequest { Amount = 10m }, default)));
    }

    // ── Anular una nota de periodo publicado ──────────────────────────────────

    [Fact]
    public async Task Publicado_AnularDevuelveElSaldoYAvisa()
    {
        var p = PublishedExpense();
        var created = FinanceEnv.Ok(await Create(p.Expense, Req(200_000m)));

        var voided = FinanceEnv.Ok(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "El proveedor la anuló" }, default));

        Assert.Equal(BuildingExpenseCreditNoteStatus.Voided, voided.CreditNote.Status);
        Assert.Equal(0m, CreditOf(p.OwnerA.Id));
        Assert.Equal(0m, CreditOf(p.OwnerB.Id));
        using var db = _env.T.NewContext();
        Assert.All(db.OwnerCreditMovements.ToList(), l => Assert.Equal(0m, l.RemainingAmount));
        Assert.Equal(4, db.Notifications.Count(x => x.Type == NotificationType.SupplierCreditApplied)); // 2 al acreditar + 2 al anular
        Assert.Equal(1_000_000m, db.BuildingExpenses.Single(x => x.Id == p.Expense.Id).Amount);
    }

    [Fact]
    public async Task Publicado_NoSePuedeAnularSiElSaldoYaSeUso()
    {
        var p = PublishedExpense();
        var created = FinanceEnv.Ok(await Create(p.Expense, Req(200_000m)));

        // El propietario de A uso parte del saldo en un pago.
        var lot = _env.T.Db.OwnerCreditMovements.Single(x => x.OwnerId == p.OwnerA.Id);
        lot.RemainingAmount = 20_000m;
        _env.T.Db.OwnerCredits.Single(x => x.OwnerId == p.OwnerA.Id).Amount = 20_000m;
        _env.T.Db.SaveChanges();

        var text = FinanceEnv.BadRequestText(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "x" }, default));

        Assert.Contains("ya se usó", text);
        Assert.Contains("A", text);
        Assert.Equal(BuildingExpenseCreditNoteStatus.Applied, _env.T.NewContext().BuildingExpenseCreditNotes.Single().Status);
        Assert.Equal(20_000m, CreditOf(p.OwnerA.Id));
        Assert.Equal(80_000m, CreditOf(p.OwnerB.Id)); // nada a medias: B tampoco se toco
    }

    [Fact]
    public async Task Publicado_DespuesDeAnular_SePuedeVolverARegistrarLaMismaNota()
    {
        var p = PublishedExpense();
        var created = FinanceEnv.Ok(await Create(p.Expense, Req(200_000m, "NC-1")));
        FinanceEnv.Ok(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Monto mal" }, default));

        FinanceEnv.Ok(await Create(p.Expense, Req(150_000m, "NC-1")));

        Assert.Equal(90_000m, CreditOf(p.OwnerA.Id));
        Assert.Equal(60_000m, CreditOf(p.OwnerB.Id));
    }

    [Fact]
    public async Task UnaNotaDeBorradorYaPublicada_NoSeAnula()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, Req(200_000m)));
        SetPeriodStatus(ExpensePeriodStatus.Published);

        var text = FinanceEnv.BadRequestText(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "x" }, default));

        Assert.Contains("ya se tuvo en cuenta", text);
        Assert.Equal(800_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    [Fact]
    public async Task Lista_IncluyeElRepartoDeLasNotasDePeriodoPublicado()
    {
        var p = PublishedExpense();
        FinanceEnv.Ok(await Create(p.Expense, Req(200_000m)));

        var list = FinanceEnv.Ok(await _api.GetByExpense(p.Expense.Id, default));

        var note = Assert.Single(list);
        Assert.Equal(BuildingExpenseCreditNoteMode.Credited, note.Mode);
        Assert.Equal(new[] { "A", "B" }, note.Allocations.Select(a => a.UnitCode).ToArray());
    }

    [Fact]
    public async Task Publicado_QuienNoTieneAccesoNoPuedeVerLaVistaPreviaNiAcreditar()
    {
        var p = PublishedExpense();
        var outsider = As("BuildingManager");

        Assert.IsType<ForbidResult>((await outsider.Preview(p.Expense.Id, new BuildingExpenseCreditNotePreviewRequest { Amount = 10m }, default)).Result);
        Assert.IsType<ForbidResult>((await outsider.Create(p.Expense.Id, Req(10_000m, "NC-9"), default)).Result);
        Assert.Empty(_env.T.NewContext().OwnerCreditMovements);
    }
}
