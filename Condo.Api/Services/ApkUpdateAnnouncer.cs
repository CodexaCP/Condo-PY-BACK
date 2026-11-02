namespace Condo.Api.Services;

/// <summary>
/// Avisa por push a los celulares con la app instalada cuando se publica un APK nuevo. Cada pocos minutos mira el APK del sitio
/// (<see cref="ApkInfoService"/>); si su huella cambio desde el ultimo aviso, manda "hay una actualizacion, toca para actualizar". Al
/// tocarla se abre la app, que consulta /api/app/version y muestra el aviso con el boton Descargar.
/// La ultima huella avisada se guarda en un archivo junto al servicio (App:ApkStatePath), asi un reinicio o un despliegue no repite el aviso.
/// La primera vez que corre (sin archivo) solo toma nota del APK actual: no avisa por un APK que ya estaba publicado.
/// </summary>
public sealed class ApkUpdateAnnouncer(
    IServiceScopeFactory scopes, ApkInfoService apk, IConfiguration configuration, ILogger<ApkUpdateAnnouncer> logger) : BackgroundService
{
    // El APK se copia en el despliegue: se espera a que lleve un rato sin cambios antes de avisar.
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(60);

    // Lo ultimo atendido en esta ejecucion: si no se pudiera escribir el archivo de estado, igual no se repite el aviso cada pocos minutos.
    private string? handledFingerprint;

    private string StatePath => configuration["App:ApkStatePath"] is { Length: > 0 } p ? p : Path.Combine(AppContext.BaseDirectory, "apk-announced.txt");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, configuration.GetValue("App:ApkWatchSeconds", 120)));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error revisando el APK publicado.");
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // apagado normal
        }
    }

    /// <summary>Una revision. Devuelve true si mando el aviso. Separada del ciclo para poder probarla con una fecha dada.</summary>
    public async Task<bool> CheckOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        var info = apk.Get();
        if (info is null) return false;
        if (nowUtc - info.LastWriteUtc < SettleTime) return false;

        if (handledFingerprint == info.Fingerprint) return false;

        var previous = ReadState();
        if (previous is null)
        {
            handledFingerprint = info.Fingerprint;
            WriteState(info);
            logger.LogInformation("APK publicado registrado como base (versionCode {Version}); no se avisa por el que ya estaba.", info.VersionCode);
            return false;
        }

        if (previous.Value.Fingerprint == info.Fingerprint)
        {
            handledFingerprint = info.Fingerprint;
            return false;
        }

        // Si el APK nuevo no tiene un versionCode mayor, la app no mostraria nada al abrirse: se anota pero no se molesta a nadie.
        if (previous.Value.VersionCode is { } old && info.VersionCode is { } now && now <= old)
        {
            handledFingerprint = info.Fingerprint;
            WriteState(info);
            logger.LogWarning("Se publico un APK nuevo con el mismo versionCode ({Version}): no se avisa. Sube el versionCode en build.gradle.", now);
            return false;
        }

        var title = configuration["App:UpdatePushTitle"] is { Length: > 0 } t ? t : "Hay una actualización de Expenses";
        var body = configuration["App:UpdatePushBody"] is { Length: > 0 } b ? b : "Toca para actualizar.";

        using var scope = scopes.CreateScope();
        var push = scope.ServiceProvider.GetRequiredService<PushDispatcher>();
        var sent = await push.NotifyAllDevicesAsync(title, body, "AppUpdate", ct);

        handledFingerprint = info.Fingerprint;
        WriteState(info);
        logger.LogInformation("APK nuevo (versionCode {Version}): aviso enviado a {Count} dispositivos.", info.VersionCode, sent);
        return true;
    }

    private (string Fingerprint, int? VersionCode)? ReadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            var parts = File.ReadAllText(StatePath).Trim().Split(';');
            if (parts[0].Length == 0) return null;
            return (parts[0], parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : null);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteState(ApkInfo info)
    {
        try
        {
            File.WriteAllText(StatePath, $"{info.Fingerprint};{info.VersionCode}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "No se pudo guardar el estado del aviso de APK en {Path}: se avisaria de nuevo en el proximo reinicio.", StatePath);
        }
    }
}
