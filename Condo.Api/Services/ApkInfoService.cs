using System.IO.Compression;
using System.Security.Cryptography;

namespace Condo.Api.Services;

/// <summary>Lo que se sabe del APK publicado: su huella (cambia cuando suben uno nuevo), el versionCode que trae y cuando se escribio.</summary>
public sealed record ApkInfo(string Fingerprint, int? VersionCode, long Length, DateTime LastWriteUtc);

/// <summary>
/// Lee el APK que el sitio publica (App:ApkFilePath, por defecto el de /downloads del web en el VPS). Asi la version que anuncia
/// <c>/api/app/version</c> y el aviso a los celulares salen del propio archivo, sin configurar nada a mano cada vez que se sube un APK.
/// Se vuelve a leer solo cuando cambia el tamaño o la fecha del archivo.
/// </summary>
public sealed class ApkInfoService(IConfiguration configuration, ILogger<ApkInfoService> logger)
{
    public const string DefaultApkPath = "/var/www/condo-py/browser/downloads/expenses.apk";

    private readonly object gate = new();
    private (long Length, DateTime LastWrite, ApkInfo Info)? cached;

    public string ApkPath => configuration["App:ApkFilePath"] is { Length: > 0 } path ? path : DefaultApkPath;

    /// <summary>La informacion del APK publicado; null si el archivo no esta (por ejemplo, mientras se copia un despliegue) o no se puede leer.</summary>
    public ApkInfo? Get()
    {
        try
        {
            var file = new FileInfo(ApkPath);
            if (!file.Exists) return null;

            lock (gate)
            {
                if (cached is { } c && c.Length == file.Length && c.LastWrite == file.LastWriteTimeUtc) return c.Info;

                var info = Read(file);
                cached = (file.Length, file.LastWriteTimeUtc, info);
                return info;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(ex, "No se pudo leer el APK publicado en {Path}.", ApkPath);
            return null;
        }
    }

    private static ApkInfo Read(FileInfo file)
    {
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var fingerprint = Convert.ToHexString(SHA256.HashData(stream));

        stream.Position = 0;
        int? versionCode = null;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var manifest = zip.GetEntry("AndroidManifest.xml");
            if (manifest is not null)
            {
                using var entry = manifest.Open();
                using var buffer = new MemoryStream();
                entry.CopyTo(buffer);
                versionCode = AndroidManifestReader.ReadVersionCode(buffer.ToArray());
            }
        }

        return new ApkInfo(fingerprint, versionCode, file.Length, file.LastWriteTimeUtc);
    }
}
