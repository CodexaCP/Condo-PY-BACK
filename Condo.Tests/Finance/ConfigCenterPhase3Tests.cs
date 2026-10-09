using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Services;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Finance;

/// <summary>
/// Centro de configuracion, fase 3: politica de mora completa (gracia, tope, minimo, cargos que entran en la base, exoneracion por unidad),
/// correccion de los pagos revertidos que seguian contando como pagados, politica de fondos, umbral del presupuesto y avisos automaticos.
/// </summary>
public class ConfigCenterPhase3Tests : IDisposable
{
    private readonly FinanceEnv _env = new();

    // La mora se prueba con fechas fijas: el periodo vencio el 10/03/2026 y "hoy" es 24/03/2026 (dos intervalos semanales).
    private static readonly DateOnly Due = new(2026, 3, 10);
    private static readonly DateOnly TwoWeeksLater = new(2026, 3, 24);

    public ConfigCenterPhase3Tests()
    {
        _env.Tenant.CompanyId = _env.Company.Id;
        _env.Tenant.UserId = _env.Admin.Id;     // quien registra pagos y cambios tiene que existir (llaves foraneas)
        _env.Access.Buildings.Add(_env.Building.Id);
    }

    public void Dispose() => _env.Dispose();

    // ── Armado ───────────────────────────────────────────────────────────────

