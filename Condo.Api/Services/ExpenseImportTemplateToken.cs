using System.Security.Cryptography;
using System.Text;

namespace Condo.Api.Services;

/// <summary>
/// Marca cifrada (AES-GCM) que lleva la plantilla de carga de gastos con la empresa y el edificio para los que se
/// genero: al importar se comprueba que coincidan, asi una plantilla no sirve en otro edificio ni en otra empresa.
/// La clave se deriva de la clave JWT de la configuracion (estable entre reinicios).
/// </summary>
public static class ExpenseImportTemplateToken
{
    public sealed record Payload(Guid CompanyId, Guid BuildingId);

    private const string Version = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string Create(string secret, Guid companyId, Guid buildingId)
    {
        var plain = Encoding.UTF8.GetBytes($"{Version}|{companyId:N}|{buildingId:N}");
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(DeriveKey(secret), TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    // Null si la marca no es valida (alterada, de otro sistema o con otra clave).
    public static Payload? Read(string secret, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var data = Convert.FromBase64String(token.Trim());
            if (data.Length <= NonceSize + TagSize) return null;

            var nonce = data[..NonceSize];
            var tag = data[NonceSize..(NonceSize + TagSize)];
            var cipher = data[(NonceSize + TagSize)..];
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(DeriveKey(secret), TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);

            var parts = Encoding.UTF8.GetString(plain).Split('|');
            if (parts.Length != 3 || parts[0] != Version) return null;
            if (!Guid.TryParseExact(parts[1], "N", out var companyId) || !Guid.TryParseExact(parts[2], "N", out var buildingId)) return null;

            return new Payload(companyId, buildingId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] DeriveKey(string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("condopy-expense-import|" + secret));
}
