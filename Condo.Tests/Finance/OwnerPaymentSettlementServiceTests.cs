using Condo.Api.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Condo.Tests.Finance;

/// <summary>
/// Liquidacion de pagos de propietario (OwnerPaymentSettlementService) usada directamente, sin controlador ni sesion: es el camino por el que
/// entrara la confirmacion de un proveedor de pagos, que llega anonima. El comportamiento por el controlador esta fijado en
/// <see cref="OwnerPaymentSettlementTests"/>.
/// </summary>
public class OwnerPaymentSettlementServiceTests : IDisposable
{
    private readonly FinanceEnv _env = new();
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public void Dispose() => _env.Dispose();

    private OwnerPaymentSettlementService Settlement()
    {
        var credits = new OwnerCreditService(_env.T.Db);
        return new OwnerPaymentSettlementService(_env.T.Db, credits, new ComprobanteService(_env.T.Db, credits));
    }

    private InvoiceDraftService Drafts() => new(_env.T.Db, _env.Access);

    private sealed record Fixture(Unit Unit, ApplicationUser Owner, OwnerPayment Payment);

    // Un pago ya revisado, listo para liquidar, de un propietario con una deuda de 100.000 en una unidad.
    private Fixture Arrange(decimal debt = 100_000m, decimal reviewed = 100_000m, OwnerPaymentChannel channel = OwnerPaymentChannel.App)
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.AddOwner(unit, owner);
        var period = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = 2025, Month = 1, Name = "01/2025",
            StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 1, 31), DueDate = new DateOnly(2025, 1, 21),
            Status = ExpensePeriodStatus.Published
        };
        _env.T.Db.ExpensePeriods.Add(period);
        _env.T.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, ChargeType = ExpenseChargeType.Ordinary,
            Concept = "Expensas", Amount = debt
        });
        var payment = new OwnerPayment
        {
            CompanyId = _env.Company.Id, OwnerId = owner.Id, PaymentDate = _today, DeclaredAmount = reviewed, ReviewedAmount = reviewed,
            Status = OwnerPaymentStatus.UnderReview, Reference = "PAY-2026-000001", Channel = channel
        };
        payment.Units.Add(new OwnerPaymentUnit { CompanyId = _env.Company.Id, UnitId = unit.Id });
        _env.T.Db.OwnerPayments.Add(payment);
        _env.T.Db.SaveChanges();
        return new Fixture(unit, owner, payment);
    }

    private List<Payment> SavedPayments() => _env.T.NewContext().Payments.Include(x => x.Allocations).ToList();

    // ── Contexto de sistema ──────────────────────────────────────────────────

    [Fact]
    public async Task ElSistemaLiquidaSinSesionNiAlcancePorEdificio()
    {
        // Nadie tiene edificios asignados y el rol no es de administracion: el contexto de sistema no los necesita.
        _env.Tenant.Role = "Owner";
        var f = Arrange();

        var context = new SettlementContext(_env.Company.Id, null);
        await Settlement().SettleAsync(f.Payment, context, default);
        await _env.T.Db.SaveChangesAsync();

        Assert.True(context.IsSystem);
        var payment = Assert.Single(SavedPayments());
        Assert.Equal(100_000m, payment.Amount);
        Assert.Equal(f.Unit.Id, payment.UnitId);
        Assert.Contains("Pago de propietario aprobado", payment.Notes);
        Assert.Single(payment.Allocations);
    }

    [Fact]
    public async Task LosBorradoresDeFacturaSeGeneranConContextoDeSistemaAunqueNadieTengaElEdificioAsignado()
    {
        _env.Tenant.Role = "Owner";
        var f = Arrange();
        await Settlement().SettleAsync(f.Payment, new SettlementContext(_env.Company.Id, null), default);
        f.Payment.Status = OwnerPaymentStatus.Approved;
        await _env.T.Db.SaveChangesAsync();

        var created = await Drafts().CreateDraftsFromOwnerPaymentAsync(f.Payment, _env.Admin.Id, default, systemContext: true);

        var draft = _env.T.NewContext().Invoices.Single();
        Assert.Equal(created.Single(), draft.Id);
        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Equal(100_000m, draft.MontoTotal);
        Assert.Equal(f.Payment.Id, draft.OwnerPaymentId);
    }

    [Fact]
    public async Task SinContextoDeSistemaUnaSesionSinElEdificioNoGeneraBorradores()
    {
        // Comportamiento de siempre para el personal: los borradores solo se generan para edificios de su alcance.
        _env.Tenant.Role = "CompanyOperator";
        var f = Arrange();
        await Settlement().SettleAsync(f.Payment, new SettlementContext(_env.Company.Id, new HashSet<Guid> { _env.Building.Id }), default);
        f.Payment.Status = OwnerPaymentStatus.Approved;
        await _env.T.Db.SaveChangesAsync();

        var created = await Drafts().CreateDraftsFromOwnerPaymentAsync(f.Payment, _env.Admin.Id, default);

        Assert.Empty(created);
        Assert.Empty(_env.T.NewContext().Invoices.ToList());
    }

    // ── Reglas de la liquidacion ─────────────────────────────────────────────

    [Fact]
    public async Task LiquidarNoCambiaElEstadoDelPagoNiGuardaPorSiMismo()
    {
        var f = Arrange();

        await Settlement().SettleAsync(f.Payment, new SettlementContext(_env.Company.Id, null), default);

        Assert.Equal(OwnerPaymentStatus.UnderReview, f.Payment.Status);   // quien llama decide el estado y cuando guardar
        Assert.Empty(SavedPayments());
    }

    [Fact]
    public async Task UnAlcanceQueNoIncluyeElEdificioDelComprobanteSeRechazaSinTocarNada()
    {
        var f = Arrange();

        var ex = await Assert.ThrowsAsync<OutOfScopeException>(() =>
            Settlement().SettleAsync(f.Payment, new SettlementContext(_env.Company.Id, new HashSet<Guid> { Guid.NewGuid() }), default));
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(OwnerPaymentSettlementService.OutOfScopeMessage, ex.Message);
        Assert.Empty(SavedPayments());
    }

    [Fact]
    public async Task UnMontoQueNoCubreComprobantesCompletosLanzaSinTocarNada()
    {
        var f = Arrange(debt: 100_000m, reviewed: 60_000m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Settlement().SettleAsync(f.Payment, new SettlementContext(_env.Company.Id, null), default));
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(typeof(InvalidOperationException), ex.GetType());   // ni de alcance ni de cierre
        Assert.Contains("No se aceptan pagos parciales", ex.Message);
        Assert.Empty(SavedPayments());
    }

    [Fact]
    public async Task UnaUnidadQueElPagoCubreYNoSeDeclaroSeAgregaUnaSolaVez()
    {
        var f = Arrange();
        var second = _env.T.AddUnit(_env.Building);
        _env.T.AddOwner(second, f.Owner);
        _env.T.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = _env.T.Db.ExpensePeriods.Single(x => x.Name == "01/2025").Id, UnitId = second.Id,
            ChargeType = ExpenseChargeType.Ordinary, Concept = "Expensas", Amount = 50_000m
        });
        f.Payment.ReviewedAmount = 150_000m;
        _env.T.Db.SaveChanges();

        await Settlement().SettleAsync(f.Payment, new SettlementContext(_env.Company.Id, null), default);
        await _env.T.Db.SaveChangesAsync();

        Assert.Equal(2, f.Payment.Units.Count);   // en memoria, la unidad agregada figura una sola vez
        var rows = _env.T.NewContext().OwnerPaymentUnits.Where(x => x.OwnerPaymentId == f.Payment.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(50_000m, rows.Single(r => r.UnitId == second.Id).AllocatedAmount);
    }

    // ── Referencia ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GuardarUnPagoNuevoLeAsignaLaSiguienteReferenciaDeSuEmpresa()
    {
        var f = Arrange();   // ya existe PAY-2026-000001
        var service = Settlement();

        var next = await service.NextReferenceAsync(_env.Company.Id, 2026, default);
        var other = await service.NextReferenceAsync(_env.T.AddCompany("Otra").Id, 2026, default);

        Assert.Equal("PAY-2026-000002", next);
        Assert.Equal("PAY-2026-000001", other);
        _ = f;
    }

    // ── Cobertura con saldo a favor (regla pura) ─────────────────────────────

    private static Comprobante Comp(decimal total, int month) => new()
    {
        UnitId = Guid.NewGuid(), ExpensePeriodId = Guid.NewGuid(), Year = 2025, Month = month,
        Lines = [(new ExpenseCharge { Amount = total, Concept = "Expensas" }, total)]
    };

    [Fact]
    public void ElMontoSoloGanaSiYaCierraExactoAunqueHayaSaldoAFavor()
    {
        var (coverage, credit) = OwnerPaymentSettlementService.CoverWithCredit(100_000m, 5_000m, [Comp(100_000m, 1)]);

        Assert.True(coverage.Exact);
        Assert.Equal(0m, credit);
    }

    [Fact]
    public void SiNoAlcanzaSeSumaElSaldoAFavorSoloSiCierraExacto()
    {
        var (coverage, credit) = OwnerPaymentSettlementService.CoverWithCredit(70_000m, 30_000m, [Comp(100_000m, 1)]);
        Assert.True(coverage.Exact);
        Assert.Equal(30_000m, credit);

        // Con mas saldo del necesario la suma ya no cierra el comprobante: no se usa el credito "a medias".
        var (excess, none) = OwnerPaymentSettlementService.CoverWithCredit(70_000m, 50_000m, [Comp(100_000m, 1)]);
        Assert.False(excess.Exact);
        Assert.Equal(0m, none);
    }

    [Fact]
    public void SiNiConElSaldoAFavorCierraExactoNoEsExacto()
    {
        var (coverage, credit) = OwnerPaymentSettlementService.CoverWithCredit(40_000m, 10_000m, [Comp(100_000m, 1)]);

        Assert.False(coverage.Exact);
        Assert.Equal(0m, credit);
    }

    [Fact]
    public void ElAlcanceExigeTodosLosEdificiosCubiertos()
    {
        var inside = Guid.NewGuid();
        var outside = Guid.NewGuid();
        var a = new Comprobante { BuildingId = inside };
        var b = new Comprobante { BuildingId = outside };

        Assert.True(OwnerPaymentSettlementService.CoverageInScope(new ComprobanteCoverage(true, [a], 0m), [inside]));
        Assert.False(OwnerPaymentSettlementService.CoverageInScope(new ComprobanteCoverage(true, [a, b], 0m), [inside]));
    }
}
