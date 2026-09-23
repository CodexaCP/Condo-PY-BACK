using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/uploads")]
public class UploadsController(IWebHostEnvironment env) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf", ".xml"];
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    [HttpPost]
    public async Task<ActionResult<UploadResultDto>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No se recibió ningún archivo.");

        if (file.Length > MaxBytes)
            return BadRequest("El archivo supera el límite de 10 MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest("Solo se permiten imágenes (jpg, png, webp, gif).");

        var uploadsPath = Path.Combine(env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsPath);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath  = Path.Combine(uploadsPath, fileName);

        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream, ct);

        // Ruta relativa (no absoluta): si se guardara con Request.Scheme/Host, el link queda atado
        // al dominio de ese momento (p.ej. un tunel de desarrollo) y se rompe para siempre si cambia.
        // Cada cliente arma la URL completa con su propio apiUrl configurado al momento de mostrarla.
        return Ok(new UploadResultDto { Url = $"/uploads/{fileName}" });
    }
}

public class UploadResultDto
{
    public string Url { get; set; } = string.Empty;
}
