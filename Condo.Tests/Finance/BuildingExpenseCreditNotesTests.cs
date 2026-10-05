using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Services;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Condo.Tests.Finance;

/// <summary>Nota de credito del proveedor sobre un gasto, fase 1: periodo en borrador (baja el gasto), cerrado y publicado (se rechaza).</summary>
public class BuildingExpenseCreditNotesTests : IDisposable
{
    private readonly FinanceEnv _env = new();
    private readonly BuildingExpenseCreditNotesController _api;

    public BuildingExpenseCreditNotesTests()
    {
        _api = new BuildingExpenseCreditNotesController(_env.T.Db, _env.Access, _env.Tenant);
    }

    public void Dispose() => _env.Dispose();

    private static CreateBuildingExpenseCreditNoteRequest Req(decimal amount, string numero = "001-001-0000123") => new()
    {
        Numero = numero,
        IssueDate = new DateOnly(2026, 10, 2),
        Amount = amount,
        Reason = "Descuento por mercadería devuelta"
    };

    private Task<ActionResult<BuildingExpenseCreditNoteResultDto>> Create(BuildingExpense expense, CreateBuildingExpenseCreditNoteRequest request) =>
        _api.Create(expense.Id, request, default);

    private void SetPeriodStatus(ExpensePeriodStatus status)
    {
        var period = _env.T.Db.ExpensePeriods.Single(x => x.Id == _env.Period.Id);
        period.Status = status;
        _env.T.Db.SaveChanges();
    }

    private BuildingExpenseCreditNotesController As(string role, params Guid[] buildings)
    {
        var tenant = new FakeTenantContext { CompanyId = _env.Company.Id, Role = role };
        var access = new FakeAccessScope(tenant);
        foreach (var b in buildings) access.Buildings.Add(b);
        return new BuildingExpenseCreditNotesController(_env.T.Db, access, tenant);
    }

    private BuildingExpensesController Expenses() => new(
        _env.T.Db, _env.Access, _env.Tenant, new StubWebHostEnvironment(Path.GetTempPath()),
        new ConfigurationBuilder().Build(), new MovementRubroResolver(_env.T.Db, new FinanceModuleGate(_env.T.Db)));

    // ── Periodo en borrador ───────────────────────────────────────────────────

    [Fact]
    public async Task EnBorrador_BajaElGastoYGuardaLoFacturadoPorElProveedor()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        var result = FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        Assert.Equal(800_000m, result.Expense.Amount);
        Assert.Equal(1_000_000m, result.Expense.OriginalAmount);
        Assert.Equal(200_000m, result.Expense.CreditedAmount);
        Assert.Equal(BuildingExpenseCreditNoteMode.Netted, result.CreditNote.Mode);
        Assert.Equal(BuildingExpenseCreditNoteStatus.Applied, result.CreditNote.Status);

