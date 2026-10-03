using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Condo.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Condo.Tests.Marketplace;

/// <summary>
/// El limitador montado en un servidor HTTP real en memoria, con la misma configuracion que usa Program.cs: pasado el tope responde
/// 429 con el formato de error del marketplace y el encabezado Retry-After; el resto sigue funcionando.
/// </summary>
public class MarketplaceRateLimitingHostTests
{
    private static async Task<IHost> StartAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services => services.AddRateLimiter(MarketplaceRateLimiting.Configure));
            web.Configure(app =>
            {
                // Se "autentica" con el usuario que manda el encabezado de prueba (en produccion lo hace el JWT).
                app.Use(async (context, next) =>
                {
                    if (context.Request.Headers.TryGetValue("X-Test-User", out var user))
                    {
                        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "test"));
                    }

                    await next();
                });
                app.UseRateLimiter();
                app.Run(context => context.Response.WriteAsync("ok"));
            });
        });

        return await builder.StartAsync();
    }

    private static HttpRequestMessage Post(string path, string user)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Test-User", user);
        return request;
    }

    [Fact]
    public async Task Pasado_el_tope_responde_429_con_el_mensaje_del_marketplace_y_Retry_After()
    {
        using var host = await StartAsync();
        using var client = host.GetTestClient();
        var user = Guid.NewGuid().ToString();

        for (var i = 0; i < MarketplaceRateLimiting.SensitivePerMinute; i++)
        {
            using var ok = await client.SendAsync(Post("/api/marketplace/reservations", user));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rejected = await client.SendAsync(Post("/api/marketplace/reservations", user));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retry) && int.Parse(retry.First()) > 0);
        var body = await rejected.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal(MarketplaceRateLimiting.RejectedCode, body!["error"]);
        Assert.Equal(MarketplaceRateLimiting.RejectedMessage, body["message"]);
    }

    [Fact]
    public async Task Otro_usuario_y_las_lecturas_siguen_funcionando_mientras_uno_esta_cortado()
    {
        using var host = await StartAsync();
        using var client = host.GetTestClient();
        var abusive = Guid.NewGuid().ToString();
        for (var i = 0; i <= MarketplaceRateLimiting.SensitivePerMinute; i++)
        {
            (await client.SendAsync(Post("/api/marketplace/reservations", abusive))).Dispose();
        }

        using var neighbour = await client.SendAsync(Post("/api/marketplace/reservations", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, neighbour.StatusCode);

        var read = new HttpRequestMessage(HttpMethod.Get, "/api/marketplace/listings/explore");
        read.Headers.Add("X-Test-User", abusive);
        using var readResponse = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        using var other = await client.SendAsync(Post("/api/owner-payments", abusive));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}
