using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Localization;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/locale")]
public class LocaleController(ITenantContext tenantContext, CompanyLocaleService localeService) : ControllerBase
{
    // Configuracion regional de la empresa del usuario (moneda con su formato, impuesto, vocabulario, tipos de documento y capacidades).
    // La sesion ya la trae al iniciar; esto sirve para refrescarla despues de editar la empresa.
    [HttpGet("me")]
    public async Task<ActionResult<CompanyLocaleDto>> GetMine(CancellationToken cancellationToken)
    {
        var locale = await localeService.GetAsync(tenantContext.CompanyId, cancellationToken);
        return Ok(locale.Dto);
    }

    // Catalogo de paises con sus valores iniciales (moneda, impuesto, zona horaria, tipos de documento). Lo usa el alta de la empresa.
    [HttpGet("countries")]
    public ActionResult<IReadOnlyList<CountryProfileDto>> GetCountries() =>
        Ok(CountryProfiles.All.Select(CountryProfileDto.From).ToList());
}