        var saved = _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id);
        Assert.Equal(800_000m, saved.Amount);
        Assert.Equal(1_000_000m, saved.OriginalAmount);
    }

    [Fact]
    public async Task VariasNotas_SeAcumulanSobreElMismoGasto()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        FinanceEnv.Ok(await Create(expense, Req(200_000m, "NC-1")));
        var second = FinanceEnv.Ok(await Create(expense, Req(100_000m, "NC-2")));

        Assert.Equal(700_000m, second.Expense.Amount);
        Assert.Equal(1_000_000m, second.Expense.OriginalAmount);
        Assert.Equal(300_000m, second.Expense.CreditedAmount);
        Assert.Equal(2, _env.T.NewContext().BuildingExpenseCreditNotes.Count(x => x.BuildingExpenseId == expense.Id));
    }

    [Fact]
    public async Task LaLiquidacionSeCalculaConElNeto()
    {
        var a = _env.T.AddUnit(_env.Building, "A");
        var b = _env.T.AddUnit(_env.Building, "B");
        a.Coefficient = 0.6m;
        b.Coefficient = 0.4m;
        _env.T.Db.SaveChanges();
        var expense = _env.AddExpense(null, 1_000_000m);
        FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        var period = _env.T.Db.ExpensePeriods.AsNoTracking().Single(x => x.Id == _env.Period.Id);
        var settlement = new ExpenseSettlement { CompanyId = _env.Company.Id, ExpensePeriodId = period.Id, BuildingId = _env.Building.Id };
        var preview = await new ExpenseSettlementDistributionService(_env.T.Db).PreviewAsync(period, settlement, default);

        var items = preview.Items.Where(i => i.SourceBuildingExpenseId == expense.Id).ToList();
        Assert.Equal(800_000m, items.Sum(i => i.Amount));
        Assert.Equal(480_000m, items.Single(i => i.UnitCode == "A").Amount);
        Assert.Equal(320_000m, items.Single(i => i.UnitCode == "B").Amount);
    }

    [Fact]
    public async Task SiLaLiquidacionYaEstabaCalculada_AvisaQueHayQueRecalcularla()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        _env.T.Db.ExpenseSettlements.Add(new ExpenseSettlement
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = _env.Period.Id, BuildingId = _env.Building.Id,
            GeneratedByUserId = _env.Admin.Id, Status = ExpenseSettlementStatus.Calculated
        });
        _env.T.Db.SaveChanges();

        var result = FinanceEnv.Ok(await Create(expense, Req(100_000m)));

        Assert.True(result.SettlementNeedsRecalculation);
    }

    [Fact]
    public async Task SinLiquidacionCalculada_NoPideRecalcular()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        var result = FinanceEnv.Ok(await Create(expense, Req(100_000m)));

        Assert.False(result.SettlementNeedsRecalculation);
    }

    // ── Validaciones ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NoPuedeIgualarNiSuperarElGasto()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        Assert.Contains("igualar ni superar", FinanceEnv.BadRequestText(await Create(expense, Req(1_000_000m))));
        Assert.Contains("igualar ni superar", FinanceEnv.BadRequestText(await Create(expense, Req(1_500_000m))));

        FinanceEnv.Ok(await Create(expense, Req(600_000m, "NC-1")));
        // Lo pendiente ahora son 400.000: otra nota por esa cifra tambien lo dejaria en cero.
        Assert.Contains("igualar ni superar", FinanceEnv.BadRequestText(await Create(expense, Req(400_000m, "NC-2"))));
        Assert.Equal(400_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    [Fact]
    public async Task ElNumeroDeLaNotaDelMismoProveedorNoSeRepite()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        FinanceEnv.Ok(await Create(expense, Req(100_000m, "NC-7")));

        var again = await Create(expense, Req(50_000m, "NC-7"));

        Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Equal(900_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    [Theory]
    [InlineData("", "Motivo", 100, "número")]
    [InlineData("NC-1", "", 100, "motivo")]
    [InlineData("NC-1", "Motivo", 0, "mayor que cero")]
    [InlineData("NC-1", "Motivo", -5, "mayor que cero")]
    public async Task ExigeNumeroMotivoYMontoPositivo(string numero, string motivo, int monto, string texto)
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var request = Req(monto, numero);
        request.Reason = motivo;

        Assert.Contains(texto, FinanceEnv.BadRequestText(await Create(expense, request)));
        Assert.Empty(_env.T.NewContext().BuildingExpenseCreditNotes);
    }

    [Theory]
    [InlineData("https://otro.sitio/archivo.pdf")]
    [InlineData("/uploads/../secreto.pdf")]
    [InlineData("//uploads/a.pdf")]
    public async Task ElArchivoSoloPuedeSerUnoSubidoALaPlataforma(string url)
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var request = Req(100_000m);
        request.DocumentUrl = url;

        Assert.Contains("archivo", FinanceEnv.BadRequestText(await Create(expense, request)));
    }

    [Fact]
    public async Task AceptaUnArchivoSubidoALaPlataforma()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var request = Req(100_000m);
        request.DocumentUrl = "/uploads/0b1f.pdf";

        var result = FinanceEnv.Ok(await Create(expense, request));

        Assert.Equal("/uploads/0b1f.pdf", result.CreditNote.DocumentUrl);
    }

    [Fact]
    public async Task FechaFuturaOInvalidaSeRechaza()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var request = Req(100_000m);
        request.IssueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        Assert.Contains("fecha", FinanceEnv.BadRequestText(await Create(expense, request)));
    }

    // ── Periodo cerrado o publicado ───────────────────────────────────────────

    [Fact]
    public async Task ConElPeriodoCerrado_PideAnularLaLiquidacion()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        SetPeriodStatus(ExpensePeriodStatus.Closed);

        var text = FinanceEnv.BadRequestText(await Create(expense, Req(100_000m)));

        Assert.Contains("cerrado", text);
        Assert.Contains("Anulá la liquidación", text);
        Assert.Equal(1_000_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    [Fact]
    public async Task ConElPeriodoPublicado_AunNoSePermite()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        SetPeriodStatus(ExpensePeriodStatus.Published);

        var text = FinanceEnv.BadRequestText(await Create(expense, Req(100_000m)));

        Assert.Contains("publicado", text);
        Assert.Equal(1_000_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
        Assert.Empty(_env.T.NewContext().BuildingExpenseCreditNotes);
    }

    // ── Anular ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnularDevuelveElMontoYQuitaLoFacturado()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        var voided = FinanceEnv.Ok(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Se cargó dos veces" }, default));

        Assert.Equal(BuildingExpenseCreditNoteStatus.Voided, voided.CreditNote.Status);
        Assert.Equal(1_000_000m, voided.Expense.Amount);
        Assert.Equal(1_000_000m, voided.Expense.OriginalAmount);
        Assert.Equal(0m, voided.Expense.CreditedAmount);
        var saved = _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id);
        Assert.Null(saved.OriginalAmount);
    }

    [Fact]
    public async Task AnularUnaDeDosNotas_DejaLaOtraAplicada()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var first = FinanceEnv.Ok(await Create(expense, Req(200_000m, "NC-1")));
        FinanceEnv.Ok(await Create(expense, Req(100_000m, "NC-2")));

        var voided = FinanceEnv.Ok(await _api.Void(first.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error de carga" }, default));

        Assert.Equal(900_000m, voided.Expense.Amount);
        Assert.Equal(1_000_000m, voided.Expense.OriginalAmount);
        Assert.Equal(100_000m, voided.Expense.CreditedAmount);
    }

    [Fact]
    public async Task AnularExigeMotivoYNoSePuedeDosVeces()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        Assert.Contains("motivo", FinanceEnv.BadRequestText(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = " " }, default)));

        FinanceEnv.Ok(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error" }, default));
        Assert.Contains("ya está anulada", FinanceEnv.BadRequestText(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Otra vez" }, default)));
    }

    [Fact]
    public async Task NoSePuedeAnularConElPeriodoCerradoOPublicado()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        SetPeriodStatus(ExpensePeriodStatus.Closed);
        Assert.Contains("cerrado", FinanceEnv.BadRequestText(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "x" }, default)));

        SetPeriodStatus(ExpensePeriodStatus.Published);
        Assert.Contains("publicado", FinanceEnv.BadRequestText(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "x" }, default)));

        Assert.Equal(800_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    // ── Lista ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListaLasNotasDelGastoIncluidasLasAnuladas()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var first = FinanceEnv.Ok(await Create(expense, Req(200_000m, "NC-1")));
        FinanceEnv.Ok(await Create(expense, Req(100_000m, "NC-2")));
        FinanceEnv.Ok(await _api.Void(first.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error" }, default));

        var list = FinanceEnv.Ok(await _api.GetByExpense(expense.Id, default));

        Assert.Equal(2, list.Count);
        Assert.Contains(list, n => n.Numero == "NC-1" && n.Status == BuildingExpenseCreditNoteStatus.Voided);
        Assert.Contains(list, n => n.Numero == "NC-2" && n.Status == BuildingExpenseCreditNoteStatus.Applied);
    }

    // ── Permisos y aislamiento ────────────────────────────────────────────────

    [Fact]
    public async Task QuienNoTieneAccesoAlEdificio_NoPuedeCrearNiVerNiAnular()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, Req(200_000m)));
        var outsider = As("BuildingManager"); // sin ningun edificio asignado

        Assert.IsType<ForbidResult>((await outsider.Create(expense.Id, Req(10_000m, "NC-9"), default)).Result);
        Assert.IsType<ForbidResult>((await outsider.GetByExpense(expense.Id, default)).Result);
        Assert.IsType<ForbidResult>((await outsider.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "x" }, default)).Result);
        Assert.Equal(800_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    [Fact]
    public async Task ElPersonalDelEdificioSiPuede()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var manager = As("BuildingManager", _env.Building.Id);

        var result = FinanceEnv.Ok(await manager.Create(expense.Id, Req(100_000m), default));

        Assert.Equal(900_000m, result.Expense.Amount);
    }

    [Fact]
    public async Task GastoInexistente_DaNotFound()
    {
        Assert.IsType<NotFoundResult>((await _api.Create(Guid.NewGuid(), Req(1m), default)).Result);
        Assert.IsType<NotFoundResult>((await _api.GetByExpense(Guid.NewGuid(), default)).Result);
        Assert.IsType<NotFoundResult>((await _api.Void(Guid.NewGuid(), new VoidBuildingExpenseCreditNoteRequest { Reason = "x" }, default)).Result);
    }

    // ── El gasto con notas no se cambia a escondidas ──────────────────────────

    private BuildingExpenseUpsertRequest UpsertOf(BuildingExpense e, decimal amount, Guid? periodId = null) => new()
    {
        BuildingId = e.BuildingId, ExpensePeriodId = periodId ?? e.ExpensePeriodId, Category = e.Category, SupplierName = e.SupplierName,
        Description = "Gasto de prueba editado", ExpenseDate = new DateOnly(2026, 10, 3), Amount = amount,
        DistributionType = BuildingExpenseDistributionType.ByCoefficient
    };

    [Fact]
    public async Task ConNotasAplicadas_NoSeCambiaElMontoDelGasto()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        var result = await Expenses().Update(expense.Id, UpsertOf(expense, 900_000m), default);

        Assert.Contains("notas de crédito", FinanceEnv.BadRequestText(result));
        Assert.Equal(800_000m, _env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).Amount);
    }

    [Fact]
    public async Task ConNotasAplicadas_SePuedeEditarElRestoSiElMontoNoCambia()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        var dto = FinanceEnv.Ok(await Expenses().Update(expense.Id, UpsertOf(expense, 800_000m), default));

        Assert.Equal("Gasto de prueba editado", dto.Description);
        Assert.Equal(800_000m, dto.Amount);
        Assert.Equal(1_000_000m, dto.OriginalAmount);
        Assert.Equal(200_000m, dto.CreditedAmount);
    }

    [Fact]
    public async Task ConNotasAplicadas_NoSeEliminaElGasto()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        FinanceEnv.Ok(await Create(expense, Req(200_000m)));

        var result = await Expenses().Delete(expense.Id, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("notas de crédito", (string)bad.Value!);
        Assert.False(_env.T.NewContext().BuildingExpenses.Single(x => x.Id == expense.Id).IsDeleted);
    }

    [Fact]
    public async Task TrasAnularLasNotas_SeVuelveAPoderEditarYEliminar()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, Req(200_000m)));
        FinanceEnv.Ok(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error" }, default));

        var edited = FinanceEnv.Ok(await Expenses().Update(expense.Id, UpsertOf(expense, 950_000m), default));
        Assert.Equal(950_000m, edited.Amount);
        Assert.Equal(950_000m, edited.OriginalAmount);

        Assert.IsType<NoContentResult>(await Expenses().Delete(expense.Id, default));
    }

    [Fact]
    public async Task ElGastoSinNotas_MuestraSuMontoComoOriginalYSinCredito()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        var dto = FinanceEnv.Ok(await Expenses().GetById(expense.Id, default));

        Assert.Equal(1_000_000m, dto.Amount);
        Assert.Equal(1_000_000m, dto.OriginalAmount);
        Assert.Equal(0m, dto.CreditedAmount);
    }
}
