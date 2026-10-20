using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Finance;

/// <summary>
/// Pruebas de caracterizacion de como se aprueba y se registra un pago de propietario HOY (OwnerPaymentsController). Fijan el comportamiento
/// actual antes de extraer la liquidacion a un servicio propio: cada prueba describe lo que el sistema hace, no lo que deberia hacer.
/// Si una de estas pruebas cambia despues de la extraccion, el comportamiento cambio.
/// </summary>
public class OwnerPaymentSettlementTests : IDisposable
{
    private readonly FinanceEnv _env = new();
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public OwnerPaymentSettlementTests()
    {
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Access.Buildings.Add(_env.Building.Id);
        AsStaff();
    }

    public void Dispose() => _env.Dispose();

    // ── Armado ───────────────────────────────────────────────────────────────

    private OwnerPaymentsController Api()
    {
        var credits = new OwnerCreditService(_env.T.Db);
        var comprobantes = new ComprobanteService(_env.T.Db, credits);
        return new OwnerPaymentsController(
            _env.T.Db, _env.Tenant, _env.Access, credits, comprobantes, new InvoiceDraftService(_env.T.Db, _env.Access),
            new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance),
            new OwnerPaymentSettlementService(_env.T.Db, credits, comprobantes));
    }

    private CondoDbContext Fresh() => _env.T.NewContext();

    private void AsStaff(string role = "CompanyAdmin")
    {
        _env.Tenant.Role = role;
        _env.Tenant.UserId = _env.Admin.Id;
    }

    private void AsOwner(ApplicationUser owner)
    {
        _env.Tenant.Role = "Owner";
        _env.Tenant.UserId = owner.Id;
    }

    private ApplicationUser AddOwner(params Unit[] units)
    {
        var owner = _env.T.AddUser(_env.Company, UserRole.Owner);
        foreach (var unit in units) _env.T.AddOwner(unit, owner, primary: true);
        return owner;
    }

    private ExpensePeriod AddPeriod(Building building, int year, int month, ExpensePeriodStatus status = ExpensePeriodStatus.Published)
    {
        var first = new DateOnly(year, month, 1);
        var period = new ExpensePeriod
        {
            CompanyId = building.CompanyId!.Value, BuildingId = building.Id, Year = year, Month = month, Name = $"{month:00}/{year}",
            StartDate = first, EndDate = first.AddMonths(1).AddDays(-1), DueDate = first.AddDays(20), Status = status
        };
        _env.T.Db.ExpensePeriods.Add(period);
        _env.T.Db.SaveChanges();
        return period;
    }

    private ExpenseCharge AddCharge(Unit unit, ExpensePeriod period, decimal amount, string concept = "Expensas", ExpenseChargeType type = ExpenseChargeType.Ordinary)
    {
        var charge = new ExpenseCharge
        {
            CompanyId = unit.CompanyId, ExpensePeriodId = period.Id, UnitId = unit.Id, ChargeType = type, Concept = concept, Amount = amount
        };
        _env.T.Db.ExpenseCharges.Add(charge);
        _env.T.Db.SaveChanges();
        return charge;
    }

    private void AddCredit(ApplicationUser owner, decimal amount)
    {
        _env.T.Db.OwnerCredits.Add(new OwnerCredit { CompanyId = _env.Company.Id, OwnerId = owner.Id, Amount = amount });
        _env.T.Db.SaveChanges();
    }

    private async Task<OwnerPaymentDto> Register(ApplicationUser owner, decimal amount, PaymentMethod method = PaymentMethod.BankTransfer, DateOnly? date = null)
    {
        AsStaff();
        return FinanceEnv.Ok(await Api().Register(
            new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = amount, Method = method, PaymentDate = date ?? _today, ExternalReference = "TRF-1" }, default));
    }

    // El propietario declara desde la app (queda Pendiente).
    private async Task<OwnerPaymentDto> Declare(ApplicationUser owner, decimal amount, params Unit[] units)
    {
        AsOwner(owner);
        return FinanceEnv.Ok(await Api().Create(
            new OwnerPaymentCreateRequest { DeclaredAmount = amount, PaymentDate = _today, ComprobanteUrl = "comp.png", UnitIds = units.Select(u => u.Id).ToList() }, default));
    }

    private async Task<OwnerPaymentDto> Review(Guid id, decimal amount)
    {
        AsStaff();
        return FinanceEnv.Ok(await Api().Review(id, new OwnerPaymentReviewRequest { ReviewedAmount = amount }, default));
    }

    private async Task<OwnerPaymentDto> Approve(Guid id)
    {
        AsStaff();
        return FinanceEnv.Ok(await Api().Approve(id, default));
    }

    private List<Payment> PaymentsOf(ApplicationUser owner)
    {
        var db = Fresh();
        var unitIds = db.UnitOwners.Where(x => x.OwnerId == owner.Id).Select(x => x.UnitId).ToList();
        return db.Payments.Include(x => x.Allocations).Where(x => unitIds.Contains(x.UnitId)).ToList()
            .OrderBy(x => x.CreatedAtUtc).ToList();
    }

    private List<Notification> NotificationsFor(Guid userId) =>
        Fresh().Notifications.Where(x => x.RecipientId == userId).ToList();

    private List<Invoice> InvoicesOf(ApplicationUser owner)
    {
        var db = Fresh();
        var unitIds = db.UnitOwners.Where(x => x.OwnerId == owner.Id).Select(x => x.UnitId).ToList();
        return db.Invoices.Where(x => unitIds.Contains(x.UnitId)).ToList();
    }

    private async Task<RegisterPreviewDto> Preview(ApplicationUser owner)
    {
        AsStaff();
        return FinanceEnv.Ok(await Api().GetRegisterPreview(owner.Id, default));
    }

    private static string Text<T>(ActionResult<T> result, int status)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(status, obj.StatusCode);
        return obj.Value?.ToString() ?? string.Empty;
    }

    private string Year => DateTime.UtcNow.Year.ToString();

    // ═════════════════════════════════════════════════════════════════════════
    // Registro por el sistema (canal Web, nace aprobado)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RegistrarUnPagoExactoCreaUnPagoPorComprobanteConSusAsignacionesYLoDejaAprobado()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        var period = AddPeriod(_env.Building, 2025, 1);
        var expensas = AddCharge(unit, period, 80_000m, "Expensas");
        var luz = AddCharge(unit, period, 20_000m, "Luz", ExpenseChargeType.Individual);

        var dto = await Register(owner, 100_000m, PaymentMethod.Cash);

        Assert.Equal("Approved", dto.Status);
        Assert.Equal("Web", dto.Channel);
        Assert.Equal("Cash", dto.Method);
        Assert.Equal($"PAY-{Year}-000001", dto.Reference);
        Assert.Equal(100_000m, dto.DeclaredAmount);
        Assert.Equal(100_000m, dto.ReviewedAmount);
        Assert.NotNull(dto.ResolvedAt);
        Assert.Equal(100_000m, Assert.Single(dto.Units).AllocatedAmount);

        // Un comprobante (unidad + periodo) = UN Payment por el total, con una asignacion por cada cargo por su pendiente exacto.
        var payment = Assert.Single(PaymentsOf(owner));
        Assert.Equal(100_000m, payment.Amount);
        Assert.Equal(period.Id, payment.ExpensePeriodId);
        Assert.Equal(unit.Id, payment.UnitId);
        Assert.Equal(_today, payment.PaymentDate);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(dto.Reference, payment.Reference);
        Assert.False(payment.IsReversed);
        Assert.Contains("Pago registrado por el sistema", payment.Notes);
        Assert.Contains("Comprobante 01/2025 unidad", payment.Notes);
        Assert.Contains("Doc.: TRF-1", payment.Notes);
        Assert.Equal(2, payment.Allocations.Count);
        Assert.Equal(80_000m, payment.Allocations.Single(a => a.ExpenseChargeId == expensas.Id).AllocatedAmount);
        Assert.Equal(20_000m, payment.Allocations.Single(a => a.ExpenseChargeId == luz.Id).AllocatedAmount);

        // La deuda queda saldada.
        Assert.Empty((await Preview(owner)).Comprobantes);
    }

    [Fact]
    public async Task RegistrarGeneraElAvisoAlPropietarioYUnBorradorDeFacturaPorComprobante()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 60_000m);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 2), 40_000m);

        var dto = await Register(owner, 100_000m);

        var notice = Assert.Single(NotificationsFor(owner.Id));
        Assert.Equal(NotificationType.PaymentApproved, notice.Type);
        Assert.Equal("Registramos tu pago", notice.Title);
        Assert.Equal(dto.Id, notice.EntityId);
        Assert.Contains("Gs. 100.000", notice.Body);

        var drafts = InvoicesOf(owner);
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d =>
        {
            Assert.Equal(InvoiceStatus.Draft, d.Status);
            Assert.Equal(dto.Id, d.OwnerPaymentId);
            Assert.Equal(_env.Admin.Id, d.CreatedByUserId);
        });
        Assert.Equal(new[] { 40_000m, 60_000m }, drafts.Select(d => d.MontoTotal).OrderBy(x => x).ToArray());
        Assert.Equal(2, Fresh().InvoiceAuditLogs.Count(x => x.Action == InvoiceAuditAction.DraftCreated));
    }

    [Fact]
    public async Task ElPagoCubreLosComprobantesDelMasAntiguoAlMasNuevoYDejaElResto()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        var enero = AddPeriod(_env.Building, 2025, 1);
        var febrero = AddPeriod(_env.Building, 2025, 2);
        var marzo = AddPeriod(_env.Building, 2025, 3);
        // Se cargan en desorden a proposito: el orden sale del periodo, no del alta.
        AddCharge(unit, marzo, 300_000m);
        AddCharge(unit, enero, 100_000m);
        AddCharge(unit, febrero, 200_000m);

        await Register(owner, 300_000m);   // enero + febrero

        var paid = PaymentsOf(owner);
        Assert.Equal(new[] { enero.Id, febrero.Id }.OrderBy(x => x), paid.Select(p => p.ExpensePeriodId).OrderBy(x => x));
        var left = Assert.Single((await Preview(owner)).Comprobantes);
        Assert.Equal(marzo.Id, left.ExpensePeriodId);
        Assert.Equal(300_000m, left.Total);
    }

    [Fact]
    public async Task UnMontoQueNoCubreComprobantesCompletosSeRechazaYNoDejaNada()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);

        AsStaff();
        var parcial = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 60_000m, PaymentDate = _today }, default);
        var excedido = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 150_000m, PaymentDate = _today }, default);

        Assert.Contains("No se aceptan pagos parciales", Text(parcial, 400));
        Assert.Contains("No se aceptan pagos parciales", Text(excedido, 400));
        Assert.Empty(Fresh().OwnerPayments.IgnoreQueryFilters().Where(x => x.OwnerId == owner.Id));
        Assert.Empty(PaymentsOf(owner));
        Assert.Empty(InvoicesOf(owner));
        Assert.Empty(NotificationsFor(owner.Id));
    }

    [Fact]
    public async Task LaToleranciaEsMedioGuarani()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000.40m);

        var dto = await Register(owner, 100_000m);

        // Se imputa el pendiente exacto de cada linea, no el monto redondeado que declaro el usuario.
        Assert.Equal(100_000.40m, Assert.Single(PaymentsOf(owner)).Amount);
        Assert.Equal(100_000.40m, Assert.Single(dto.Units).AllocatedAmount);
    }

    [Fact]
    public async Task UnPagoSinDeudaSeRechazaConElMensajeDeSinComprobantes()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);

        AsStaff();
        var result = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 10_000m, PaymentDate = _today }, default);

        Assert.Equal("No tiene comprobantes pendientes de pago.", Text(result, 400));
    }

    [Fact]
    public async Task PagarDosVecesElMismoComprobanteFallaLaSegunda()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);

        await Register(owner, 100_000m);
        AsStaff();
        var second = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 100_000m, PaymentDate = _today }, default);

        Assert.Equal("No tiene comprobantes pendientes de pago.", Text(second, 400));
        Assert.Single(PaymentsOf(owner));
        Assert.Single(InvoicesOf(owner));
    }

    [Fact]
    public async Task UnPropietarioConDosUnidadesPagaLosComprobantesDeAmbasEnUnSoloPago()
    {
        var a1 = _env.T.AddUnit(_env.Building, "A1");
        var a2 = _env.T.AddUnit(_env.Building, "A2");
        var owner = AddOwner(a1, a2);
        var enero = AddPeriod(_env.Building, 2025, 1);
        AddCharge(a1, enero, 100_000m);
        AddCharge(a2, enero, 50_000m);

        var dto = await Register(owner, 150_000m);

        Assert.Equal(2, PaymentsOf(owner).Count);
        Assert.Equal(2, dto.Units.Count);
        Assert.Equal(100_000m, dto.Units.Single(u => u.UnitCode == "A1").AllocatedAmount);
        Assert.Equal(50_000m, dto.Units.Single(u => u.UnitCode == "A2").AllocatedAmount);
        Assert.Equal(2, InvoicesOf(owner).Count);
    }

    // ── Saldo a favor ────────────────────────────────────────────────────────

    [Fact]
    public async Task ElSaldoAFavorCompletaUnPagoQueNoAlcanzaYElPaymentSiempreVaPorElTotalDelComprobante()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        AddCredit(owner, 30_000m);

        var dto = await Register(owner, 70_000m);

        var payment = Assert.Single(PaymentsOf(owner));
        Assert.Equal(100_000m, payment.Amount);                      // el Payment (y la factura) valen el comprobante completo
        Assert.Contains("Incluye Gs. 30.000 de saldo a favor", payment.Notes);
        Assert.Equal(0m, Fresh().OwnerCredits.Single(x => x.OwnerId == owner.Id).Amount);

        var movements = Fresh().OwnerCreditMovements.Where(x => x.OwnerId == owner.Id).ToList();
        var applied = Assert.Single(movements, m => m.Kind == OwnerCreditMovementKind.Applied);
        Assert.Equal(30_000m, applied.Amount);
        Assert.Equal(payment.Id, applied.PaymentId);
        Assert.All(movements.Where(m => m.Kind == OwnerCreditMovementKind.Generated), m => Assert.Equal(0m, m.RemainingAmount));
        Assert.Equal(100_000m, Assert.Single(dto.Units).AllocatedAmount);
        Assert.Equal(100_000m, Assert.Single(InvoicesOf(owner)).MontoTotal);
    }

    [Fact]
    public async Task UnPagoQueYaCerrabaExactoNoConsumeSaldoAFavor()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        AddCredit(owner, 5_000m);

        await Register(owner, 100_000m);

        Assert.Equal(5_000m, Fresh().OwnerCredits.Single(x => x.OwnerId == owner.Id).Amount);
        Assert.DoesNotContain(Fresh().OwnerCreditMovements.Where(x => x.OwnerId == owner.Id).ToList(), m => m.Kind == OwnerCreditMovementKind.Applied);
        Assert.DoesNotContain("saldo a favor", Assert.Single(PaymentsOf(owner)).Notes);
    }

    [Fact]
    public async Task ElSaldoAFavorSeConsumeDelComprobanteMasAntiguoEnAdelante()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        var enero = AddPeriod(_env.Building, 2025, 1);
        var febrero = AddPeriod(_env.Building, 2025, 2);
        AddCharge(unit, enero, 100_000m);
        AddCharge(unit, febrero, 100_000m);
        AddCredit(owner, 40_000m);

        await Register(owner, 160_000m);

        var payments = PaymentsOf(owner);
        Assert.Equal(2, payments.Count);
        var viaEnero = payments.Single(p => p.ExpensePeriodId == enero.Id);
        var viaFebrero = payments.Single(p => p.ExpensePeriodId == febrero.Id);
        Assert.Contains("Incluye Gs. 40.000 de saldo a favor", viaEnero.Notes);
        Assert.DoesNotContain("saldo a favor", viaFebrero.Notes);
        Assert.Equal(0m, Fresh().OwnerCredits.Single(x => x.OwnerId == owner.Id).Amount);
    }

    // ── Validaciones de entrada ──────────────────────────────────────────────

    [Theory]
    [InlineData("Owner")]
    [InlineData("Resident")]
    public async Task SoloElPersonalPuedeRegistrarPagos(string role)
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);

        _env.Tenant.Role = role;
        var result = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 100_000m, PaymentDate = _today }, default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(PaymentsOf(owner));
    }

    [Fact]
    public async Task LasValidacionesBasicasDelRegistro()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        AsStaff();

        Task<ActionResult<OwnerPaymentDto>> Call(Action<OwnerPaymentRegisterRequest> tweak)
        {
            var request = new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 100_000m, PaymentDate = _today };
            tweak(request);
            return Api().Register(request, default);
        }

        Assert.Equal("El propietario es obligatorio.", Text(await Call(r => r.OwnerId = Guid.Empty), 400));
        Assert.Equal("El monto debe ser mayor a cero.", Text(await Call(r => r.Amount = 0), 400));
        Assert.Equal("La fecha de pago es obligatoria.", Text(await Call(r => r.PaymentDate = default), 400));
        Assert.Equal("La fecha de pago no puede ser futura.", Text(await Call(r => r.PaymentDate = _today.AddDays(5)), 400));
        Assert.Equal("El método de pago no es válido.", Text(await Call(r => r.Method = (PaymentMethod)99), 400));
        Assert.Contains("100 caracteres", Text(await Call(r => r.ExternalReference = new string('x', 101)), 400));
        Assert.Contains("500 caracteres", Text(await Call(r => r.Notes = new string('x', 501)), 400));
        Assert.IsType<NotFoundResult>((await Call(r => r.OwnerId = Guid.NewGuid())).Result);
        Assert.Empty(PaymentsOf(owner));
    }

    [Fact]
    public async Task UnPagoConFechaDeMananaSeAcepta()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);

        var dto = await Register(owner, 100_000m, date: _today.AddDays(1));

        Assert.Equal(_today.AddDays(1), dto.PaymentDate);
    }

    [Fact]
    public async Task SiElPropietarioTieneUnPagoDeLaAppSinResolverElRegistroSeRechazaConConflicto()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);

        AsStaff();
        var result = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 100_000m, PaymentDate = _today }, default);

        Assert.Contains(declared.Reference, Text(result, 409));
        Assert.Empty(PaymentsOf(owner));
    }

    // ── Alcance por edificio ─────────────────────────────────────────────────

    [Fact]
    public async Task UnPropietarioFueraDelAlcanceDelPersonalNoSeEncuentra()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);

        AsStaff("CompanyOperator");
        _env.Access.Buildings.Clear();
        var result = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 100_000m, PaymentDate = _today }, default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(PaymentsOf(owner));
    }

    [Fact]
    public async Task UnPagoQueCubreUnComprobanteDeUnEdificioFueraDelAlcanceSeRechazaConProhibido()
    {
        var other = _env.T.AddBuilding(_env.Company, "Otro edificio");
        var mine = _env.T.AddUnit(_env.Building, "M1");
        var theirs = _env.T.AddUnit(other, "T1");
        var owner = AddOwner(mine, theirs);
        // El comprobante mas antiguo es del edificio que el personal no tiene asignado.
        AddCharge(theirs, AddPeriod(other, 2025, 1), 100_000m);
        AddCharge(mine, AddPeriod(_env.Building, 2025, 2), 50_000m);

        AsStaff("BuildingManager");
        var result = await Api().Register(new OwnerPaymentRegisterRequest { OwnerId = owner.Id, Amount = 100_000m, PaymentDate = _today }, default);

        Assert.Contains("edificios que no tenés asignados", Text(result, 403));
        Assert.Empty(PaymentsOf(owner));
        Assert.Empty(Fresh().OwnerPayments.IgnoreQueryFilters().Where(x => x.OwnerId == owner.Id));
    }

    // ── Numeracion ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LaReferenciaEsCorrelativaPorEmpresaYNoSeReutiliza()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 2), 100_000m);

        var first = await Register(owner, 100_000m);
        var second = await Register(owner, 100_000m);

        Assert.Equal($"PAY-{Year}-000001", first.Reference);
        Assert.Equal($"PAY-{Year}-000002", second.Reference);

        // Otra empresa empieza de nuevo en 000001.
        var company2 = _env.T.AddCompany("Otra empresa");
        var building2 = _env.T.AddBuilding(company2, "Edificio 2");
        var unit2 = _env.T.AddUnit(building2);
        var owner2 = _env.T.AddUser(company2, UserRole.Owner);
        _env.T.AddOwner(unit2, owner2);
        AddCharge(unit2, AddPeriod(building2, 2025, 1), 70_000m);
        _env.Tenant.CompanyId = company2.Id;
        _env.Access.Buildings.Clear();
        _env.Access.Buildings.Add(building2.Id);
        var third = await Register(owner2, 70_000m);
        Assert.Equal($"PAY-{Year}-000001", third.Reference);
    }

    [Fact]
    public async Task UnPagoRechazadoOEliminadoNoLiberaSuNumeroDeReferencia()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);
        AsStaff();
        FinanceEnv.Ok(await Api().Reject(declared.Id, new OwnerPaymentRejectRequest { RejectionReason = "Comprobante ilegible" }, default));

        var registered = await Register(owner, 100_000m);

        Assert.Equal($"PAY-{Year}-000001", declared.Reference);
        Assert.Equal($"PAY-{Year}-000002", registered.Reference);
    }

    // ── Avisos ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SiElEdificioApagoElAvisoDePagoRecibidoElPagoSeAplicaSinAviso()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        _env.T.Db.BuildingNoticeRules.Add(new BuildingNoticeRule
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Kind = NoticeKind.PaymentReceived, IsActive = false
        });
        _env.T.Db.SaveChanges();

        await Register(owner, 100_000m);

        Assert.Single(PaymentsOf(owner));
        Assert.Empty(NotificationsFor(owner.Id));
        Assert.Single(InvoicesOf(owner));   // el borrador de factura no depende del aviso
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Camino de la app: declarar -> revisar -> aprobar / rechazar
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeclararUnPagoLoDejaPendienteSinTocarLaDeudaYAvisaALaAdministracion()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);

        var dto = await Declare(owner, 100_000m, unit);

        Assert.Equal("Pending", dto.Status);
        Assert.Equal("App", dto.Channel);
        Assert.Equal($"PAY-{Year}-000001", dto.Reference);
        Assert.Null(dto.ReviewedAmount);
        Assert.Empty(PaymentsOf(owner));
        Assert.Single((await Preview(owner)).Comprobantes);

        var notice = Assert.Single(NotificationsFor(_env.Admin.Id));
        Assert.Equal(NotificationType.OwnerPaymentSubmitted, notice.Type);
        Assert.Equal(dto.Id, notice.EntityId);
        Assert.Contains("100.000", notice.Body);
    }

    [Fact]
    public async Task DeclararValidaLasUnidadesYQueElMontoCubraComprobantesCompletos()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var foreignUnit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        AsOwner(owner);

        var parcial = await Api().Create(new OwnerPaymentCreateRequest { DeclaredAmount = 40_000m, PaymentDate = _today, UnitIds = [unit.Id] }, default);
        var ajena = await Api().Create(new OwnerPaymentCreateRequest { DeclaredAmount = 100_000m, PaymentDate = _today, UnitIds = [foreignUnit.Id] }, default);
        var sinUnidad = await Api().Create(new OwnerPaymentCreateRequest { DeclaredAmount = 100_000m, PaymentDate = _today, UnitIds = [] }, default);
        var cero = await Api().Create(new OwnerPaymentCreateRequest { DeclaredAmount = 0, PaymentDate = _today, UnitIds = [unit.Id] }, default);

        Assert.Contains("No se aceptan pagos parciales", Text(parcial, 400));
        Assert.Equal("Una o más unidades no pertenecen a este propietario.", Text(ajena, 400));
        Assert.Equal("Debe seleccionar al menos una unidad.", Text(sinUnidad, 400));
        Assert.Equal("El monto declarado debe ser mayor a cero.", Text(cero, 400));
        Assert.Empty(Fresh().OwnerPayments.IgnoreQueryFilters().Where(x => x.OwnerId == owner.Id));
    }

    [Fact]
    public async Task SoloElPropietarioPuedeDeclararUnPago()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);

        AsStaff();
        var result = await Api().Create(new OwnerPaymentCreateRequest { DeclaredAmount = 100_000m, PaymentDate = _today, UnitIds = [unit.Id] }, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task RevisarPasaAEnRevisionYAvisaAlPropietarioPeroNoCreaPagos()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);

        var reviewed = await Review(declared.Id, 100_000m);

        Assert.Equal("UnderReview", reviewed.Status);
        Assert.Equal(100_000m, reviewed.ReviewedAmount);
        Assert.Equal(_env.Admin.Id, Fresh().OwnerPayments.Single(x => x.Id == declared.Id).ReviewedByUserId);
        Assert.Empty(PaymentsOf(owner));
        Assert.Contains(NotificationsFor(owner.Id), n => n.Type == NotificationType.PaymentUnderReview);
    }

    [Fact]
    public async Task RevisarConUnMontoQueNoCubreComprobantesOUnPagoYaResueltoSeRechaza()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);

        AsStaff();
        var mismatch = await Api().Review(declared.Id, new OwnerPaymentReviewRequest { ReviewedAmount = 30_000m }, default);
        var zero = await Api().Review(declared.Id, new OwnerPaymentReviewRequest { ReviewedAmount = 0 }, default);
        Assert.Contains("Rechace el pago", Text(mismatch, 400));
        Assert.Equal("El monto revisado debe ser mayor a cero.", Text(zero, 400));

        await Review(declared.Id, 100_000m);
        var again = await Api().Review(declared.Id, new OwnerPaymentReviewRequest { ReviewedAmount = 100_000m }, default);
        Assert.Equal("Solo se pueden revisar pagos en estado PENDIENTE.", Text(again, 400));
    }

    [Fact]
    public async Task AprobarLiquidaElPagoDeLaAppIgualQueElRegistroPeroConSuPropiaNota()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        var enero = AddPeriod(_env.Building, 2025, 1);
        var expensas = AddCharge(unit, enero, 70_000m);
        var mora = AddCharge(unit, enero, 5_000m, "Mora", ExpenseChargeType.Adjustment);
        var declared = await Declare(owner, 75_000m, unit);
        await Review(declared.Id, 75_000m);

        var approved = await Approve(declared.Id);

        Assert.Equal("Approved", approved.Status);
        Assert.Equal("App", approved.Channel);
        Assert.NotNull(approved.ResolvedAt);
        var payment = Assert.Single(PaymentsOf(owner));
        Assert.Equal(75_000m, payment.Amount);
        Assert.Equal(declared.Reference, payment.Reference);
        Assert.Contains("Pago de propietario aprobado", payment.Notes);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(2, payment.Allocations.Count);
        Assert.Equal(70_000m, payment.Allocations.Single(a => a.ExpenseChargeId == expensas.Id).AllocatedAmount);
        Assert.Equal(5_000m, payment.Allocations.Single(a => a.ExpenseChargeId == mora.Id).AllocatedAmount);
        Assert.Equal(75_000m, Assert.Single(approved.Units).AllocatedAmount);

        var notice = Assert.Single(NotificationsFor(owner.Id), n => n.Type == NotificationType.PaymentApproved);
        Assert.Equal("Tu pago fue aprobado", notice.Title);
        var draft = Assert.Single(InvoicesOf(owner));
        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Equal(declared.Id, draft.OwnerPaymentId);
        Assert.Empty((await Preview(owner)).Comprobantes);
    }

    [Fact]
    public async Task AprobarAgregaLasUnidadesQueElPagoCubreAunqueElPropietarioNoLasDeclaro()
    {
        var a1 = _env.T.AddUnit(_env.Building, "A1");
        var a2 = _env.T.AddUnit(_env.Building, "A2");
        var owner = AddOwner(a1, a2);
        var enero = AddPeriod(_env.Building, 2025, 1);
        AddCharge(a1, enero, 100_000m);
        AddCharge(a2, enero, 50_000m);
        var declared = await Declare(owner, 150_000m, a1);   // declara una sola unidad, pero el monto cubre las dos
        await Review(declared.Id, 150_000m);

        var approved = await Approve(declared.Id);

        Assert.Equal(2, PaymentsOf(owner).Count);
        // Lo guardado es correcto: una fila por unidad.
        var rows = Fresh().OwnerPaymentUnits.Where(x => x.OwnerPaymentId == declared.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(100_000m, rows.Single(r => r.UnitId == a1.Id).AllocatedAmount);
        Assert.Equal(50_000m, rows.Single(r => r.UnitId == a2.Id).AllocatedAmount);
        // La respuesta de Aprobar lista cada unidad una sola vez. (Antes de extraer la liquidacion a su servicio listaba DOS veces la unidad
        // agregada, porque se sumaba a la coleccion y ademas al contexto; el dato guardado siempre fue correcto.)
        Assert.Equal(2, approved.Units.Count);
        Assert.Equal(50_000m, approved.Units.Single(u => u.UnitCode == "A2").AllocatedAmount);
    }

    [Fact]
    public async Task AprobarSinRevisarOAprobarDosVecesSeRechazaYNoDuplicaPagos()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);

        AsStaff();
        var sinRevisar = await Api().Approve(declared.Id, default);
        Assert.Equal("Solo se pueden aprobar pagos en estado EN REVISIÓN.", Text(sinRevisar, 400));
        Assert.Empty(PaymentsOf(owner));

        await Review(declared.Id, 100_000m);
        await Approve(declared.Id);
        var otraVez = await Api().Approve(declared.Id, default);

        Assert.Equal("Solo se pueden aprobar pagos en estado EN REVISIÓN.", Text(otraVez, 400));
        Assert.Single(PaymentsOf(owner));
        Assert.Single(InvoicesOf(owner));
    }

    [Fact]
    public async Task SiLaDeudaCambioDespuesDeRevisarAprobarSeRechazaYElPagoQuedaEnRevision()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        var enero = AddPeriod(_env.Building, 2025, 1);
        AddCharge(unit, enero, 100_000m);
        var declared = await Declare(owner, 100_000m, unit);
        await Review(declared.Id, 100_000m);

        // Entra un recargo (por ejemplo, la mora del dia) entre la revision y la aprobacion: el total del comprobante ya no coincide.
        AddCharge(unit, enero, 2_000m, "Mora", ExpenseChargeType.Adjustment);
        AsStaff();
        var result = await Api().Approve(declared.Id, default);

        Assert.Contains("Rechace el pago", Text(result, 400));
        Assert.Equal(OwnerPaymentStatus.UnderReview, Fresh().OwnerPayments.Single(x => x.Id == declared.Id).Status);
        Assert.Empty(PaymentsOf(owner));
        Assert.Empty(InvoicesOf(owner));
        Assert.DoesNotContain(NotificationsFor(owner.Id), n => n.Type == NotificationType.PaymentApproved);
    }

    [Fact]
    public async Task SiOtroPagoYaCubrioElComprobanteAprobarSeRechaza()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);
        await Review(declared.Id, 100_000m);

        // Un registro del personal salda el mismo comprobante mientras el pago de la app seguia en revision.
        var other = new OwnerPayment
        {
            CompanyId = _env.Company.Id, OwnerId = owner.Id, PaymentDate = _today, DeclaredAmount = 100_000m, ReviewedAmount = 100_000m,
            Status = OwnerPaymentStatus.UnderReview, Reference = "PAY-X", Channel = OwnerPaymentChannel.Web
        };
        other.Units.Add(new OwnerPaymentUnit { CompanyId = _env.Company.Id, UnitId = unit.Id });
        _env.T.Db.OwnerPayments.Add(other);
        _env.T.Db.SaveChanges();
        AsStaff();
        FinanceEnv.Ok(await Api().Approve(other.Id, default));

        var result = await Api().Approve(declared.Id, default);

        Assert.Equal("No tiene comprobantes pendientes de pago. Rechace el pago para que el propietario lo envíe nuevamente.", Text(result, 400));
        Assert.Single(PaymentsOf(owner));
    }

    [Fact]
    public async Task AprobarNoDejaPagosAMediasSiAlgoFallaEnLaLiquidacion()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);
        await Review(declared.Id, 100_000m);
        // Se altera el monto revisado: la liquidacion falla antes de crear nada.
        var tracked = _env.T.Db.OwnerPayments.Single(x => x.Id == declared.Id);
        tracked.ReviewedAmount = 99_000m;
        _env.T.Db.SaveChanges();

        AsStaff();
        var result = await Api().Approve(declared.Id, default);

        Text(result, 400);
        Assert.Empty(PaymentsOf(owner));
        Assert.Empty(Fresh().PaymentAllocations.ToList());
        Assert.Equal(OwnerPaymentStatus.UnderReview, Fresh().OwnerPayments.Single(x => x.Id == declared.Id).Status);
    }

    [Fact]
    public async Task RechazarExigeMotivoYSoloValeAntesDeResolver()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);

        AsStaff();
        var sinMotivo = await Api().Reject(declared.Id, new OwnerPaymentRejectRequest { RejectionReason = "  " }, default);
        var largo = await Api().Reject(declared.Id, new OwnerPaymentRejectRequest { RejectionReason = new string('x', 501) }, default);
        Assert.Equal("El motivo de rechazo es obligatorio.", Text(sinMotivo, 400));
        Assert.Equal("El motivo no puede superar los 500 caracteres.", Text(largo, 400));

        var rejected = FinanceEnv.Ok(await Api().Reject(declared.Id, new OwnerPaymentRejectRequest { RejectionReason = "Comprobante ilegible" }, default));
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Comprobante ilegible", rejected.RejectionReason);
        Assert.Empty(PaymentsOf(owner));
        Assert.Contains(NotificationsFor(owner.Id), n => n.Type == NotificationType.PaymentRejected);

        var again = await Api().Reject(declared.Id, new OwnerPaymentRejectRequest { RejectionReason = "Otra vez" }, default);
        Assert.Equal("No se puede rechazar un pago ya resuelto.", Text(again, 400));
        // La deuda sigue intacta: el propietario puede volver a declarar.
        Assert.Single((await Preview(owner)).Comprobantes);
    }

    [Fact]
    public async Task UnPagoAprobadoNoSePuedeRechazar()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var registered = await Register(owner, 100_000m);

        AsStaff();
        var result = await Api().Reject(registered.Id, new OwnerPaymentRejectRequest { RejectionReason = "Error" }, default);

        Assert.Equal("No se puede rechazar un pago ya resuelto.", Text(result, 400));
        Assert.Single(PaymentsOf(owner));
    }

    // ── Alcance al aprobar ───────────────────────────────────────────────────

    [Fact]
    public async Task UnEncargadoSinTodosLosEdificiosDelPagoNoPuedeAprobarloYQuedaEnRevision()
    {
        var other = _env.T.AddBuilding(_env.Company, "Otro edificio");
        var mine = _env.T.AddUnit(_env.Building, "M1");
        var theirs = _env.T.AddUnit(other, "T1");
        var owner = AddOwner(mine, theirs);
        AddCharge(mine, AddPeriod(_env.Building, 2025, 1), 100_000m);
        AddCharge(theirs, AddPeriod(other, 2025, 2), 50_000m);
        _env.Access.Buildings.Add(other.Id);   // el administrador de empresa tiene los dos edificios
        var declared = await Declare(owner, 150_000m, mine, theirs);
        await Review(declared.Id, 150_000m);
        await Approve(declared.Id);            // con ambos edificios si puede (control)
        Assert.Equal(2, PaymentsOf(owner).Count);

        // Segundo pago: ahora el personal solo tiene un edificio asignado.
        AddCharge(mine, AddPeriod(_env.Building, 2025, 3), 10_000m);
        AddCharge(theirs, AddPeriod(other, 2025, 3), 20_000m);
        var declared2 = await Declare(owner, 30_000m, mine, theirs);
        await Review(declared2.Id, 30_000m);
        AsStaff("BuildingManager");
        _env.Access.Buildings.Remove(other.Id);
        var result = await Api().Approve(declared2.Id, default);

        Assert.Contains("edificios que no tenés asignados", Text(result, 403));
        Assert.Equal(OwnerPaymentStatus.UnderReview, Fresh().OwnerPayments.Single(x => x.Id == declared2.Id).Status);
        Assert.Equal(2, PaymentsOf(owner).Count);   // los dos del primer pago, ninguno nuevo
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Reversa de un pago registrado por el sistema
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevertirUnPagoRegistradoReabreLaDeudaYLoDejaRechazadoConSuMotivo()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var registered = await Register(owner, 100_000m);

        AsStaff();
        var reversed = await Api().Reverse(registered.Id, new OwnerPaymentReverseRequest { Reason = "Cheque rebotado" }, default);

        var dto = FinanceEnv.Ok(reversed);
        Assert.Equal("Rejected", dto.Status);
        Assert.NotNull(dto.ReversedAt);
        Assert.True(Assert.Single(PaymentsOf(owner)).IsReversed);
        Assert.Single((await Preview(owner)).Comprobantes);
    }

    [Fact]
    public async Task UnPagoDeLaAppNoSePuedeRevertirPorEsteCamino()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);
        AddCharge(unit, AddPeriod(_env.Building, 2025, 1), 100_000m);
        var declared = await Declare(owner, 100_000m, unit);
        await Review(declared.Id, 100_000m);
        await Approve(declared.Id);

        AsStaff();
        var result = await Api().Reverse(declared.Id, new OwnerPaymentReverseRequest { Reason = "Error" }, default);

        Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.NotEqual(200, ((ObjectResult)result.Result!).StatusCode);
        Assert.False(Assert.Single(PaymentsOf(owner)).IsReversed);
    }
}
