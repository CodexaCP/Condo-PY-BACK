using Condo.Application.Abstractions;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;

namespace Condo.Infrastructure.Services;

// Envio de push notifications via Firebase Cloud Messaging (FCM). Requiere la variable de entorno
// GOOGLE_APPLICATION_CREDENTIALS apuntando a un archivo service-account.json (nunca en el repo).
// Si no esta configurada o el archivo no existe, no revienta: solo loguea y no manda nada, igual
// que ResendEmailSender cuando falta Resend:ApiKey.
public class FirebaseCloudMessagingSender : IPushNotificationSender
{
    private readonly ILogger<FirebaseCloudMessagingSender> logger;
    private readonly Lazy<FirebaseApp?> app;

    public FirebaseCloudMessagingSender(ILogger<FirebaseCloudMessagingSender> logger)
    {
        this.logger = logger;
        app = new Lazy<FirebaseApp?>(CreateApp);
    }

    private FirebaseApp? CreateApp()
    {
        var credentialsPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
        if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath))
        {
            logger.LogWarning("GOOGLE_APPLICATION_CREDENTIALS no configurada — Firebase Cloud Messaging deshabilitado, no se enviaran push notifications.");
            return null;
        }

        return FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(credentialsPath)
        });
    }

    public async Task SendAsync(string deviceToken, string title, string body, IDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        if (app.Value is null) return;

        var message = new Message
        {
            Token = deviceToken,
            Notification = new Notification { Title = title, Body = body },
            Data = data as IReadOnlyDictionary<string, string> ?? data?.ToDictionary(x => x.Key, x => x.Value),
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification { ChannelId = "default" }
            }
        };

        try
        {
            await FirebaseMessaging.GetMessaging(app.Value).SendAsync(message, cancellationToken);
        }
        catch (FirebaseMessagingException ex)
        {
            logger.LogError(ex, "Error enviando push a {DeviceToken}: {ErrorCode}", deviceToken, ex.MessagingErrorCode);
        }
    }
}