    private Building B => _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id);

    private void SetLateFee(decimal rate = 2m, LateFeeFrequency frequency = LateFeeFrequency.Weekly, Action<Building>? tweak = null)
    {
        var b = B;
        b.LateFeeRatePercentage = rate;
        b.LateFeeFrequency = frequency;
        tweak?.Invoke(b);
        _env.T.Db.SaveChanges();
    }

    private ExpensePeriod AddPublished(DateOnly due, DateOnly? lateFeeDate = null, int year = 2026, int month = 3)
    {
        var period = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Year = year, Month = month, Name = $"{month:00}/{year}",
            StartDate = new DateOnly(year, month, 1), EndDate = new DateOnly(year, month, 28), DueDate = due, LateFeeDate = lateFeeDate,
            Status = ExpensePeriodStatus.Published
        };
        _env.T.Db.ExpensePeriods.Add(period);
        _env.T.Db.SaveChanges();
        return period;
    }

    private Unit AddUnitWithCharge(ExpensePeriod period, decimal amount, ExpenseChargeType type = ExpenseChargeType.Ordinary)
    {
        var unit = _env.T.AddUnit(_env.Building);
        AddCharge(period, unit, amount, type);
        return unit;
    }

    private ExpenseCharge AddCharge(ExpensePeriod period, Unit unit, decimal amount, ExpenseChargeType type = ExpenseChargeType.Ordinary)
    {
        var charge = new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, ChargeType = type, Concept = type.ToString(), Amount = amount
        };
        _env.T.Db.ExpenseCharges.Add(charge);
        _env.T.Db.SaveChanges();
        return charge;
    }

    private Payment AddPayment(ExpensePeriod period, Unit unit, decimal amount, bool reversed = false)
    {
        var payment = new Payment
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, PaymentDate = Due, Amount = amount,
            Method = PaymentMethod.Cash, Reference = $"P-{Guid.NewGuid():N}"[..12], IsReversed = reversed
        };
        _env.T.Db.Payments.Add(payment);
        _env.T.Db.SaveChanges();
        return payment;
    }

    private async Task<LateFeeRunResult> RunLateFee(DateOnly? today = null) =>
        await new LateFeeAccrualRunner(_env.T.NewContext()).RunAsync(today ?? TwoWeeksLater, default);

    private List<ExpenseCharge> LateFees(Unit unit) =>
        _env.T.NewContext().ExpenseCharges.Where(x => x.UnitId == unit.Id && x.IsLateFee).OrderBy(x => x.AutoLateFeeIntervalIndex).ToList();

    private ApplicationUser AddOwner(Unit unit)
    {
        var owner = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.AddOwner(unit, owner);
        return owner;
    }

    private BuildingConfigPoliciesController Policies() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));

    private BuildingConfigController Api() =>
        new(_env.T.Db, _env.Access, _env.Tenant, new BuildingConfigOverviewService(_env.T.Db, new FinanceModuleGate(_env.T.Db)));

    private UnitLateFeeExemptionController Exemption() => new(_env.T.Db, _env.Access, _env.Tenant);

    private ExpensePeriodsController Periods() => new(
        _env.T.Db, _env.Access, _env.Tenant, new ExpenseSettlementDistributionService(_env.T.Db), new StubWebHostEnvironment(Path.GetTempPath()),
        new OwnerCreditService(_env.T.Db), new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance),
        NullLogger<ExpensePeriodsController>.Instance);

    private static void AssertStatus<T>(ActionResult<T> result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);

    // ═════════════════════════════════════════════════════════════════════════
    // Mora automatica
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SinPoliticaLaMoraEsLaDeSiempre_UnRecargoPorIntervaloSobreLaBase()
    {
        SetLateFee(2m, LateFeeFrequency.Weekly);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);

        var result = await RunLateFee();

        Assert.Equal(2, result.ChargesCreated);
        var fees = LateFees(unit);
        Assert.Equal([1, 2], fees.Select(f => f.AutoLateFeeIntervalIndex!.Value).ToArray());
        Assert.All(fees, f => Assert.Equal(20_000m, f.Amount));                 // 2 % de 1.000.000
        Assert.All(fees, f => Assert.Equal(ExpenseChargeType.Adjustment, f.ChargeType));
    }

    [Fact]
    public async Task CorrerDosVecesNoDuplicaLosRecargos()
    {
        SetLateFee();
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);

        await RunLateFee();
        var second = await RunLateFee();

        Assert.Equal(0, second.ChargesCreated);
        Assert.Equal(2, LateFees(unit).Count);
    }

    [Fact]
    public async Task UnaUnidadAlDiaNoAcumulaMora_PeroSiSuPagoSeRevierte()
    {
        SetLateFee();
        var period = AddPublished(Due);
        var paidUnit = AddUnitWithCharge(period, 1_000_000m);
        var reversedUnit = AddUnitWithCharge(period, 1_000_000m);
        AddPayment(period, paidUnit, 1_000_000m);
        AddPayment(period, reversedUnit, 1_000_000m, reversed: true);   // se pago y se revirtio: la deuda vuelve a estar abierta

        await RunLateFee();

        Assert.Empty(LateFees(paidUnit));
        Assert.Equal(2, LateFees(reversedUnit).Count);
    }

    [Fact]
    public async Task LaGraciaEnLaFechaDeCorteRetrasaLaMora()
    {
        SetLateFee();
        var period = AddPublished(Due, lateFeeDate: Due.AddDays(7));   // corte una semana despues del vencimiento
        var unit = AddUnitWithCharge(period, 1_000_000m);

        await RunLateFee();

        Assert.Single(LateFees(unit));       // dos semanas desde el vencimiento = un solo intervalo desde el corte
    }

    [Fact]
    public async Task LaMoraMinimaSeAplicaCuandoElInteresDaMenos()
    {
        SetLateFee(0.5m, LateFeeFrequency.Weekly, b => b.LateFeeMinAmount = 20_000m);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);            // 0,5 % = 5.000, menos que el minimo

        await RunLateFee();

        var fees = LateFees(unit);
        Assert.Equal(2, fees.Count);
        Assert.All(fees, f => Assert.Equal(20_000m, f.Amount));
        Assert.Contains("mínima", fees[0].Notes);
    }

    [Fact]
    public async Task LaMoraMinimaNoBajaUnInteresMayor()
    {
        SetLateFee(2m, LateFeeFrequency.Weekly, b => b.LateFeeMinAmount = 5_000m);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);

        await RunLateFee();

        Assert.All(LateFees(unit), f => Assert.Equal(20_000m, f.Amount));
    }

    [Fact]
    public async Task ElTopeCortaLaMoraAcumulada()
    {
        // Cada semana 2 % = 20.000; el tope es 3 % de la base = 30.000: el primer intervalo cobra 20.000 y el segundo solo los 10.000 que faltan.
        SetLateFee(2m, LateFeeFrequency.Weekly, b => b.LateFeeCapPercentage = 3m);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);

        await RunLateFee();

        var fees = LateFees(unit);
        Assert.Equal([20_000m, 10_000m], fees.Select(f => f.Amount).ToArray());
        Assert.Contains("tope", fees[1].Notes);

        // Pasan mas semanas: ya no se cobra nada mas.
        var later = await RunLateFee(TwoWeeksLater.AddDays(21));
        Assert.Equal(0, later.ChargesCreated);
        Assert.Equal(30_000m, LateFees(unit).Sum(f => f.Amount));
    }

    [Fact]
    public async Task LaMoraManualTambienCuentaParaElTope()
    {
        SetLateFee(2m, LateFeeFrequency.Weekly, b => b.LateFeeCapPercentage = 3m);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);
        _env.T.Db.ExpenseCharges.Add(new ExpenseCharge
        {
            CompanyId = _env.Company.Id, ExpensePeriodId = period.Id, UnitId = unit.Id, ChargeType = ExpenseChargeType.Adjustment,
            IsLateFee = true, Concept = "Recargo manual", Amount = 25_000m
        });
        _env.T.Db.SaveChanges();

        await RunLateFee();

        Assert.Equal(30_000m, LateFees(unit).Sum(f => f.Amount));       // 25.000 manual + 5.000 que entran en el tope
    }

    [Fact]
    public async Task LosCargosQueElEdificioExcluyeNoEntranEnLaBase()
    {
        SetLateFee(2m, LateFeeFrequency.Weekly, b =>
        {
            b.LateFeeAppliesToReserve = false;
            b.LateFeeAppliesToExtraordinary = false;
            b.LateFeeAppliesToIndividual = false;
        });
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 800_000m);
        AddCharge(period, unit, 100_000m, ExpenseChargeType.ReserveFund);
        AddCharge(period, unit, 50_000m, ExpenseChargeType.Extraordinary);
        AddCharge(period, unit, 50_000m, ExpenseChargeType.Individual);

        await RunLateFee();

        Assert.All(LateFees(unit), f => Assert.Equal(16_000m, f.Amount));     // 2 % de 800.000 (solo la expensa ordinaria)
    }

    [Fact]
    public async Task LosCargosIncluidosSiEntranEnLaBase()
    {
        SetLateFee();
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 800_000m);
        AddCharge(period, unit, 100_000m, ExpenseChargeType.ReserveFund);
        AddCharge(period, unit, 100_000m, ExpenseChargeType.Extraordinary);

        await RunLateFee();

        Assert.All(LateFees(unit), f => Assert.Equal(20_000m, f.Amount));     // 2 % de 1.000.000
    }

    [Fact]
    public async Task UnaUnidadExoneradaNoAcumulaMora_PeroLasDemasSi()
    {
        SetLateFee();
        var period = AddPublished(Due);
        var exempt = AddUnitWithCharge(period, 1_000_000m);
        var normal = AddUnitWithCharge(period, 1_000_000m);
        _env.T.Db.Units.Single(x => x.Id == exempt.Id).LateFeeExempt = true;
        _env.T.Db.SaveChanges();

        await RunLateFee();

        Assert.Empty(LateFees(exempt));
        Assert.Equal(2, LateFees(normal).Count);
    }

    [Fact]
    public async Task UnPeriodoEnBorradorNoGeneraMora()
    {
        SetLateFee();
        var period = AddPublished(Due);
        period.Status = ExpensePeriodStatus.Draft;
        _env.T.Db.SaveChanges();
        var unit = AddUnitWithCharge(period, 1_000_000m);

        Assert.Equal(0, (await RunLateFee()).ChargesCreated);
        Assert.Empty(LateFees(unit));
    }

    // ── Aviso de mora aplicada ───────────────────────────────────────────────

    private void SetRule(NoticeKind kind, bool active, int? offset = null)
    {
        _env.T.Db.BuildingNoticeRules.Add(new BuildingNoticeRule
        {
            CompanyId = _env.Company.Id, BuildingId = _env.Building.Id, Kind = kind, IsActive = active, OffsetDays = offset
        });
        _env.T.Db.SaveChanges();
    }

    private List<Notification> NoticesOf(NotificationType type) =>
        _env.T.NewContext().Notifications.Where(x => x.Type == type).ToList();

    [Fact]
    public async Task ElAvisoDeMoraAplicadaVieneApagadoPorDefecto()
    {
        SetLateFee();
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);
        AddOwner(unit);

        await RunLateFee();

        Assert.Empty(NoticesOf(NotificationType.LateFeeApplied));
    }

    [Fact]
    public async Task ConElAvisoEncendidoSeAvisaUnaSolaVezAlPropietarioYAlResidente()
    {
        SetLateFee();
        SetRule(NoticeKind.LateFeeApplied, true);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);
        var owner = AddOwner(unit);
        var tenantUser = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.AddResident(unit, tenantUser);

        var first = await RunLateFee();
        var second = await RunLateFee(TwoWeeksLater.AddDays(7));      // un intervalo mas: nueva mora, pero el aviso no se repite

        Assert.Equal(2, first.UnitsNotified);
        var notices = NoticesOf(NotificationType.LateFeeApplied);
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, n => n.RecipientId == owner.Id);
        Assert.Contains(notices, n => n.RecipientId == tenantUser.Id);
        Assert.All(notices, n => Assert.Equal(period.Id, n.EntityId));
        Assert.Equal(0, second.UnitsNotified);
    }

    [Fact]
    public async Task ElAvisoDeMoraNoLlegaAUnPropietarioQueYaNoLoEs()
    {
        SetLateFee();
        SetRule(NoticeKind.LateFeeApplied, true);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);
        var former = _env.T.AddUser(_env.Company, UserRole.Owner);
        var link = _env.T.AddOwner(unit, former);
        link.EndDate = new DateOnly(2026, 1, 1);
        _env.T.Db.SaveChanges();

        await RunLateFee();

        Assert.Empty(NoticesOf(NotificationType.LateFeeApplied));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Correccion: los pagos revertidos ya no cuentan como pagados
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ElAvisoDeDeudaParaBloquearReservasNoCuentaLosPagosRevertidos()
    {
        var period = AddPublished(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5), year: 2026, month: 2);
        var paid = AddUnitWithCharge(period, 500_000m);
        var reversed = AddUnitWithCharge(period, 500_000m);
        AddPayment(period, paid, 500_000m);
        AddPayment(period, reversed, 500_000m, reversed: true);

        var service = new UnitOverdueService(_env.T.Db);

        Assert.False(await service.IsUnitOverdueAsync(paid.Id, default));
        Assert.True(await service.IsUnitOverdueAsync(reversed.Id, default));
    }

    [Fact]
    public async Task ElReporteDeCobranzaNoSumaLosPagosRevertidos()
    {
        var period = AddPublished(Due);
        var unitA = AddUnitWithCharge(period, 400_000m);
        var unitB = AddUnitWithCharge(period, 600_000m);
        AddPayment(period, unitA, 400_000m);
        AddPayment(period, unitB, 600_000m, reversed: true);

        var report = FinanceEnv.Ok(await new CollectionsController(_env.T.Db, _env.Access).GetReport(_env.Building.Id, 2026, 1, 12, default));

        Assert.Equal(1_000_000m, report.Summary.TotalChargedAmount);
        Assert.Equal(400_000m, report.Summary.TotalCollectedAmount);          // el pago revertido no cuenta
    }

    [Fact]
    public async Task ElRecargoManualDescuentaSoloLosPagosVigentes()
    {
        _env.LoginAs("CompanyAdmin");
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 1_000_000m);
        AddPayment(period, unit, 1_000_000m, reversed: true);

        var result = FinanceEnv.Ok(await Periods().ApplyLateFees(
            period.Id, new ApplyLateFeesRequest { RatePercentage = 5m, ReferenceDate = TwoWeeksLater, Concept = "Recargo" }, default));

        Assert.Equal(1, result.ChargesCreated);
        Assert.Equal(50_000m, result.TotalLateFeeAmount);                      // 5 % de la deuda completa: el pago revertido no la redujo
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Gracia: la fecha de corte de los periodos nuevos
    // ═════════════════════════════════════════════════════════════════════════

    private ExpensePeriodUpsertRequest PeriodReq(int month, DateOnly? lateFeeDate = null) => new()
    {
        BuildingId = _env.Building.Id, Year = 2027, Month = month, Name = $"Mes {month}",
        StartDate = new DateOnly(2027, month, 1), EndDate = new DateOnly(2027, month, 28), DueDate = new DateOnly(2027, month, 28), LateFeeDate = lateFeeDate
    };

    private static ExpensePeriodDto Created(ActionResult<ExpensePeriodDto> result) =>
        (ExpensePeriodDto)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    [Fact]
    public async Task ElPeriodoNuevoSinFechaDeCorteTomaVencimientoMasGracia()
    {
        B.GraceDays = 5;
        _env.T.Db.SaveChanges();

        var period = Created(await Periods().Create(PeriodReq(1), default));

        Assert.Equal(new DateOnly(2027, 2, 2), period.LateFeeDate);              // 28/01 + 5 dias
    }

    [Fact]
    public async Task LaFechaDeCorteIndicadaSeRespetaYSinGraciaQuedaVacia()
    {
        B.GraceDays = 5;
        _env.T.Db.SaveChanges();

        var explicitDate = Created(await Periods().Create(PeriodReq(2, new DateOnly(2027, 3, 15)), default));
        Assert.Equal(new DateOnly(2027, 3, 15), explicitDate.LateFeeDate);

        B.GraceDays = null;
        _env.T.Db.SaveChanges();
        var none = Created(await Periods().Create(PeriodReq(3), default));
        Assert.Null(none.LateFeeDate);

        B.GraceDays = 0;
        _env.T.Db.SaveChanges();
        Assert.Null(Created(await Periods().Create(PeriodReq(4), default)).LateFeeDate);
    }

    [Fact]
    public async Task ClonarUnPeriodoSinFechaDeCorteAplicaLaGracia()
    {
        var source = AddPublished(Due.AddMonths(-1), year: 2026, month: 1);          // sin fecha de corte
        B.GraceDays = 3;
        _env.T.Db.SaveChanges();

        // El clon copia gastos e ingresos y calcula el saldo de arrastre con sumas que SQLite no soporta: se prueba la fecha con la regla directa.
        Assert.Equal(source.DueDate.AddMonths(1).AddDays(3), ExpensePeriodsController.ResolveLateFeeDate(null, source.DueDate.AddMonths(1), 3));
        Assert.Equal(new DateOnly(2026, 5, 5), ExpensePeriodsController.ResolveLateFeeDate(new DateOnly(2026, 5, 5), new DateOnly(2026, 4, 1), 3));
        Assert.Null(ExpensePeriodsController.ResolveLateFeeDate(null, new DateOnly(2026, 4, 1), null));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ElAltaEnLoteAplicaLaGraciaDeCadaEdificio()
    {
        B.GraceDays = 7;
        _env.T.Db.SaveChanges();
        var request = new BulkCreateExpensePeriodsRequest
        {
            Year = 2027, Month = 5, StartDate = new DateOnly(2027, 5, 1), EndDate = new DateOnly(2027, 5, 31), DueDate = new DateOnly(2027, 6, 10),
            BuildingIds = [_env.Building.Id]
        };

        FinanceEnv.Ok(await Periods().BulkCreate(request, default));

        var period = _env.T.NewContext().ExpensePeriods.Single(x => x.BuildingId == _env.Building.Id && x.Year == 2027 && x.Month == 5);
        Assert.Equal(new DateOnly(2027, 6, 17), period.LateFeeDate);
    }

    [Fact]
    public async Task EditarUnPeriodoNoVuelveAAplicarLaGracia()
    {
        B.GraceDays = 5;
        _env.T.Db.SaveChanges();
        var created = Created(await Periods().Create(PeriodReq(6), default));

        // Al editar se respeta lo que mande el formulario: vacio queda vacio.
        var edit = PeriodReq(6);
        var updated = FinanceEnv.Ok(await Periods().Update(created.Id, edit, default));

        Assert.Null(updated.LateFeeDate);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Politica de mora (API del Centro)
    // ═════════════════════════════════════════════════════════════════════════

    private static UpdateLateFeePolicyRequest PolicyReq(decimal? rate = 2m, LateFeeFrequency? frequency = LateFeeFrequency.Weekly) => new()
    {
        RatePercentage = rate, Frequency = frequency, GraceDays = 3, CapPercentage = 50m, MinAmount = 10_000m,
        AppliesToReserve = false, AppliesToExtraordinary = true, AppliesToIndividual = true
    };

    [Fact]
    public async Task LaPoliticaDeUnEdificioSinConfigurarMuestraLosValoresDeSiempre()
    {
        var policy = FinanceEnv.Ok(await Policies().GetLateFee(_env.Building.Id, default));

        Assert.Null(policy.RatePercentage);
        Assert.Null(policy.CapPercentage);
        Assert.True(policy.AppliesToReserve && policy.AppliesToExtraordinary && policy.AppliesToIndividual);
        Assert.False(policy.Confirmed);
        Assert.True(policy.CanEdit);
    }

    [Fact]
    public async Task GuardarLaPoliticaDeMoraLaDejaConfirmadaYRegistradaEnElHistorial()
    {
        var policy = FinanceEnv.Ok(await Policies().UpdateLateFee(_env.Building.Id, PolicyReq(), default));

        Assert.Equal(2m, policy.RatePercentage);
        Assert.Equal(LateFeeFrequency.Weekly, policy.Frequency);
        Assert.Equal(3, policy.GraceDays);
        Assert.Equal(50m, policy.CapPercentage);
        Assert.Equal(10_000m, policy.MinAmount);
        Assert.False(policy.AppliesToReserve);
        Assert.True(policy.Confirmed);

        var saved = _env.T.NewContext().Buildings.Single(x => x.Id == _env.Building.Id);
        Assert.Equal(2m, saved.LateFeeRatePercentage);
        Assert.True(saved.LateFeePolicyConfirmed);

        var entry = Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.LateFee).ToList());
        Assert.Equal("Updated", entry.Action);
        Assert.Contains("lateFeeCapPercentage", entry.ChangesJson);
    }

    [Fact]
    public async Task ElCambioDeTasaAvisaAPropietariosYAdministradores()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var owner = AddOwner(unit);

        FinanceEnv.Ok(await Policies().UpdateLateFee(_env.Building.Id, PolicyReq(), default));

        var notices = NoticesOf(NotificationType.LateFeeConfigChanged);
        Assert.Contains(notices, n => n.RecipientId == owner.Id);
        Assert.Contains(notices, n => n.RecipientId == _env.Admin.Id);
    }

    [Fact]
    public async Task GuardarSinCambiarLaTasaNoVuelveAAvisar()
    {
        var unit = _env.T.AddUnit(_env.Building);
        AddOwner(unit);
        FinanceEnv.Ok(await Policies().UpdateLateFee(_env.Building.Id, PolicyReq(), default));
        var before = NoticesOf(NotificationType.LateFeeConfigChanged).Count;

        var again = PolicyReq();
        again.CapPercentage = 60m;                      // otro campo, la misma tasa
        FinanceEnv.Ok(await Policies().UpdateLateFee(_env.Building.Id, again, default));

        Assert.Equal(before, NoticesOf(NotificationType.LateFeeConfigChanged).Count);
    }

    [Fact]
    public async Task SinTasaQuedaConfirmadoQueElEdificioNoCobraMora()
    {
        SetLateFee(3m, LateFeeFrequency.Daily);

        var policy = FinanceEnv.Ok(await Policies().UpdateLateFee(_env.Building.Id, new UpdateLateFeePolicyRequest { RatePercentage = null, Frequency = LateFeeFrequency.Daily }, default));

        Assert.Null(policy.RatePercentage);
        Assert.Null(policy.Frequency);                         // sin tasa no hay frecuencia
        Assert.True(policy.Confirmed);
        var section = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.LateFee);
        Assert.Equal(ConfigSectionStatus.Complete, section.Status);
        Assert.Contains(section.Summary, i => i.Value == "No cobra mora");
    }

    [Theory]
    [InlineData(101, LateFeeFrequency.Weekly, null, null, null)]
    [InlineData(-1, LateFeeFrequency.Weekly, null, null, null)]
    [InlineData(2, null, null, null, null)]
    [InlineData(2, LateFeeFrequency.Weekly, 366, null, null)]
    [InlineData(2, LateFeeFrequency.Weekly, null, 1001, null)]
    [InlineData(2, LateFeeFrequency.Weekly, null, null, -5)]
    public async Task LaPoliticaDeMoraValidaSusCampos(int rate, LateFeeFrequency? frequency, int? grace, int? cap, int? min)
    {
        var request = new UpdateLateFeePolicyRequest { RatePercentage = rate, Frequency = frequency, GraceDays = grace, CapPercentage = cap, MinAmount = min };

        AssertStatus(await Policies().UpdateLateFee(_env.Building.Id, request, default), 400);

        Assert.False(_env.T.NewContext().Buildings.Single(x => x.Id == _env.Building.Id).LateFeePolicyConfirmed);
    }

    [Fact]
    public async Task LaPoliticaDeMoraSoloLaEditanSuperAdminYAdministrador()
    {
        foreach (var role in new[] { "CompanyOperator", "BuildingManager", "Owner" })
        {
            _env.LoginAs(role);
            AssertStatus(await Policies().UpdateLateFee(_env.Building.Id, PolicyReq(), default), 403);
        }

        _env.LoginAs("BuildingManager");
        Assert.NotNull(FinanceEnv.Ok(await Policies().GetLateFee(_env.Building.Id, default)));          // el encargado la ve
        Assert.False(FinanceEnv.Ok(await Policies().GetLateFee(_env.Building.Id, default)).CanEdit);

        _env.LoginAs("CompanyAdmin");
        FinanceEnv.Ok(await Policies().UpdateLateFee(_env.Building.Id, PolicyReq(), default));
    }

    [Fact]
    public async Task LaPoliticaDeUnEdificioAjenoResponde404()
    {
        _env.LoginAs("CompanyAdmin");
        _env.Access.Buildings.Clear();

        Assert.IsType<NotFoundResult>((await Policies().GetLateFee(_env.Building.Id, default)).Result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Exoneracion de mora por unidad
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExonerarUnaUnidadExigeMotivoYQuedaRegistrado()
    {
        var unit = _env.T.AddUnit(_env.Building);

        AssertStatus(await Exemption().Set(unit.Id, new UnitLateFeeExemptionRequest { Exempt = true, Reason = "  " }, default), 400);
        AssertStatus(await Exemption().Set(unit.Id, new UnitLateFeeExemptionRequest { Exempt = true, Reason = new string('x', 301) }, default), 400);

        var dto = FinanceEnv.Ok(await Exemption().Set(unit.Id, new UnitLateFeeExemptionRequest { Exempt = true, Reason = "Acuerdo de pago con la comisión" }, default));

        Assert.True(dto.LateFeeExempt);
        Assert.Equal("Acuerdo de pago con la comisión", dto.LateFeeExemptReason);
        var saved = _env.T.NewContext().Units.Single(x => x.Id == unit.Id);
        Assert.Equal(_env.Tenant.UserId, saved.LateFeeExemptByUserId);
        Assert.NotNull(saved.LateFeeExemptAtUtc);
        var entry = Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Action == "Exempted").ToList());
        Assert.Equal(unit.Id, entry.EntityId);
        Assert.Contains("Acuerdo de pago", entry.Summary);
    }

    [Fact]
    public async Task QuitarLaExoneracionLimpiaLosDatosYSeRegistra()
    {
        var unit = _env.T.AddUnit(_env.Building);
        FinanceEnv.Ok(await Exemption().Set(unit.Id, new UnitLateFeeExemptionRequest { Exempt = true, Reason = "Motivo" }, default));

        var dto = FinanceEnv.Ok(await Exemption().Set(unit.Id, new UnitLateFeeExemptionRequest { Exempt = false }, default));

        Assert.False(dto.LateFeeExempt);
        Assert.Null(dto.LateFeeExemptReason);
        var saved = _env.T.NewContext().Units.Single(x => x.Id == unit.Id);
        Assert.Null(saved.LateFeeExemptByUserId);
        Assert.Null(saved.LateFeeExemptAtUtc);
        Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Action == "ExemptionRemoved").ToList());
    }

    [Fact]
    public async Task RepetirLaMismaExoneracionNoDejaOtraEntrada()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var request = new UnitLateFeeExemptionRequest { Exempt = true, Reason = "Motivo" };
        FinanceEnv.Ok(await Exemption().Set(unit.Id, request, default));

        FinanceEnv.Ok(await Exemption().Set(unit.Id, request, default));

        Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.LateFee).ToList());
    }

    [Fact]
    public async Task SoloSuperAdminYAdministradorExoneran_YSoloDeSusEdificios()
    {
        var unit = _env.T.AddUnit(_env.Building);
        var request = new UnitLateFeeExemptionRequest { Exempt = true, Reason = "Motivo" };

        foreach (var role in new[] { "CompanyOperator", "BuildingManager", "Owner" })
        {
            _env.LoginAs(role);
            AssertStatus(await Exemption().Set(unit.Id, request, default), 403);
        }

        _env.LoginAs("CompanyAdmin");
        _env.Access.Buildings.Clear();
        Assert.IsType<NotFoundResult>((await Exemption().Set(unit.Id, request, default)).Result);
        Assert.IsType<NotFoundResult>((await Exemption().Set(Guid.NewGuid(), request, default)).Result);
    }

    [Fact]
    public async Task LaFichaDeLaUnidadMuestraLaExoneracion()
    {
        var unit = _env.T.AddUnit(_env.Building);
        FinanceEnv.Ok(await Exemption().Set(unit.Id, new UnitLateFeeExemptionRequest { Exempt = true, Reason = "Motivo" }, default));

        var dto = FinanceEnv.Ok(await new UnitsController(_env.T.Db, _env.Access).GetById(unit.Id, default));

        Assert.True(dto.LateFeeExempt);
        Assert.Equal("Motivo", dto.LateFeeExemptReason);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Fondos
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LaPoliticaDeFondosArrancaConLosValoresDeSiempre()
    {
        var policy = FinanceEnv.Ok(await Policies().GetFundPolicy(_env.Building.Id, default));

        Assert.Equal(ReserveUsePolicy.FreeUse, policy.ReserveUsePolicy);
        Assert.Equal(IncomeTreatment.CreditToOwners, policy.IncomeTreatment);
        Assert.False(policy.Confirmed);
    }

    [Fact]
    public async Task GuardarLosFondosLosConfirmaYLosRegistra()
    {
        var policy = FinanceEnv.Ok(await Policies().UpdateFundPolicy(_env.Building.Id, new UpdateFundPolicyRequest
        {
            IncomeTreatment = IncomeTreatment.ToReserveFund, ReserveFundPercentage = 5m, ExtraordinaryPercentage = 2m,
            ReserveUsePolicy = ReserveUsePolicy.RequiresAssemblyApproval, ReserveUseThreshold = 5_000_000m
        }, default));

        Assert.Equal(5m, policy.ReserveFundPercentage);
        Assert.Equal(ReserveUsePolicy.RequiresAssemblyApproval, policy.ReserveUsePolicy);
        Assert.True(policy.Confirmed);
        var entry = Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Funds).ToList());
        Assert.Contains("reserveUsePolicy", entry.ChangesJson);
        var section = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Funds);
        Assert.Equal(ConfigSectionStatus.Complete, section.Status);
    }

    [Fact]
    public async Task SinAportesPeroConfirmadoLosFondosQuedanCompletos()
    {
        FinanceEnv.Ok(await Policies().UpdateFundPolicy(_env.Building.Id, new UpdateFundPolicyRequest(), default));

        var section = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Funds);

        Assert.Equal(ConfigSectionStatus.Complete, section.Status);
    }

    [Theory]
    [InlineData(101, null, null)]
    [InlineData(null, -1, null)]
    [InlineData(null, null, -1)]
    public async Task LaPoliticaDeFondosValidaSusCampos(int? reserve, int? extra, int? threshold)
    {
        var request = new UpdateFundPolicyRequest { ReserveFundPercentage = reserve, ExtraordinaryPercentage = extra, ReserveUseThreshold = threshold };

        AssertStatus(await Policies().UpdateFundPolicy(_env.Building.Id, request, default), 400);
    }

    [Fact]
    public async Task LaPoliticaDeFondosSoloLaEditanSuperAdminYAdministrador()
    {
        _env.LoginAs("CompanyOperator");

        AssertStatus(await Policies().UpdateFundPolicy(_env.Building.Id, new UpdateFundPolicyRequest(), default), 403);
        Assert.NotNull(FinanceEnv.Ok(await Policies().GetFundPolicy(_env.Building.Id, default)));
        _env.LoginAs("BuildingManager");
        AssertStatus(await Policies().GetFundPolicy(_env.Building.Id, default), 403);       // el encargado no ve los fondos
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Umbral del semaforo del presupuesto
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ElUmbralPorDefectoEsElDeSiempre_YUnoMayorAmpliaElAmarillo()
    {
        // Gasto 15 % sobre lo presupuestado: con el umbral de siempre (10 %) es rojo; con 25 % es amarillo.
        Assert.Equal(BudgetStatus.Red, FinanceBudgetCalculator.Status(LedgerCategoryType.Expense, 100m, 115m));
        Assert.Equal(BudgetStatus.Amber, FinanceBudgetCalculator.Status(LedgerCategoryType.Expense, 100m, 115m, 0.25m));
        Assert.Equal(BudgetStatus.Amber, FinanceBudgetCalculator.Status(LedgerCategoryType.Expense, 100m, 108m));
        // Ingresos: quedar 20 % por debajo.
        Assert.Equal(BudgetStatus.Red, FinanceBudgetCalculator.Status(LedgerCategoryType.Income, 100m, 80m));
        Assert.Equal(BudgetStatus.Amber, FinanceBudgetCalculator.Status(LedgerCategoryType.Income, 100m, 80m, 0.25m));
    }

    [Fact]
    public async Task ElLibroTomaElUmbralConfiguradoEIgnoraUnoInvalido()
    {
        _env.Setup();
        var settings = _env.T.Db.FinanceSettings.Single(x => x.BuildingId == _env.Building.Id);
        Assert.Equal(10, settings.BudgetWarnPercent);

        settings.BudgetWarnPercent = 30;
        _env.T.Db.SaveChanges();
        Assert.Equal(30, (await new FinanceLedgerService(_env.T.NewContext()).LoadContextAsync(_env.Building.Id, default))!.BudgetWarnPercent);

        settings.BudgetWarnPercent = 0;                       // un valor fuera de rango no apaga el semaforo
        _env.T.Db.SaveChanges();
        Assert.Equal(10, (await new FinanceLedgerService(_env.T.NewContext()).LoadContextAsync(_env.Building.Id, default))!.BudgetWarnPercent);
    }

    [Fact]
    public async Task ElUmbralDelPresupuestoSeEditaYQuedaRegistrado()
    {
        _env.Setup();

        var before = FinanceEnv.Ok(await Policies().GetBudgetAlerts(_env.Building.Id, default));
        Assert.Equal(10, before.WarnPercent);
        Assert.True(before.FinanceAvailable);

        var updated = FinanceEnv.Ok(await Policies().UpdateBudgetAlerts(_env.Building.Id, new UpdateBudgetAlertsRequest { WarnPercent = 25 }, default));

        Assert.Equal(25, updated.WarnPercent);
        Assert.Equal(25, _env.T.NewContext().FinanceSettings.Single(x => x.BuildingId == _env.Building.Id).BudgetWarnPercent);
        var entry = Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Budget).ToList());
        Assert.Contains("25", entry.Summary);

        // Mandar el mismo valor no deja otra entrada.
        FinanceEnv.Ok(await Policies().UpdateBudgetAlerts(_env.Building.Id, new UpdateBudgetAlertsRequest { WarnPercent = 25 }, default));
        Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Budget).ToList());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public async Task ElUmbralDelPresupuestoValidaElRango(int percent)
    {
        _env.Setup();

        AssertStatus(await Policies().UpdateBudgetAlerts(_env.Building.Id, new UpdateBudgetAlertsRequest { WarnPercent = percent }, default), 400);
    }

    [Fact]
    public async Task ElUmbralDelPresupuestoRequiereFinanzas_ConfiguracionInicialYPermiso()
    {
        // Sin configuracion inicial (no hay FinanceSettings).
        AssertStatus(await Policies().UpdateBudgetAlerts(_env.Building.Id, new UpdateBudgetAlertsRequest { WarnPercent = 20 }, default), 409);

        _env.Setup();
        _env.LoginAs("CompanyOperator");
        AssertStatus(await Policies().UpdateBudgetAlerts(_env.Building.Id, new UpdateBudgetAlertsRequest { WarnPercent = 20 }, default), 403);

        _env.LoginAs("SuperAdmin");
        _env.T.Db.Buildings.Single(x => x.Id == _env.Building.Id).FinanceModuleEnabled = false;
        _env.T.Db.SaveChanges();
        AssertStatus(await Policies().UpdateBudgetAlerts(_env.Building.Id, new UpdateBudgetAlertsRequest { WarnPercent = 20 }, default), 403);
        Assert.False(FinanceEnv.Ok(await Policies().GetBudgetAlerts(_env.Building.Id, default)).FinanceAvailable);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Reglas de avisos
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SinReglasRigenLosValoresPorDefecto()
    {
        var rules = FinanceEnv.Ok(await Policies().GetNoticeRules(_env.Building.Id, default)).Rules.ToDictionary(r => r.Kind);

        Assert.Equal(5, rules.Count);
        Assert.False(rules[NoticeKind.BeforeDue].IsActive);
        Assert.Equal(3, rules[NoticeKind.BeforeDue].OffsetDays);
        Assert.False(rules[NoticeKind.OnDue].IsActive);
        Assert.False(rules[NoticeKind.LateFeeApplied].IsActive);
        Assert.True(rules[NoticeKind.PaymentReceived].IsActive);             // los avisos que ya existian siguen encendidos
        Assert.True(rules[NoticeKind.PeriodPublished].IsActive);
        Assert.All(rules.Values, r => Assert.True(r.IsDefault));
    }

    [Fact]
    public async Task GuardarReglasLasPersisteYQuedanRegistradas()
    {
        var result = FinanceEnv.Ok(await Policies().UpdateNoticeRules(_env.Building.Id, new UpdateNoticeRulesRequest
        {
            Rules =
            [
                new() { Kind = NoticeKind.BeforeDue, IsActive = true, OffsetDays = 5 },
                new() { Kind = NoticeKind.OnDue, IsActive = true },
                new() { Kind = NoticeKind.PaymentReceived, IsActive = false }
            ]
        }, default));

        var byKind = result.Rules.ToDictionary(r => r.Kind);
        Assert.True(byKind[NoticeKind.BeforeDue].IsActive);
        Assert.Equal(5, byKind[NoticeKind.BeforeDue].OffsetDays);
        Assert.False(byKind[NoticeKind.BeforeDue].IsDefault);
        Assert.False(byKind[NoticeKind.PaymentReceived].IsActive);
        Assert.True(byKind[NoticeKind.PeriodPublished].IsDefault);               // los que no se mandaron siguen por defecto

        var entry = Assert.Single(_env.T.NewContext().FinanceAuditLogs.Where(x => x.Section == ConfigSectionKeys.Documents).ToList());
        Assert.Contains("Aviso de pago recibido", entry.Summary);
    }

    [Fact]
    public async Task GuardarLasReglasDosVecesActualizaSinDuplicarFilas()
    {
        var request = new UpdateNoticeRulesRequest { Rules = [new() { Kind = NoticeKind.BeforeDue, IsActive = true, OffsetDays = 5 }] };
        FinanceEnv.Ok(await Policies().UpdateNoticeRules(_env.Building.Id, request, default));
        request.Rules[0].OffsetDays = 7;

        FinanceEnv.Ok(await Policies().UpdateNoticeRules(_env.Building.Id, request, default));

        var rows = _env.T.NewContext().BuildingNoticeRules.Where(x => x.BuildingId == _env.Building.Id).ToList();
        Assert.Single(rows);
        Assert.Equal(7, rows[0].OffsetDays);
    }

    [Fact]
    public async Task ApagarElAvisoAntesDelVencimientoConservaSusDias()
    {
        FinanceEnv.Ok(await Policies().UpdateNoticeRules(_env.Building.Id,
            new UpdateNoticeRulesRequest { Rules = [new() { Kind = NoticeKind.BeforeDue, IsActive = true, OffsetDays = 6 }] }, default));

        var result = FinanceEnv.Ok(await Policies().UpdateNoticeRules(_env.Building.Id,
            new UpdateNoticeRulesRequest { Rules = [new() { Kind = NoticeKind.BeforeDue, IsActive = false }] }, default));

        var rule = result.Rules.Single(r => r.Kind == NoticeKind.BeforeDue);
        Assert.False(rule.IsActive);
        Assert.Equal(6, rule.OffsetDays);
    }

    [Fact]
    public async Task LasReglasValidanDiasTiposYDuplicados()
    {
        foreach (var offset in new int?[] { null, 0, 31 })
        {
            AssertStatus(await Policies().UpdateNoticeRules(_env.Building.Id,
                new UpdateNoticeRulesRequest { Rules = [new() { Kind = NoticeKind.BeforeDue, IsActive = true, OffsetDays = offset }] }, default), 400);
        }

        AssertStatus(await Policies().UpdateNoticeRules(_env.Building.Id,
            new UpdateNoticeRulesRequest { Rules = [new() { Kind = (NoticeKind)99, IsActive = true }] }, default), 400);
        AssertStatus(await Policies().UpdateNoticeRules(_env.Building.Id, new UpdateNoticeRulesRequest
        {
            Rules = [new() { Kind = NoticeKind.OnDue, IsActive = true }, new() { Kind = NoticeKind.OnDue, IsActive = false }]
        }, default), 400);
        Assert.Empty(_env.T.NewContext().BuildingNoticeRules.ToList());
    }

    [Fact]
    public async Task LasReglasSoloLasEditanSuperAdminYAdministrador_YElEncargadoNoLasVe()
    {
        _env.LoginAs("CompanyOperator");
        AssertStatus(await Policies().UpdateNoticeRules(_env.Building.Id, new UpdateNoticeRulesRequest(), default), 403);
        Assert.NotNull(FinanceEnv.Ok(await Policies().GetNoticeRules(_env.Building.Id, default)));

        _env.LoginAs("BuildingManager");
        AssertStatus(await Policies().GetNoticeRules(_env.Building.Id, default), 403);
    }

    [Fact]
    public async Task ElAyudanteDeReglasUsaElValorPorDefectoSinFila()
    {
        SetRule(NoticeKind.PaymentReceived, false);
        SetRule(NoticeKind.LateFeeApplied, true);

        Assert.False(await NoticeRules.IsActiveAsync(_env.T.Db, _env.Building.Id, NoticeKind.PaymentReceived, default));
        Assert.True(await NoticeRules.IsActiveAsync(_env.T.Db, _env.Building.Id, NoticeKind.LateFeeApplied, default));
        Assert.True(await NoticeRules.IsActiveAsync(_env.T.Db, _env.Building.Id, NoticeKind.PeriodPublished, default));     // sin fila: encendido
        Assert.False(await NoticeRules.IsActiveAsync(_env.T.Db, _env.Building.Id, NoticeKind.OnDue, default));            // sin fila: apagado

        var other = _env.T.AddBuilding(_env.Company, "Otro");
        var active = await NoticeRules.BuildingsWithActiveAsync(_env.T.Db, [_env.Building.Id, other.Id], NoticeKind.PaymentReceived, default);
        Assert.Equal([other.Id], active.ToArray());
    }

    // ── El aviso "pago recibido" se puede apagar ─────────────────────────────

    private OwnerPaymentsController OwnerPayments()
    {
        var credits = new OwnerCreditService(_env.T.Db);
        return new OwnerPaymentsController(
            _env.T.Db, _env.Tenant, _env.Access, credits, new ComprobanteService(_env.T.Db, credits), new InvoiceDraftService(_env.T.Db, _env.Access),
            new PushDispatcher(_env.T.Db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));
    }

    private async Task<(ApplicationUser Owner, OwnerPaymentRegisterRequest Request)> OwnerReadyToPay()
    {
        var period = AddPublished(Due, year: 2026, month: 3);
        var unit = AddUnitWithCharge(period, 100_000m);
        var owner = AddOwner(unit);
        await Task.CompletedTask;
        return (owner, new OwnerPaymentRegisterRequest { OwnerId = owner.Id, PaymentDate = Due, Amount = 100_000m });
    }

    [Fact]
    public async Task RegistrarUnPagoAvisaAlPropietarioPorDefecto()
    {
        var (owner, request) = await OwnerReadyToPay();

        FinanceEnv.Ok(await OwnerPayments().Register(request, default));

        Assert.Contains(NoticesOf(NotificationType.PaymentApproved), n => n.RecipientId == owner.Id);
    }

    [Fact]
    public async Task ConElAvisoDePagoRecibidoApagadoElPagoSeRegistraSinAvisar()
    {
        SetRule(NoticeKind.PaymentReceived, false);
        var (owner, request) = await OwnerReadyToPay();

        var result = FinanceEnv.Ok(await OwnerPayments().Register(request, default));

        Assert.Equal(nameof(OwnerPaymentStatus.Approved), result.Status.ToString());                    // el pago se registra igual
        Assert.Empty(NoticesOf(NotificationType.PaymentApproved));
        Assert.Single(_env.T.NewContext().Payments.Where(x => x.UnitId != Guid.Empty && !x.IsReversed).ToList());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Avisos de vencimiento
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<PaymentReminderResult> Remind(DateOnly today, int hour = 9) =>
        await new PaymentReminderRunner(_env.T.NewContext()).RunAsync(today, hour, default);

    [Fact]
    public async Task ElAvisoAntesDelVencimientoLlegaALosQueDebenYNoALosQueYaPagaron()
    {
        SetRule(NoticeKind.BeforeDue, true, 3);
        var period = AddPublished(Due);
        var owing = AddUnitWithCharge(period, 100_000m);
        var paid = AddUnitWithCharge(period, 100_000m);
        var reversed = AddUnitWithCharge(period, 100_000m);
        var owingOwner = AddOwner(owing);
        var paidOwner = AddOwner(paid);
        var reversedOwner = AddOwner(reversed);
        AddPayment(period, paid, 100_000m);
        AddPayment(period, reversed, 100_000m, reversed: true);                 // un pago revertido no cuenta como pagado

        var result = await Remind(Due.AddDays(-3));

        Assert.Equal(2, result.Notifications);
        var notices = NoticesOf(NotificationType.PaymentDueSoon);
        Assert.Contains(notices, n => n.RecipientId == owingOwner.Id);
        Assert.Contains(notices, n => n.RecipientId == reversedOwner.Id);
        Assert.DoesNotContain(notices, n => n.RecipientId == paidOwner.Id);
        Assert.All(notices, n => Assert.Equal(period.Id, n.EntityId));
        Assert.Contains("en 3 días", notices[0].Body);
    }

    [Fact]
    public async Task ElAvisoSeEnviaUnaSolaVezPorPersonaYPeriodo()
    {
        SetRule(NoticeKind.BeforeDue, true, 3);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 100_000m);
        AddOwner(unit);

        await Remind(Due.AddDays(-3));
        var again = await Remind(Due.AddDays(-3), hour: 15);

        Assert.Equal(0, again.Notifications);
        Assert.Single(NoticesOf(NotificationType.PaymentDueSoon));
    }

    [Fact]
    public async Task UnaPersonaConVariasUnidadesRecibeUnSoloAviso()
    {
        SetRule(NoticeKind.OnDue, true);
        var period = AddPublished(Due);
        var unitA = AddUnitWithCharge(period, 100_000m);
        var unitB = AddUnitWithCharge(period, 100_000m);
        var owner = AddOwner(unitA);
        _env.T.AddOwner(unitB, owner);

        await Remind(Due);

        Assert.Single(NoticesOf(NotificationType.PaymentDueToday));
    }

    [Fact]
    public async Task ElAvisoDelDiaDeVencimientoAvisaHoy()
    {
        SetRule(NoticeKind.OnDue, true);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 100_000m);
        var owner = AddOwner(unit);
        var tenantUser = _env.T.AddUser(_env.Company, UserRole.Owner);
        _env.T.AddResident(unit, tenantUser);

        Assert.Equal(0, (await Remind(Due.AddDays(-1))).Notifications);          // el dia antes no
        var result = await Remind(Due);

        Assert.Equal(2, result.Notifications);                                   // propietario y residente
        Assert.Equal(new[] { owner.Id, tenantUser.Id }.Order().ToArray(), NoticesOf(NotificationType.PaymentDueToday).Select(n => n.RecipientId).Order().ToArray());
    }

    [Fact]
    public async Task SinReglaEncendidaNoSeAvisaDeNada()
    {
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 100_000m);
        AddOwner(unit);

        Assert.Equal(0, (await Remind(Due)).Notifications);
        Assert.Equal(0, (await Remind(Due.AddDays(-3))).Notifications);

        SetRule(NoticeKind.OnDue, false);                                         // regla guardada pero apagada
        Assert.Equal(0, (await Remind(Due)).Notifications);
    }

    [Fact]
    public async Task NoSeAvisaAntesDeLas8DeLaMananaNiDeUnPeriodoEnBorrador()
    {
        SetRule(NoticeKind.OnDue, true);
        var period = AddPublished(Due);
        var unit = AddUnitWithCharge(period, 100_000m);
        AddOwner(unit);

        Assert.Equal(0, (await Remind(Due, hour: 7)).Notifications);

        period.Status = ExpensePeriodStatus.Draft;
        _env.T.Db.SaveChanges();
        Assert.Equal(0, (await Remind(Due, hour: 10)).Notifications);
    }

    [Fact]
    public async Task ElAvisoNoMezclaEdificios()
    {
        SetRule(NoticeKind.OnDue, true);                                          // solo el edificio de las pruebas
        var other = _env.T.AddBuilding(_env.Company, "Otro edificio");
        var otherPeriod = new ExpensePeriod
        {
            CompanyId = _env.Company.Id, BuildingId = other.Id, Year = 2026, Month = 3, Name = "03/2026", StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2026, 3, 28), DueDate = Due, Status = ExpensePeriodStatus.Published
        };
        _env.T.Db.ExpensePeriods.Add(otherPeriod);
        var otherUnit = _env.T.AddUnit(other);
        _env.T.Db.ExpenseCharges.Add(new ExpenseCharge { CompanyId = _env.Company.Id, ExpensePeriodId = otherPeriod.Id, UnitId = otherUnit.Id, Concept = "x", Amount = 100_000m });
        _env.T.Db.SaveChanges();
        AddOwner(otherUnit);

        Assert.Equal(0, (await Remind(Due)).Notifications);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Resumen del Centro
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ElResumenMuestraLaSeccionDeDocumentosYComunicacion()
    {
        var before = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Documents);
        Assert.Equal(ConfigSectionStatus.Optional, before.Status);
        Assert.Contains(before.Summary, i => i.Label == "Avisos automáticos encendidos" && i.Value == "2 de 5");
        Assert.Contains(before.Summary, i => i.Label == "Modelos de documentos" && i.Value == "Estándar de CONDOPY");
        Assert.Equal("self", before.LinkKind);

        SetRule(NoticeKind.OnDue, true);
        var after = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Documents);
        Assert.Equal(ConfigSectionStatus.Complete, after.Status);
        Assert.Contains(after.Summary, i => i.Value == "3 de 5");
    }

    [Fact]
    public async Task ElResumenDeMoraMuestraElDetalleYLasUnidadesExoneradas()
    {
        SetLateFee(2m, LateFeeFrequency.Weekly, b =>
        {
            b.GraceDays = 4;
            b.LateFeeCapPercentage = 40m;
            b.LateFeeMinAmount = 15_000m;
        });
        var unit = _env.T.AddUnit(_env.Building);
        _env.T.Db.Units.Single(x => x.Id == unit.Id).LateFeeExempt = true;
        _env.T.Db.SaveChanges();

        var section = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.LateFee);

        Assert.Equal(ConfigSectionStatus.Complete, section.Status);
        Assert.Equal("self", section.LinkKind);
        Assert.Contains(section.Summary, i => i.Label == "Interés por mora" && i.Value == "2 % semanal");
        Assert.Contains(section.Summary, i => i.Label == "Días de gracia" && i.Value == "4");
        Assert.Contains(section.Summary, i => i.Label == "Tope de la mora" && i.Value.StartsWith("40 %"));
        Assert.Contains(section.Summary, i => i.Label == "Unidades exoneradas" && i.Value == "1");
    }

    [Fact]
    public async Task ElResumenDelPresupuestoMuestraElUmbral()
    {
        _env.Setup();
        _env.T.Db.FinanceSettings.Single(x => x.BuildingId == _env.Building.Id).BudgetWarnPercent = 20;
        _env.T.Db.SaveChanges();

        var section = FinanceEnv.Ok(await Api().GetOverview(_env.Building.Id, default)).Sections.Single(s => s.Key == ConfigSectionKeys.Budget);

        Assert.Contains(section.Summary, i => i.Label == "Umbral del semáforo" && i.Value == "20 %");
    }

    [Fact]
    public async Task LaFichaDelEdificioRegistraLosModelosDeDocumentosEnElHistorial()
    {
        var before = BuildingConfigSnapshot.Capture(B, []);
        B.UseStandardTemplates = false;
        var after = BuildingConfigSnapshot.Capture(B, []);

        var diff = BuildingConfigSnapshot.Diff(before, after);

        Assert.Equal([ConfigSectionKeys.Documents], diff.Keys.ToArray());
        Assert.Equal("useStandardTemplates", diff[ConfigSectionKeys.Documents].Single().Field);
        await Task.CompletedTask;
    }
}
