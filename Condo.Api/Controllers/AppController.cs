using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Informacion de la app Android. El APK se distribuye fuera de Play Store y no se actualiza solo: la app consulta
/// esto al abrir para saber si su version ya no es compatible con la API (minVersionCode) o si hay una nueva.
/// Se configura en appsettings / variables de entorno: App:MinVersionCode, App:LatestVersionCode, App:ApkUrl, App:Message.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/app")]
public class AppController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("version")]
    public ActionResult<AppVersionDto> GetVersion()
    {
        var message = configuration["App:Message"];

        return Ok(new AppVersionDto
        {
            MinVersionCode = configuration.GetValue("App:MinVersionCode", 1),
            LatestVersionCode = configuration.GetValue("App:LatestVersionCode", 1),
            ApkUrl = configuration["App:ApkUrl"] ?? "https://tramiya.com.py/downloads/condopy.apk",
            Message = string.IsNullOrWhiteSpace(message) ? null : message
        });
    }
}
