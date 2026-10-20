using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>
/// Pagos online de expensas. El propietario ve en que edificios puede pagar, abre un cobro (un edificio, comprobantes completos del mas antiguo
/// en adelante) y consulta su estado mientras espera la confirmacion del proveedor. El personal ve los cobros de su alcance; el detalle tecnico
/// (eventos del proveedor) solo lo ven el SuperAdmin y el Administrador de empresa. El dinero no pasa por Condo-PY.
/// </summary>
[ApiController]
[Authorize]
[Route("api/online-payments")]
public class OnlinePaymentsController(
    OnlinePaymentService service, ITenantContext tenantContext, IAccessScopeService accessScope, OnlinePaymentSettings settings) : ControllerBase
{
    private static readonly string[] StaffRoles = ["SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager"];

    private bool IsOwner => string.Equals(tenantContext.Role, "Owner", StringComparison.OrdinalIgnoreCase);

    private bool IsStaff => StaffRoles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase);

    // El detalle tecnico del proveedor: quien administra la empresa, no el personal de un edificio.
    private bool CanSeeEvents => tenantContext.IsSuperAdmin || (tenantContext.IsCompanyAdmin && accessScope.HasFullCompanyScope);

    private ObjectResult Problem(OnlinePaymentResult<OnlinePaymentDto> result) =>
        StatusCode(result.Status, new { error = result.Code, message = result.Message });

    // ─── Propietario ─────────────────────────────────────────────────────────

    [HttpGet("options")]
    public async Task<ActionResult<OnlinePaymentOptionsDto>> GetOptions(CancellationToken ct)
    {
        if (!IsOwner) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        return Ok(await service.GetOptionsAsync(tenantContext.UserId, companyId.Value, ct));
    }

    [HttpPost]
    public async Task<ActionResult<OnlinePaymentDto>> Create([FromBody] CreateOnlinePaymentRequest request, CancellationToken ct)
    {
        if (!IsOwner) return Forbid();
        var companyId = tenantContext.CompanyId;
        if (companyId is null) return Forbid();

        var baseUrl = string.IsNullOrWhiteSpace(settings.CallbackBaseUrl) ? $"https://{Request.Host}" : settings.CallbackBaseUrl!;
        var result = await service.CreateAsync(
            tenantContext.UserId, companyId.Value, request, baseUrl, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return result.Ok ? Ok(result.Value) : Problem(result);
    }

    // Estado del cobro: la app lo consulta cada tanto mientras el propietario paga en la pagina del proveedor.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OnlinePaymentDto>> GetById(Guid id, CancellationToken ct)
    {
        if (!IsOwner) return Forbid();

        var dto = await service.GetForOwnerAsync(id, tenantContext.UserId, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    // ─── Personal ────────────────────────────────────────────────────────────

    [HttpGet("admin")]
    public async Task<ActionResult<IReadOnlyList<OnlinePaymentDto>>> GetAll(
        [FromQuery] Guid? companyId, [FromQuery] Guid? buildingId, [FromQuery] string? status, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        if (!IsStaff) return Forbid();
        var company = tenantContext.IsSuperAdmin ? companyId : tenantContext.CompanyId;
        if (company is null) return BadRequest("Indicá la empresa.");

        var scope = await ScopeAsync(ct);
        return Ok(await service.ListAsync(company.Value, scope, buildingId, status, take, ct));
    }

    [HttpGet("admin/{id:guid}")]
    public async Task<ActionResult<OnlinePaymentDto>> GetAdmin(Guid id, [FromQuery] Guid? companyId, CancellationToken ct)
    {
        if (!IsStaff) return Forbid();
        var company = tenantContext.IsSuperAdmin ? companyId : tenantContext.CompanyId;
        if (company is null) return BadRequest("Indicá la empresa.");

        var dto = await service.GetForStaffAsync(id, company.Value, await ScopeAsync(ct), CanSeeEvents, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    // Edificios que el usuario puede ver; nulo = todos los de la empresa.
    private async Task<HashSet<Guid>?> ScopeAsync(CancellationToken ct) =>
        tenantContext.IsSuperAdmin || accessScope.HasFullCompanyScope ? null : await accessScope.GetAccessibleBuildingIdsAsync(ct);
}
