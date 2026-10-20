using System.Net;
using System.Text.Json;
using Condo.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Condo.Tests.Support;

// Manejador global de errores: un error no controlado sale como 500 en JSON con un codigo de referencia, conserva las cabeceras CORS
// (sin ellas el navegador solo mostraba "Failed to fetch") y queda en el log con ese mismo codigo.
public class GlobalExceptionMiddlewareTests
{
    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new ListLogger(Entries);
        public void Dispose() { }

        private sealed class ListLogger(List<(LogLevel, string, Exception?)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private static async Task<(TestServer Server, ListLoggerProvider Log)> BuildAsync(string environment = "Production")
    {
        var log = new ListLoggerProvider();
        var host = await new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(l => l.AddProvider(log));
                    services.AddCors(o => o.AddPolicy("frontend", p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
                })
                .Configure(app =>
                {
                    app.UseCors("frontend");
                    app.UseMiddleware<GlobalExceptionMiddleware>();
                    app.Map("/ok", b => b.Run(ctx => ctx.Response.WriteAsync("todo bien")));
                    app.Map("/boom", b => b.Run(_ => throw new InvalidOperationException("Sequence contains no matching element")));
                    app.Map("/big", b => b.Run(_ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge)));
                }))
            .StartAsync();
        return (host.GetTestServer(), log);
    }

    private static HttpRequestMessage Get(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", "https://app.expenses.com.py");
        return request;
    }

    [Fact]
    public async Task Un_error_no_controlado_responde_500_con_mensaje_codigo_y_cabeceras_CORS()
    {
        var (server, _) = await BuildAsync();
        using var client = server.CreateClient();

        var response = await client.SendAsync(Get("/boom"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));   // sin esto el navegador dice "Failed to fetch"
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("message").GetString()!;
        var errorId = body.RootElement.GetProperty("errorId").GetString()!;
        Assert.Equal(8, errorId.Length);
        Assert.Contains(errorId, message);
        // No se filtra el detalle tecnico fuera de desarrollo.
        Assert.DoesNotContain("Sequence contains", message);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("detail").ValueKind);
    }

    [Fact]
    public async Task El_error_queda_en_el_log_con_la_excepcion_y_el_mismo_codigo_de_referencia()
    {
        var (server, log) = await BuildAsync();
        using var client = server.CreateClient();

        var response = await client.SendAsync(Get("/boom"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errorId = body.RootElement.GetProperty("errorId").GetString()!;

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(errorId));
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains("/boom", entry.Message);
    }

    [Fact]
    public async Task En_desarrollo_incluye_el_detalle_tecnico()
    {
        var (server, _) = await BuildAsync("Development");
        using var client = server.CreateClient();

        var response = await client.SendAsync(Get("/boom"));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("Sequence contains no matching element", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Una_peticion_normal_no_se_toca()
    {
        var (server, log) = await BuildAsync();
        using var client = server.CreateClient();

        var response = await client.SendAsync(Get("/ok"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("todo bien", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Una_peticion_demasiado_grande_responde_con_su_codigo_y_no_como_error_del_servidor()
    {
        var (server, log) = await BuildAsync();
        using var client = server.CreateClient();

        var response = await client.SendAsync(Get("/big"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Error);
    }
}
