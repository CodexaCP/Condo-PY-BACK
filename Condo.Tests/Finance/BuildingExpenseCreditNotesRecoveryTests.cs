using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Nota de credito del proveedor sobre un gasto ya pagado: cuenta por cobrar al proveedor. Devolucion de dinero (el saldo a favor de las
/// unidades se acredita al marcarla devuelta) o descuento en la proxima factura (se acredita al registrar y el descuento se va aplicando).
/// </summary>
public partial class BuildingExpenseCreditNotesTests
{
    private static CreateBuildingExpenseCreditNoteRequest RecReq(decimal amount, SupplierRecoveryType? type, string numero = "001-001-0000123") =>
        new()
        {
            Numero = numero, IssueDate = new DateOnly(2026, 10, 2), Amount = amount, Reason = "Mercadería devuelta",
            DocumentUrl = "/uploads/nc-proveedor.pdf", RecoveryType = type
        };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private BuildingExpense Unpaid(decimal amount = 1_000_000m)
    {
        var expense = _env.AddExpense(null, amount);
        expense.DueDate = new DateOnly(2026, 12, 31);
        _env.T.Db.SaveChanges();
        return expense;
    }

    private SupplierAccountsService Accounts() => new(_env.T.Db);

    [Fact]
    public async Task GastoYaPagado_ExigeElegirComoSeRecupera()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        var result = await Create(expense, RecReq(100_000m, null));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_env.T.Db.BuildingExpenseCreditNotes);
    }

    [Fact]
    public async Task GastoPorPagar_NoGeneraCuentaPorCobrar_SoloBajaLaCuentaPorPagar()
    {
        var expense = Unpaid();

        var result = FinanceEnv.Ok(await Create(expense, RecReq(100_000m, null)));

        Assert.Equal(SupplierRecoveryType.None, result.CreditNote.RecoveryType);
        Assert.Equal(SupplierRecoveryStatus.None, result.CreditNote.RecoveryStatus);
        Assert.Equal(0m, result.CreditNote.PendingRecoveryAmount);
        Assert.Equal(900_000m, result.Expense.Amount);

        var summary = await Accounts().SummaryAsync(_env.Building.Id, Today, default);
        var row = Assert.Single(summary.Suppliers);
        Assert.Equal(900_000m, row.PayableTotal);
        Assert.Equal(0m, row.ReceivableTotal);
    }

    [Fact]
    public async Task GastoPorPagar_IgnoraLaElecciónSiSeManda()
    {
        var expense = Unpaid();

        var result = FinanceEnv.Ok(await Create(expense, RecReq(100_000m, SupplierRecoveryType.Refund)));

        Assert.Equal(SupplierRecoveryType.None, result.CreditNote.RecoveryType);
    }

    [Fact]
    public async Task Borrador_ConDevolucion_QuedaPendienteYAlMarcarlaDevueltaSeCierra()
    {
        var expense = _env.AddExpense(null, 1_000_000m);

        var created = FinanceEnv.Ok(await Create(expense, RecReq(200_000m, SupplierRecoveryType.Refund)));
        Assert.Equal(SupplierRecoveryStatus.Pending, created.CreditNote.RecoveryStatus);
        Assert.Equal(200_000m, created.CreditNote.PendingRecoveryAmount);
        Assert.Equal(800_000m, created.Expense.Amount);

        var pending = await Accounts().ReceivablesAsync(_env.Building.Id, null, SupplierAccountsService.StatusPending, Today, default);
        Assert.Equal(200_000m, Assert.Single(pending).PendingAmount);

        var done = FinanceEnv.Ok(await _api.MarkRefunded(
            created.CreditNote.Id, new MarkBuildingExpenseCreditNoteRefundedRequest { RefundedOn = Today, Reference = "Transf. 123" }, default));
        Assert.Equal(SupplierRecoveryStatus.Recovered, done.CreditNote.RecoveryStatus);
        Assert.Equal(0m, done.CreditNote.PendingRecoveryAmount);
        Assert.Equal("Transf. 123", done.CreditNote.RefundReference);
        Assert.Empty(await Accounts().ReceivablesAsync(_env.Building.Id, null, SupplierAccountsService.StatusPending, Today, default));
    }

    [Fact]
    public async Task Publicado_ConDevolucionPendiente_NoAcreditaALosPropietariosNiEntraAlLibro()
    {
        var p = PublishedExpense();

        var result = FinanceEnv.Ok(await Create(p.Expense, RecReq(200_000m, SupplierRecoveryType.Refund)));

        Assert.Equal(0m, result.CreditedToOwners);
        Assert.Empty(result.CreditNote.Allocations);
        Assert.Equal(0m, CreditOf(p.OwnerA.Id));
        Assert.Equal(0m, CreditOf(p.OwnerB.Id));
        Assert.Equal(SupplierRecoveryStatus.Pending, result.CreditNote.RecoveryStatus);
        Assert.Empty(await SupplierCreditNoteLedger.LoadAsync(_env.T.Db, new[] { _env.Building.Id }, null, null, default));
    }

    [Fact]
    public async Task Publicado_AlMarcarDevuelta_AcreditaALasUnidadesComoSiempre()
    {
        var p = PublishedExpense();
        var created = FinanceEnv.Ok(await Create(p.Expense, RecReq(200_000m, SupplierRecoveryType.Refund)));

        var done = FinanceEnv.Ok(await _api.MarkRefunded(
            created.CreditNote.Id, new MarkBuildingExpenseCreditNoteRefundedRequest { RefundedOn = Today }, default));

        Assert.Equal(200_000m, done.CreditedToOwners);
        Assert.Equal(2, done.CreditNote.Allocations.Count);
        Assert.Equal(120_000m, CreditOf(p.OwnerA.Id));
        Assert.Equal(80_000m, CreditOf(p.OwnerB.Id));
        Assert.Equal(SupplierRecoveryStatus.Recovered, done.CreditNote.RecoveryStatus);
        Assert.Equal(Today, done.CreditNote.RefundedOn);

        // Ya no se puede devolver dos veces ni se acredita dos veces.
        var again = await _api.MarkRefunded(
            created.CreditNote.Id, new MarkBuildingExpenseCreditNoteRefundedRequest { RefundedOn = Today }, default);
        Assert.IsType<BadRequestObjectResult>(again.Result);
        Assert.Equal(120_000m, CreditOf(p.OwnerA.Id));
    }

    [Fact]
    public async Task Devolucion_FechaAnteriorALaNotaOFutura_SeRechaza()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, RecReq(100_000m, SupplierRecoveryType.Refund)));

        var before = await _api.MarkRefunded(
            created.CreditNote.Id, new MarkBuildingExpenseCreditNoteRefundedRequest { RefundedOn = new DateOnly(2026, 10, 1) }, default);
        var future = await _api.MarkRefunded(
            created.CreditNote.Id, new MarkBuildingExpenseCreditNoteRefundedRequest { RefundedOn = Today.AddDays(30) }, default);

        Assert.IsType<BadRequestObjectResult>(before.Result);
        Assert.IsType<BadRequestObjectResult>(future.Result);
    }

    [Fact]
    public async Task MarcarDevuelta_SoloVaParaDevolucionesPendientes()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var discount = FinanceEnv.Ok(await Create(expense, RecReq(100_000m, SupplierRecoveryType.NextInvoiceDiscount)));

        var result = await _api.MarkRefunded(
            discount.CreditNote.Id, new MarkBuildingExpenseCreditNoteRefundedRequest { RefundedOn = Today }, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Publicado_ConDescuento_AcreditaAlRegistrarYElDescuentoSeVaAplicando()
    {
        var p = PublishedExpense();
        var created = FinanceEnv.Ok(await Create(p.Expense, RecReq(200_000m, SupplierRecoveryType.NextInvoiceDiscount)));
        Assert.Equal(200_000m, created.CreditedToOwners);
        Assert.Equal(120_000m, CreditOf(p.OwnerA.Id));

        var pendingDiscount = await Accounts().PendingDiscountAsync(_env.Building.Id, "proveedor", Today, default);
        Assert.Equal(200_000m, pendingDiscount.PendingTotal);

        var partial = FinanceEnv.Ok(await _api.AddDiscount(
            created.CreditNote.Id, new AddBuildingExpenseCreditNoteDiscountRequest { Amount = 50_000m, AppliedOn = Today }, default));
        Assert.Equal(150_000m, partial.CreditNote.PendingRecoveryAmount);
        Assert.Equal(SupplierRecoveryStatus.Pending, partial.CreditNote.RecoveryStatus);
        Assert.Equal(50_000m, partial.CreditNote.DiscountAppliedAmount);

        var over = await _api.AddDiscount(
            created.CreditNote.Id, new AddBuildingExpenseCreditNoteDiscountRequest { Amount = 150_001m, AppliedOn = Today }, default);
        Assert.IsType<BadRequestObjectResult>(over.Result);

        var full = FinanceEnv.Ok(await _api.AddDiscount(
            created.CreditNote.Id, new AddBuildingExpenseCreditNoteDiscountRequest { Amount = 150_000m, AppliedOn = Today }, default));
        Assert.Equal(SupplierRecoveryStatus.Recovered, full.CreditNote.RecoveryStatus);
        Assert.Equal(0m, full.CreditNote.PendingRecoveryAmount);
        Assert.Equal(0m, (await Accounts().PendingDiscountAsync(_env.Building.Id, "proveedor", Today, default)).PendingTotal);

        // Quitar un descuento mal cargado reabre la cuenta por cobrar.
        var removed = FinanceEnv.Ok(await _api.RemoveDiscount(created.CreditNote.Id, full.CreditNote.Discounts.Last().Id, default));
        Assert.Equal(SupplierRecoveryStatus.Pending, removed.CreditNote.RecoveryStatus);
        Assert.Equal(150_000m, removed.CreditNote.PendingRecoveryAmount);
        // Los propietarios no se tocan al aplicar o quitar descuentos.
        Assert.Equal(120_000m, CreditOf(p.OwnerA.Id));
    }

    [Fact]
    public async Task Descuento_SoloEnFacturasPosterioresDelMismoProveedorYEdificio()
    {
        var expense = _env.AddExpense(null, 1_000_000m);
        var created = FinanceEnv.Ok(await Create(expense, RecReq(100_000m, SupplierRecoveryType.NextInvoiceDiscount)));

        var sameExpense = await _api.AddDiscount(created.CreditNote.Id, new AddBuildingExpenseCreditNoteDiscountRequest
            { Amount = 10_000m, AppliedOn = Today, AppliedToExpenseId = expense.Id }, default);
        Assert.IsType<BadRequestObjectResult>(sameExpense.Result);

        var other = _env.AddExpense(null, 500_000m);
        other.SupplierName = "Otro proveedor";
        _env.T.Db.SaveChanges();
        var otherSupplier = await _api.AddDiscount(created.CreditNote.Id, new AddBuildingExpenseCreditNoteDiscountRequest
            { Amount = 10_000m, AppliedOn = Today, AppliedToExpenseId = other.Id }, default);
        Assert.IsType<BadRequestObjectResult>(otherSupplier.Result);

        var next = _env.AddExpense(null, 400_000m);
        var ok = FinanceEnv.Ok(await _api.AddDiscount(created.CreditNote.Id, new AddBuildingExpenseCreditNoteDiscountRequest
            { Amount = 10_000m, AppliedOn = Today, AppliedToExpenseId = next.Id, Note = "Vino en la factura 002" }, default));
        Assert.Equal(next.Id, Assert.Single(ok.CreditNote.Discounts).AppliedToExpenseId);
    }

    [Fact]
    public async Task Anular_NoSePuedeConDevolucionRecibidaNiConDescuentosAplicados()
    {
        var p = PublishedExpense();
        var refund = FinanceEnv.Ok(await Create(p.Expense, RecReq(100_000m, SupplierRecoveryType.Refund, "001-001-0000201")));
        var discount = FinanceEnv.Ok(await Create(p.Expense, RecReq(100_000m, SupplierRecoveryType.NextInvoiceDiscount, "001-001-0000202")));

        FinanceEnv.Ok(await _api.MarkRefunded(refund.CreditNote.Id, new MarkBuildingExpenseCreditNoteRefundedRequest { RefundedOn = Today }, default));
        FinanceEnv.Ok(await _api.AddDiscount(discount.CreditNote.Id, new AddBuildingExpenseCreditNoteDiscountRequest { Amount = 10_000m, AppliedOn = Today }, default));

        var voidRefund = await _api.Void(refund.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error" }, default);
        var voidDiscount = await _api.Void(discount.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error" }, default);

        Assert.IsType<BadRequestObjectResult>(voidRefund.Result);
        Assert.IsType<BadRequestObjectResult>(voidDiscount.Result);
    }

    [Fact]
    public async Task Anular_UnaDevolucionPendienteEnPeriodoPublicado_SePuedeYNoMuevePropietarios()
    {
        var p = PublishedExpense();
        var created = FinanceEnv.Ok(await Create(p.Expense, RecReq(100_000m, SupplierRecoveryType.Refund)));

        var voided = FinanceEnv.Ok(await _api.Void(created.CreditNote.Id, new VoidBuildingExpenseCreditNoteRequest { Reason = "Error" }, default));

        Assert.Equal(BuildingExpenseCreditNoteStatus.Voided, voided.CreditNote.Status);
        Assert.Equal(0m, CreditOf(p.OwnerA.Id));
        Assert.Empty(await Accounts().ReceivablesAsync(_env.Building.Id, null, SupplierAccountsService.StatusAll, Today, default));
    }

    [Fact]
    public async Task Preview_AvisaSiElGastoYaSePagoYSiElSaldoSeAcreditaAlDevolver()
    {
        var p = PublishedExpense();

        var preview = Assert.IsType<OkObjectResult>(
            (await _api.Preview(p.Expense.Id, new BuildingExpenseCreditNotePreviewRequest
                { Amount = 100_000m, RecoveryType = SupplierRecoveryType.Refund }, default)).Result);
        var dto = Assert.IsType<BuildingExpenseCreditNotePreviewDto>(preview.Value);

        Assert.True(dto.ExpensePaid);
        Assert.True(dto.OwnersCreditedOnRefund);
    }

    [Fact]
    public async Task CuentasPorProveedor_JuntanLoQueSeLeDebeYLoQueDebe()
    {
        var unpaid = Unpaid(1_000_000m);
        var paid = _env.AddExpense(null, 600_000m);
        FinanceEnv.Ok(await Create(paid, RecReq(100_000m, SupplierRecoveryType.Refund, "001-001-0000301")));
        var paid2 = _env.AddExpense(null, 600_000m);
        FinanceEnv.Ok(await Create(paid2, RecReq(40_000m, SupplierRecoveryType.NextInvoiceDiscount, "001-001-0000302")));

        var summary = await Accounts().SummaryAsync(_env.Building.Id, Today, default);

        var row = Assert.Single(summary.Suppliers);
        Assert.Equal(1_000_000m, row.PayableTotal);
        Assert.Equal(100_000m, row.PendingRefundTotal);
        Assert.Equal(40_000m, row.PendingDiscountTotal);
        Assert.Equal(140_000m, row.ReceivableTotal);
        Assert.Equal(860_000m, row.NetBalance);
        Assert.Equal(unpaid.Amount, row.PayableTotal);
    }

    [Fact]
    public async Task CuentasPorPagar_SePuedenFiltrarPorNombreDelProveedor()
    {
        Unpaid(1_000_000m);
        var other = Unpaid(300_000m);
        other.SupplierName = "Otra Empresa S.A.";
        _env.T.Db.SaveChanges();

        var all = await new FinancePayablesService(_env.T.Db).ListAsync(_env.Building.Id, "all", null, null, null, 1, 50, Today, default);
        var onlyOther = await new FinancePayablesService(_env.T.Db).ListAsync(_env.Building.Id, "all", null, null, null, 1, 50, Today, default, "otra empresa s.a.");

        Assert.Equal(2, all.TotalCount);
        var row = Assert.Single(onlyOther.Items);
        Assert.Equal("Otra Empresa S.A.", row.SupplierName);
    }
}
