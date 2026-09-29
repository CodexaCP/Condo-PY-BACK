namespace Condo.Api.Documents;

public static class TemplateImageReader
{
    // Modelos de documentos del edificio (el PDF se convierte a imagen al subirlo): solo se lee de /uploads.
    public static byte[]? Read(string webRootPath, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var fileName = Path.GetFileName(Uri.TryCreate(url, UriKind.Absolute, out var abs) ? abs.AbsolutePath : url);
        if (string.IsNullOrEmpty(fileName)) return null;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")) return null;

        var path = Path.Combine(webRootPath, "uploads", fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
