using System.Security.Claims;
using Condo.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Condo.Tests.Marketplace;

/// <summary>Limite de tasa de las escrituras del marketplace: por usuario y por tipo de accion, sin molestar el uso normal.</summary>
public class MarketplaceRateLimitingTests
{
    private const string Id = "5f1c7a14-0000-0000-0000-000000000000";

    private static DefaultHttpContext Request(string method, string path, Guid? user = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (user.HasValue)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Value.ToString())], "test"));
        }

        return context;
    }

    // ── Clasificacion ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/marketplace/listings/explore", MarketplaceRateTier.None)]
    [InlineData("GET", "/api/marketplace/reservations/mine", MarketplaceRateTier.None)]
    [InlineData("GET", "/api/marketplace/payments/pending", MarketplaceRateTier.None)]
    [InlineData("GET", "/api/marketplace/refunds", MarketplaceRateTier.None)]
    [InlineData("POST", "/api/marketplace/listings", MarketplaceRateTier.Write)]
    [InlineData("PUT", "/api/marketplace/listings/" + Id, MarketplaceRateTier.Write)]
    [InlineData("POST", "/api/marketplace/listings/" + Id + "/suspend", MarketplaceRateTier.Write)]
    [InlineData("POST", "/api/marketplace/reservations/quote", MarketplaceRateTier.Write)]
    [InlineData("POST", "/api/marketplace/account/adjustments", MarketplaceRateTier.Write)]
    [InlineData("POST", "/api/marketplace/reservations", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/reservations/" + Id + "/payment", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/reservations/" + Id + "/cancel", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/reservations/" + Id + "/owner-cancel", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/reservations/" + Id + "/claim", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/reservations/" + Id + "/start-response", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/payments/" + Id + "/approve", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/payments/" + Id + "/reject", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/refunds/" + Id + "/return", MarketplaceRateTier.Sensitive)]
    [InlineData("POST", "/api/marketplace/claims/" + Id + "/resolve", MarketplaceRateTier.Sensitive)]
    [InlineData("GET", "/api/marketplace/reservations/" + Id + "/receipt-pdf", MarketplaceRateTier.HeavyRead)]
    [InlineData("GET", "/api/marketplace/handover-notes/" + Id + "/pdf", MarketplaceRateTier.HeavyRead)]
    [InlineData("GET", "/api/marketplace/account/statement/export", MarketplaceRateTier.HeavyRead)]
    [InlineData("POST", "/api/owner-payments", MarketplaceRateTier.None)]          // lo que no es del marketplace no se toca
    [InlineData("POST", "/api/auth/login", MarketplaceRateTier.None)]
    public void Cada_pedido_se_clasifica_segun_lo_que_hace(string method, string path, MarketplaceRateTier expected) =>
        Assert.Equal(expected, MarketplaceRateLimiting.Classify(method, path));

    // ── Comportamiento ───────────────────────────────────────────────────────

    [Fact]
    public void Las_acciones_sensibles_se_cortan_al_pasar_el_tope_y_el_resto_de_los_usuarios_no_se_ve_afectado()
    {
        using var limiter = MarketplaceRateLimiting.CreateLimiter();
        var abusive = Guid.NewGuid();
        var neighbour = Guid.NewGuid();

        for (var i = 0; i < MarketplaceRateLimiting.SensitivePerMinute; i++)
        {
            using var lease = limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations", abusive));
            Assert.True(lease.IsAcquired, $"Se cortó en el pedido {i + 1}, antes del tope.");
        }

        using (var over = limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations", abusive)))
        {
            Assert.False(over.IsAcquired);
        }

        using var other = limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations", neighbour));
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public void Cortar_las_acciones_sensibles_no_frena_las_lecturas_ni_las_escrituras_comunes_del_mismo_usuario()
    {
        using var limiter = MarketplaceRateLimiting.CreateLimiter();
        var user = Guid.NewGuid();
        for (var i = 0; i <= MarketplaceRateLimiting.SensitivePerMinute; i++)
        {
            limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations", user)).Dispose();
        }

        for (var i = 0; i < 200; i++)
        {
            using var read = limiter.AttemptAcquire(Request("GET", "/api/marketplace/listings/explore", user));
            Assert.True(read.IsAcquired);
        }

        using var write = limiter.AttemptAcquire(Request("POST", "/api/marketplace/listings", user));
        Assert.True(write.IsAcquired);
    }

    [Fact]
    public void Un_uso_normal_no_se_acerca_al_tope()
    {
        // Un Encargado confirmando 10 pagos seguidos y un vecino cotizando 30 horarios en un minuto entran sin problema.
        using var limiter = MarketplaceRateLimiting.CreateLimiter();
        var manager = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            using var lease = limiter.AttemptAcquire(Request("POST", $"/api/marketplace/payments/{Guid.NewGuid()}/approve", manager));
            Assert.True(lease.IsAcquired);
        }

        var neighbour = Guid.NewGuid();
        for (var i = 0; i < 30; i++)
        {
            using var lease = limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations/quote", neighbour));
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public void La_generacion_de_PDF_tiene_su_propio_tope()
    {
        using var limiter = MarketplaceRateLimiting.CreateLimiter();
        var user = Guid.NewGuid();
        for (var i = 0; i < MarketplaceRateLimiting.HeavyReadsPerMinute; i++)
        {
            Assert.True(limiter.AttemptAcquire(Request("GET", $"/api/marketplace/reservations/{Guid.NewGuid()}/receipt-pdf", user)).IsAcquired);
        }

        Assert.False(limiter.AttemptAcquire(Request("GET", $"/api/marketplace/reservations/{Guid.NewGuid()}/receipt-pdf", user)).IsAcquired);
    }

    [Fact]
    public void Lo_que_no_es_del_marketplace_nunca_se_limita()
    {
        using var limiter = MarketplaceRateLimiting.CreateLimiter();
        for (var i = 0; i < 500; i++)
        {
            using var lease = limiter.AttemptAcquire(Request("POST", "/api/owner-payments", Guid.NewGuid()));
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public void Sin_sesion_se_cuenta_por_IP_y_no_mezcla_a_los_usuarios_con_sesion()
    {
        using var limiter = MarketplaceRateLimiting.CreateLimiter();
        for (var i = 0; i <= MarketplaceRateLimiting.SensitivePerMinute; i++)
        {
            limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations")).Dispose();      // anonimo
        }

        Assert.False(limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/api/marketplace/reservations", Guid.NewGuid())).IsAcquired);
    }
}
