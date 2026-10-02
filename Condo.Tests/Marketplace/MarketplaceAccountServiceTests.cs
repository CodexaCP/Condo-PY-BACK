using ClosedXML.Excel;
using Condo.Api.Documents;
using Condo.Api.Services;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Condo.Infrastructure.Persistence;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Marketplace;

public class MarketplaceAccountServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly FakeAccessScope _access;

    private readonly Company _company;
    private readonly Building _building;
    private readonly Building _otherBuilding;
    private readonly Unit _unit;
    private readonly ApplicationUser _juan;
    private readonly ApplicationUser _pedro;
    private readonly ApplicationUser _superAdmin;
    private readonly ApplicationUser _companyAdmin;
    private readonly ApplicationUser _manager;
    private readonly ApplicationUser _operator;
    private readonly ApplicationUser _otherManager;
    private readonly MarketplaceListing _listing;
    private int _ref;

    public MarketplaceAccountServiceTests()
    {
        _access = new FakeAccessScope(_tenant);
        _company = _t.AddCompany();
        _companyAdmin = _t.AddUser(_company, UserRole.CompanyAdmin);
        _building = _t.AddBuilding(_company, "Edificio Aurora");
        _otherBuilding = _t.AddBuilding(_company, "Edificio B");
        _unit = _t.AddUnit(_building, "302");
        foreach (var b in new[] { _building, _otherBuilding })
        {
            _t.AssignPlan(b, _companyAdmin, includesMarketplace: true);
            _t.EnableMarketplace(b);
        }

        _juan = _t.AddUser(_company, name: "Juan");
        _t.AddOwner(_unit, _juan, primary: true);
        _pedro = _t.AddUser(_company, UserRole.Resident, "Pedro");
        _t.AddResident(_t.AddUnit(_building, "101"), _pedro);
        _superAdmin = _t.AddUser(null, UserRole.SuperAdmin, "Super");
        _manager = _t.AddUser(_company, UserRole.BuildingManager, "Encargado");
        _t.AddBuildingAccess(_manager, _building);
        _operator = _t.AddUser(_company, UserRole.CompanyOperator, "Operador");
        _t.AddBuildingAccess(_operator, _building);
        _otherManager = _t.AddUser(_company, UserRole.BuildingManager, "OtroEncargado");
        _t.AddBuildingAccess(_otherManager, _otherBuilding);
        _listing = _t.AddListing(_building, _unit, _juan, 20_000m);

        LoginStaff(_manager, "BuildingManager");
    }

    public void Dispose() => _t.Dispose();

    private void LoginStaff(ApplicationUser user, string role)
    {
        _tenant.UserId = user.Id;
        _tenant.Role = role;
        _tenant.CompanyId = user.CompanyId;
        _access.Buildings.Clear();
        foreach (var access in _t.NewContext().UserBuildingAccesses.Where(x => x.ApplicationUserId == user.Id && !x.IsDeleted))
        {
            _access.Buildings.Add(access.BuildingId);
        }
    }

    private void LoginSuperAdmin() => LoginStaff(_superAdmin, "SuperAdmin");

    private MarketplaceAccountService Accounts()
    {
        var db = (CondoDbContext)_t.NewContext();
        var gate = new MarketplaceModuleGate(db);
        var scope = new MarketplaceScope(db, _tenant, _access, gate);
        var audit = new MarketplaceAudit(db, _tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        return new MarketplaceAccountService(db, _tenant, scope, audit);
    }

    private MarketplaceCreditService Credits()
    {
        var db = (CondoDbContext)_t.NewContext();
        var audit = new MarketplaceAudit(db, _tenant, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        return new MarketplaceCreditService(db, _tenant, audit, new PushDispatcher(db, new NoopPushSender(), NullLogger<PushDispatcher>.Instance));
    }

    private MarketplaceReservation SeedReservation(
        MarketplaceReservationStatus status, MarketplaceCreditStatus credit, DateTime endsAt, decimal net = 60_000m)
    {
        var reservation = _t.NewReservation(_listing, _pedro, $"MP-{++_ref:D8}", status);
        reservation.EndsAtUtc = endsAt;
        reservation.StartsAtUtc = endsAt.AddHours(-3);
        reservation.OwnerNetAmount = net;
        reservation.BaseAmount = net;
        reservation.CommissionAmount = net / 10;
        reservation.TotalAmount = net + net / 10;
        reservation.CreditStatus = credit;
        reservation.ExpiresAtUtc = null;
        _t.Db.MarketplaceReservations.Add(reservation);
        _t.Db.SaveChanges();
        return reservation;
    }

    private MarketplaceAccountMovement Movement(
        MarketplaceAccountMovementKind kind, decimal amount, DateTime at, Building? building = null, Guid? reservationId = null, string concept = "Prueba")
    {
        var b = building ?? _building;
        var movement = new MarketplaceAccountMovement
        {
            CompanyId = _company.Id, BuildingId = b.Id, Kind = kind, Amount = amount, OccurredAtUtc = at,
            ReservationId = reservationId, Concept = concept
        };
        _t.Db.MarketplaceAccountMovements.Add(movement);
        _t.Db.SaveChanges();
        return movement;
    }

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private async Task<MarketplaceStatementDto> Statement(DateOnly? from = null, DateOnly? to = null, Building? building = null)
    {
        var result = await Accounts().GetStatementAsync((building ?? _building).Id, from, to, CancellationToken.None);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    // ── Extracto: numeros ────────────────────────────────────────────────────

    [Fact]
    public async Task El_extracto_suma_ingresos_acreditaciones_devoluciones_y_ajustes_del_periodo()
    {
        // Septiembre (antes del periodo): saldo inicial 10.000
        Movement(MarketplaceAccountMovementKind.PaymentIn, 10_000m, new DateTime(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc));
        // Octubre
        Movement(MarketplaceAccountMovementKind.PaymentIn, 66_000m, new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc));
        Movement(MarketplaceAccountMovementKind.PaymentIn, 44_000m, new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc));
        Movement(MarketplaceAccountMovementKind.OwnerCredit, -60_000m, new DateTime(2026, 10, 8, 15, 0, 0, DateTimeKind.Utc));
        Movement(MarketplaceAccountMovementKind.RefundOut, -6_000m, new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc));
        Movement(MarketplaceAccountMovementKind.Adjustment, 1_000m, new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc));
        // Noviembre (despues del periodo)
        Movement(MarketplaceAccountMovementKind.PaymentIn, 99_000m, new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc));

        var statement = await Statement(D(2026, 10, 1), D(2026, 10, 31));
        var s = statement.Summary;

        Assert.Equal(10_000m, s.OpeningBalance);
        Assert.Equal(110_000m, s.TotalIn);
        Assert.Equal(60_000m, s.TotalCredited);
        Assert.Equal(6_000m, s.TotalRefunds);
        Assert.Equal(1_000m, s.TotalAdjustments);
        Assert.Equal(55_000m, s.ClosingBalance);        // 10.000 + 110.000 - 60.000 - 6.000 + 1.000
        Assert.Equal(154_000m, s.CurrentBalance);       // incluye lo de noviembre
        Assert.Equal(5, statement.Rows.Count);
        Assert.Equal("Edificio Aurora", statement.BuildingName);
    }

    [Fact]
    public async Task Las_filas_salen_en_orden_y_con_el_numero_de_reserva_y_quien_las_registro()
    {
        var reservation = SeedReservation(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddDays(1));
        Movement(MarketplaceAccountMovementKind.PaymentIn, 66_000m, new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc),
            reservationId: reservation.Id, concept: "Pago de reserva");
        var adjustment = Movement(MarketplaceAccountMovementKind.Adjustment, -500m, new DateTime(2026, 10, 4, 15, 0, 0, DateTimeKind.Utc));
        var db = _t.NewContext();
        db.MarketplaceAccountMovements.Single(x => x.Id == adjustment.Id).CreatedByUserId = _superAdmin.Id;
        db.SaveChanges();

        var rows = (await Statement(D(2026, 10, 1), D(2026, 10, 31))).Rows;

        Assert.Equal(["Adjustment", "PaymentIn"], rows.Select(x => x.Kind));
        Assert.Equal("Super Prueba", rows[0].CreatedByName);
        Assert.Null(rows[1].CreatedByName);                 // automatico
        Assert.Equal(reservation.Reference, rows[1].Reference);
    }

    [Fact]
    public async Task La_ganancia_de_la_gestion_es_el_saldo_menos_lo_que_falta_acreditar_a_los_propietarios()
    {
        var reservation = SeedReservation(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddDays(1));
        Movement(MarketplaceAccountMovementKind.PaymentIn, 66_000m, DateTime.UtcNow.AddMinutes(-5), reservationId: reservation.Id);

        var before = (await Statement()).Summary;

        Assert.Equal(66_000m, before.CurrentBalance);
        Assert.Equal(60_000m, before.PendingToCredit);
        Assert.Equal(6_000m, before.ManagementGain);        // la comision del 10%

        // Termina la reserva y pasan 24 h: se acredita al propietario y la ganancia sigue siendo la comision.
        var db = _t.NewContext();
        var saved = db.MarketplaceReservations.Single(x => x.Id == reservation.Id);
        saved.EndsAtUtc = DateTime.UtcNow.AddHours(-26);
        saved.StartsAtUtc = DateTime.UtcNow.AddHours(-29);
        db.SaveChanges();
        await Credits().ProcessDueAsync(CancellationToken.None);

        var after = (await Statement()).Summary;
        Assert.Equal(6_000m, after.CurrentBalance);
        Assert.Equal(0m, after.PendingToCredit);
        Assert.Equal(6_000m, after.ManagementGain);
    }

    [Fact]
    public async Task Las_reservas_no_confirmadas_no_cuentan_como_pendientes_de_acreditar()
    {
        SeedReservation(MarketplaceReservationStatus.InReview, MarketplaceCreditStatus.None, DateTime.UtcNow.AddDays(1));
        SeedReservation(MarketplaceReservationStatus.Cancelled, MarketplaceCreditStatus.None, DateTime.UtcNow.AddDays(1));
        SeedReservation(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Credited, DateTime.UtcNow.AddDays(-3));

        Assert.Equal(0m, (await Statement()).Summary.PendingToCredit);
    }

    [Fact]
    public async Task Solo_entran_los_movimientos_del_edificio_consultado()
    {
        Movement(MarketplaceAccountMovementKind.PaymentIn, 66_000m, DateTime.UtcNow.AddMinutes(-1));
        Movement(MarketplaceAccountMovementKind.PaymentIn, 99_000m, DateTime.UtcNow.AddMinutes(-1), _otherBuilding);

        var statement = await Statement();

        Assert.Single(statement.Rows);
        Assert.Equal(66_000m, statement.Summary.CurrentBalance);
    }

    [Fact]
    public async Task El_periodo_se_mide_en_hora_de_Paraguay_el_dia_empieza_a_las_03_utc()
    {
        // 02:30 UTC del 3/10 = 23:30 del 2/10 en Paraguay; 03:00 UTC del 3/10 = 00:00 del 3/10.
        Movement(MarketplaceAccountMovementKind.PaymentIn, 1_000m, new DateTime(2026, 10, 3, 2, 30, 0, DateTimeKind.Utc));
        Movement(MarketplaceAccountMovementKind.PaymentIn, 2_000m, new DateTime(2026, 10, 3, 3, 0, 0, DateTimeKind.Utc));

        var day2 = await Statement(D(2026, 10, 2), D(2026, 10, 2));
        var day3 = await Statement(D(2026, 10, 3), D(2026, 10, 3));

        Assert.Equal(1_000m, Assert.Single(day2.Rows).Amount);
        Assert.Equal(2_000m, Assert.Single(day3.Rows).Amount);
        Assert.Equal(1_000m, day3.Summary.OpeningBalance);
    }

    [Fact]
    public async Task Sin_fechas_el_extracto_es_el_del_mes_en_curso()
    {
        var today = MarketplaceAccountService.TodayLocal();

        var statement = await Statement();

        Assert.Equal(new DateOnly(today.Year, today.Month, 1), statement.FromDate);
        Assert.Equal(today, statement.ToDate);
    }

    [Fact]
    public async Task Una_fecha_hasta_anterior_a_la_desde_se_rechaza()
    {
        var result = await Accounts().GetStatementAsync(_building.Id, D(2026, 10, 10), D(2026, 10, 1), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    // ── Quien lo ve ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("CompanyAdmin")]
    [InlineData("BuildingManager")]
    public async Task Lo_ven_el_SuperAdmin_el_Administrador_de_empresa_y_el_Encargado(string role)
    {
        var user = role switch { "SuperAdmin" => _superAdmin, "CompanyAdmin" => _companyAdmin, _ => _manager };
        LoginStaff(user, role);
        _access.Buildings.Add(_building.Id);

        var result = await Accounts().GetStatementAsync(_building.Id, null, null, CancellationToken.None);

        Assert.True(result.Ok, result.Error?.Message);
    }

    [Fact]
    public async Task El_Operador_no_ve_el_extracto()
    {
        LoginStaff(_operator, "CompanyOperator");

        var result = await Accounts().GetStatementAsync(_building.Id, null, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Los_propietarios_y_residentes_no_ven_el_extracto_ni_siquiera_el_propietario_publicador()
    {
        _tenant.UserId = _juan.Id;
        _tenant.Role = "Owner";
        _tenant.CompanyId = _company.Id;
        Assert.Equal(403, (await Accounts().GetStatementAsync(_building.Id, null, null, CancellationToken.None)).Error!.StatusCode);

        _tenant.UserId = _pedro.Id;
        _tenant.Role = "Resident";
        Assert.Equal(403, (await Accounts().GetStatementAsync(_building.Id, null, null, CancellationToken.None)).Error!.StatusCode);
    }

    [Fact]
    public async Task El_Encargado_de_otro_edificio_no_ve_este_extracto()
    {
        LoginStaff(_otherManager, "BuildingManager");

        var result = await Accounts().GetStatementAsync(_building.Id, null, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task El_personal_de_otra_empresa_no_ve_el_extracto()
    {
        var otherCompany = _t.AddCompany();
        var intruder = _t.AddUser(otherCompany, UserRole.BuildingManager);
        LoginStaff(intruder, "BuildingManager");
        _access.Buildings.Add(_building.Id);

        Assert.Equal(404, (await Accounts().GetStatementAsync(_building.Id, null, null, CancellationToken.None)).Error!.StatusCode);
    }

    [Fact]
    public async Task Con_el_modulo_apagado_el_extracto_responde_modulo_deshabilitado()
    {
        _t.EnableMarketplace(_building, enabled: false);

        var result = await Accounts().GetStatementAsync(_building.Id, null, null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(MarketplaceModuleGate.DisabledCode, result.Error!.Code);
    }

    [Fact]
    public async Task Solo_el_SuperAdmin_puede_editar_y_ve_el_boton_de_revertir()
    {
        var reservation = SeedReservation(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(-26));
        await Credits().CreditDueAsync(CancellationToken.None);

        var asManager = await Statement();
        Assert.False(asManager.CanEdit);
        Assert.DoesNotContain(asManager.Rows, r => r.CanReverse);

        LoginSuperAdmin();
        var asSuper = await Statement();
        Assert.True(asSuper.CanEdit);
        var row = Assert.Single(asSuper.Rows, r => r.Kind == "OwnerCredit");
        Assert.True(row.CanReverse);
        Assert.Equal(reservation.Id, row.ReservationId);
    }

    [Fact]
    public async Task No_se_ofrece_revertir_cuando_el_saldo_ya_se_uso_en_expensas()
    {
        SeedReservation(MarketplaceReservationStatus.Completed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddHours(-26));
        await Credits().CreditDueAsync(CancellationToken.None);
        var db = _t.NewContext();
        db.OwnerCreditMovements.Single().RemainingAmount = 10_000m;
        db.SaveChanges();
        LoginSuperAdmin();

        Assert.DoesNotContain((await Statement()).Rows, r => r.CanReverse);
    }

    // ── Ajustes manuales ─────────────────────────────────────────────────────

    private MarketplaceAdjustmentRequest Adjustment(decimal amount = 5_000m, string concept = "Corrección") =>
        new() { BuildingId = _building.Id, Amount = amount, Concept = concept };

    [Fact]
    public async Task El_SuperAdmin_carga_un_ajuste_con_signo_y_queda_en_el_extracto_y_en_la_auditoria()
    {
        LoginSuperAdmin();

        var plus = await Accounts().AddAdjustmentAsync(Adjustment(5_000m, "  Corrección de saldo  "), CancellationToken.None);
        var minus = await Accounts().AddAdjustmentAsync(Adjustment(-2_000m, "Gasto bancario"), CancellationToken.None);

        Assert.True(plus.Ok, plus.Error?.Message);
        Assert.True(minus.Ok);
        Assert.Equal("Corrección de saldo", plus.Value!.Concept);
        Assert.Equal("Super Prueba", plus.Value.CreatedByName);

        var statement = await Statement();
        Assert.Equal(2, statement.Rows.Count);
        Assert.Equal(3_000m, statement.Summary.TotalAdjustments);
        Assert.Equal(3_000m, statement.Summary.CurrentBalance);
        var events = _t.NewContext().MarketplaceEvents.Where(x => x.Action == "account.movement").ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(_superAdmin.Id, e.UserId));
    }

    [Theory]
    [InlineData("CompanyAdmin")]
    [InlineData("BuildingManager")]
    [InlineData("CompanyOperator")]
    public async Task Nadie_mas_que_el_SuperAdmin_puede_cargar_ajustes(string role)
    {
        var user = role switch { "CompanyAdmin" => _companyAdmin, "BuildingManager" => _manager, _ => _operator };
        LoginStaff(user, role);
        _access.Buildings.Add(_building.Id);

        var result = await Accounts().AddAdjustmentAsync(Adjustment(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(403, result.Error!.StatusCode);
        Assert.Empty(_t.NewContext().MarketplaceAccountMovements);
    }

    [Theory]
    [InlineData(0, "Concepto")]
    [InlineData(1_000.5, "Concepto")]
    [InlineData(2_000_000_000_000, "Concepto")]
    [InlineData(1_000, "")]
    [InlineData(1_000, "   ")]
    public async Task Un_ajuste_invalido_se_rechaza(double amount, string concept)
    {
        LoginSuperAdmin();

        var result = await Accounts().AddAdjustmentAsync(Adjustment((decimal)amount, concept), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Empty(_t.NewContext().MarketplaceAccountMovements);
    }

    [Fact]
    public async Task El_concepto_no_puede_superar_300_caracteres()
    {
        LoginSuperAdmin();

        Assert.Equal(400, (await Accounts().AddAdjustmentAsync(Adjustment(1_000m, new string('x', 301)), CancellationToken.None)).Error!.StatusCode);
    }

    [Fact]
    public async Task Un_ajuste_en_un_edificio_inexistente_no_se_carga()
    {
        LoginSuperAdmin();
        var request = Adjustment();
        request.BuildingId = Guid.NewGuid();

        Assert.Equal(404, (await Accounts().AddAdjustmentAsync(request, CancellationToken.None)).Error!.StatusCode);
    }

    // ── Excel ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_Excel_trae_el_resumen_y_una_fila_por_movimiento_con_valores_reales()
    {
        var reservation = SeedReservation(MarketplaceReservationStatus.Confirmed, MarketplaceCreditStatus.Pending, DateTime.UtcNow.AddDays(1));
        Movement(MarketplaceAccountMovementKind.PaymentIn, 66_000m, new DateTime(2026, 10, 5, 15, 30, 0, DateTimeKind.Utc),
            reservationId: reservation.Id, concept: "Pago de reserva");
        Movement(MarketplaceAccountMovementKind.Adjustment, -1_000m, new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc), concept: "Ajuste");
        var statement = await Statement(D(2026, 10, 1), D(2026, 10, 31));

        var bytes = MarketplaceAccountExcelExporter.Build(statement, new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Resumen", "Movimientos"], workbook.Worksheets.Select(w => w.Name));
        var moves = workbook.Worksheet("Movimientos");
        Assert.Equal("Importe", moves.Cell(1, 5).GetString());
        Assert.Equal("Ingreso por reserva", moves.Cell(2, 2).GetString());
        Assert.Equal(reservation.Reference, moves.Cell(2, 4).GetString());
        Assert.Equal(66_000, moves.Cell(2, 5).GetValue<decimal>());
        Assert.Equal(new DateTime(2026, 10, 5, 12, 30, 0), moves.Cell(2, 1).GetDateTime());   // hora de Paraguay (UTC-3)
        Assert.Equal("Ajuste manual", moves.Cell(3, 2).GetString());
        Assert.Equal(-1_000, moves.Cell(3, 5).GetValue<decimal>());
        Assert.Equal("Automático", moves.Cell(2, 6).GetString());
        Assert.True(string.IsNullOrEmpty(moves.Cell(4, 1).GetString()));

        var summary = workbook.Worksheet("Resumen");
        var labels = Enumerable.Range(1, 20).Select(r => summary.Cell(r, 1).GetString()).ToList();
        Assert.Contains("Ganancia de la gestión (hoy)", labels);
        var row = labels.IndexOf("Ingresos por reservas") + 1;
        Assert.Equal(66_000, summary.Cell(row, 2).GetValue<decimal>());
    }

    [Fact]
    public async Task El_nombre_del_archivo_es_seguro_y_lleva_el_periodo()
    {
        var statement = await Statement(D(2026, 10, 1), D(2026, 10, 31));
        statement.BuildingName = "Edificio Ñandú & Co.";

        var name = MarketplaceAccountExcelExporter.FileName(statement);

        Assert.Equal("marketplace-cuenta-Edificio-Nandu-Co-20261001-20261031.xlsx", name);
    }

    [Fact]
    public void Cada_tipo_de_movimiento_tiene_su_etiqueta_en_castellano()
    {
        Assert.Equal("Ingreso por reserva", MarketplaceAccountExcelExporter.KindLabel("PaymentIn"));
        Assert.Equal("Acreditado al propietario", MarketplaceAccountExcelExporter.KindLabel("OwnerCredit"));
        Assert.Equal("Devolución al comprador", MarketplaceAccountExcelExporter.KindLabel("RefundOut"));
        Assert.Equal("Ajuste manual", MarketplaceAccountExcelExporter.KindLabel("Adjustment"));
    }
}
