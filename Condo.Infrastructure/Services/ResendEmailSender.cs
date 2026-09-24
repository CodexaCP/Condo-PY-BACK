using System.Net.Http.Headers;
using System.Net.Http.Json;
using Condo.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Condo.Infrastructure.Services;

// Envio de correo via Resend (https://resend.com). Config en appsettings/variables de entorno:
// Resend:ApiKey, Resend:FromEmail (ej: "CONDOPY <no-reply@tramiya.com.py>", requiere dominio verificado en Resend).
// Si falta la ApiKey (todavia no configurada), no revienta: solo loguea y no manda nada, para que el
// resto del flujo (generacion de token, etc.) se pueda seguir probando sin la integracion lista.
public class ResendEmailSender(HttpClient httpClient, IConfiguration configuration, ILogger<ResendEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var apiKey = configuration["Resend:ApiKey"];
        var fromEmail = configuration["Resend:FromEmail"] ?? "CONDOPY <no-reply@tramiya.com.py>";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Resend:ApiKey no configurada — no se envio el correo a {ToEmail} (asunto: {Subject}).", toEmail, subject);
            return;
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await httpClient.PostAsJsonAsync("https://api.resend.com/emails", new
        {
            from = fromEmail,
            to = new[] { toEmail },
            subject,
            html = htmlBody
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Resend devolvio {StatusCode} al enviar a {ToEmail}: {Body}", response.StatusCode, toEmail, body);
        }
    }
}
