using System.Net;
using System.Text.Json;
using Condo.Api.Services;
using Condo.Domain.Common;
using Condo.Tests.Support;
using Microsoft.AspNetCore.Http;

namespace Condo.Tests.Marketplace;

public class MarketplaceAuditTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly FakeTenantContext _tenant = new();
    private readonly Guid _companyId;
    private readonly Guid _buildingId;

    public MarketplaceAuditTests()
    {
        var company = _t.AddCompany();
        var building = _t.AddBuilding(company);
        _companyId = company.Id;
        _buildingId = building.Id;
    }

    public void Dispose() => _t.Dispose();

    private MarketplaceAudit Audit(HttpContext? http, FakeTenantContext? tenant = null) =>
        new(_t.Db, tenant ?? _tenant, new HttpContextAccessor { HttpContext = http });

    private static DefaultHttpContext Http(string? forwardedFor = null, string? ip = "127.0.0.1", string? userAgent = null)
    {
        var http = new DefaultHttpContext();
        if (forwardedFor is not null) http.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (ip is not null) http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userAgent is not null) http.Request.Headers.UserAgent = userAgent;
        return http;
    }

    [Fact]
    public void Registra_quien_cuando_que_sobre_que_y_de_que_estado_a_cual()
    {
        var entityId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var evt = Audit(Http()).Record(_companyId, _buildingId, "MarketplaceReservation", entityId,
            MarketplaceEventActions.ReservationConfirmed, "InReview", "Confirmed");
        _t.Db.SaveChanges();

        var saved = _t.NewContext().MarketplaceEvents.Single(x => x.Id == evt.Id);
        Assert.Equal(_tenant.UserId, saved.UserId);
        Assert.Equal(_companyId, saved.CompanyId);
        Assert.Equal(_buildingId, saved.BuildingId);
        Assert.Equal("reservation.confirmed", saved.Action);
        Assert.Equal("MarketplaceReservation", saved.EntityType);
        Assert.Equal(entityId, saved.EntityId);
        Assert.Equal("InReview", saved.FromStatus);
        Assert.Equal("Confirmed", saved.ToStatus);
        Assert.True(saved.TimestampUtc >= before && saved.TimestampUtc <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Guarda_los_importes_del_momento_para_reconstruir_la_operacion()
    {
        var evt = Audit(Http()).Record(_companyId, _buildingId, "MarketplaceReservation", Guid.NewGuid(),
            MarketplaceEventActions.ReservationCreated, null, "PendingPayment",
            new { BaseAmount = 60_000m, CommissionAmount = 6_000m, TotalAmount = 66_000m, OwnerNetAmount = 60_000m });
        _t.Db.SaveChanges();

        using var json = JsonDocument.Parse(_t.NewContext().MarketplaceEvents.Single(x => x.Id == evt.Id).DataJson!);
        Assert.Equal(66_000m, json.RootElement.GetProperty("TotalAmount").GetDecimal());
        Assert.Equal(6_000m, json.RootElement.GetProperty("CommissionAmount").GetDecimal());
    }

    [Fact]
    public void Un_proceso_automatico_sin_usuario_deja_el_usuario_nulo()
    {
        var system = new FakeTenantContext { IsAuthenticated = false };

        var evt = Audit(http: null, system).Record(_companyId, _buildingId, "MarketplaceReservation", Guid.NewGuid(),
            MarketplaceEventActions.ReservationExpired, "PendingPayment", "Expired");
        _t.Db.SaveChanges();

        Assert.Null(_t.NewContext().MarketplaceEvents.Single(x => x.Id == evt.Id).UserId);
    }

    [Fact]
    public void Se_puede_indicar_el_usuario_explicitamente()
    {
        var other = Guid.NewGuid();

        var evt = Audit(Http()).Record(_companyId, _buildingId, "MarketplacePayment", Guid.NewGuid(),
            MarketplaceEventActions.PaymentApproved, userId: other);

        Assert.Equal(other, evt.UserId);
    }

    [Fact]
    public void Sin_proxy_la_ip_es_la_de_la_conexion()
    {
        var evt = Audit(Http(ip: "190.10.20.30")).Record(_companyId, _buildingId, "X", Guid.NewGuid(), "x.y");

        Assert.Equal("190.10.20.30", evt.IpAddress);
    }

    [Fact]
    public void Detras_de_un_proxy_se_toma_el_ultimo_valor_de_X_Forwarded_For_y_no_el_que_escribio_el_cliente()
    {
        // El cliente escribio 1.1.1.1; el proxy agrego la ip real al final.
        var evt = Audit(Http(forwardedFor: "1.1.1.1, 200.50.60.70", ip: "10.0.0.5")).Record(_companyId, _buildingId, "X", Guid.NewGuid(), "x.y");

        Assert.Equal("200.50.60.70", evt.IpAddress);
    }

    [Fact]
    public void Guarda_el_navegador_y_recorta_los_valores_demasiado_largos()
    {
        var evt = Audit(Http(userAgent: new string('a', 500))).Record(_companyId, _buildingId, "X", Guid.NewGuid(), "x.y");

        Assert.Equal(300, evt.UserAgent!.Length);
    }

    [Fact]
    public void Sin_contexto_http_no_falla_y_deja_ip_y_navegador_vacios()
    {
        var evt = Audit(http: null).Record(_companyId, _buildingId, "X", Guid.NewGuid(), "x.y");

        Assert.Null(evt.IpAddress);
        Assert.Null(evt.UserAgent);
    }

    [Fact]
    public void El_evento_no_se_guarda_hasta_que_quien_llama_guarda_su_cambio()
    {
        // Mismo SaveChanges que el cambio auditado: o se guardan los dos, o ninguno.
        Audit(Http()).Record(_companyId, _buildingId, "X", Guid.NewGuid(), "x.y");

        Assert.Empty(_t.NewContext().MarketplaceEvents);

        _t.Db.SaveChanges();

        Assert.Single(_t.NewContext().MarketplaceEvents);
    }
}
