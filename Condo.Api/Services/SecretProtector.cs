using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Condo.Api.Services;

/// <summary>
/// Protege los secretos de las conexiones de pago (claves privadas del comercio). Cifra con AES-GCM usando una clave maestra que vive
/// FUERA de la base de datos (variable de entorno del servicio, no appsettings.json ni el repositorio): quien obtenga un respaldo de la
/// base no obtiene las claves. Cada cifrado lleva su propio nonce y queda atado a su conexion (datos asociados), asi un secreto copiado
/// a otra fila no se puede descifrar. Con KeyId se puede rotar la clave maestra: las claves anteriores se conservan solo para descifrar.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Hay una clave maestra valida (32 bytes en base64). Sin ella no se pueden guardar ni leer secretos.</summary>
    bool IsConfigured { get; }

    string CurrentKeyId { get; }

    /// <summary>Cifra el conjunto de secretos con la clave actual. <paramref name="associatedData"/> ata el cifrado a su conexion.</summary>
    string Protect(IReadOnlyDictionary<string, string> secrets, string associatedData);

    /// <summary>Descifra. Falla si la clave no existe, el texto fue alterado o no es el de esa conexion.</summary>
    Dictionary<string, string> Unprotect(string cipher, string keyId, string associatedData);
}

public sealed class SecretProtectorException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    // masterKey y previousKeys vienen en base64 (32 bytes). Una clave mal formada se ignora y deja el servicio como "no configurado".
    public AesGcmSecretProtector(string? masterKey, string? masterKeyId, IReadOnlyDictionary<string, string>? previousKeys = null)
    {
        CurrentKeyId = string.IsNullOrWhiteSpace(masterKeyId) ? "k1" : masterKeyId.Trim();

        if (TryDecode(masterKey, out var current)) _keys[CurrentKeyId] = current;

        foreach (var (id, value) in previousKeys ?? new Dictionary<string, string>())
        {
            if (!_keys.ContainsKey(id) && TryDecode(value, out var old)) _keys[id] = old;
        }
    }

    public string CurrentKeyId { get; }

    public bool IsConfigured => _keys.ContainsKey(CurrentKeyId);

    public string Protect(IReadOnlyDictionary<string, string> secrets, string associatedData)
    {
        if (!IsConfigured) throw new SecretProtectorException("No hay clave maestra configurada en el servidor.");

        var plain = JsonSerializer.SerializeToUtf8Bytes(secrets);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_keys[CurrentKeyId], TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(associatedData));

        // nonce | tag | texto cifrado
        var result = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        cipher.CopyTo(result, NonceSize + TagSize);
        return Convert.ToBase64String(result);
    }

    public Dictionary<string, string> Unprotect(string cipher, string keyId, string associatedData)
    {
        if (!_keys.TryGetValue(keyId, out var key))
            throw new SecretProtectorException($"No está la clave maestra \"{keyId}\" con la que se cifraron los secretos.");

        try
        {
            var data = Convert.FromBase64String(cipher);
            if (data.Length < NonceSize + TagSize) throw new SecretProtectorException("El secreto guardado está dañado.");

            var nonce = data.AsSpan(0, NonceSize);
            var tag = data.AsSpan(NonceSize, TagSize);
            var encrypted = data.AsSpan(NonceSize + TagSize);
            var plain = new byte[encrypted.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, encrypted, tag, plain, Encoding.UTF8.GetBytes(associatedData));

            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [];
        }
        catch (CryptographicException exception)
        {
            throw new SecretProtectorException("No se pudo descifrar el secreto: la clave maestra no coincide o el dato fue alterado.", exception);
        }
        catch (FormatException exception)
        {
            throw new SecretProtectorException("El secreto guardado está dañado.", exception);
        }
    }

    private static bool TryDecode(string? base64, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(base64)) return false;
        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            if (bytes.Length != KeySize) return false;
            key = bytes;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Genera una clave maestra nueva lista para guardar en la variable de entorno.</summary>
    public static string GenerateMasterKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize));
}
