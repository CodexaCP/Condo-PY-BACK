using Condo.Api.Services;
using Condo.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Condo.Api.Controllers;

/// <summary>Acceso al marketplace: en que edificios lo tiene disponible el usuario final y el personal.</summary>
[ApiController]
[Authorize]
[Route("api/marketplace")]
public class MarketplaceController(MarketplaceScope scope) : ControllerBase
{
    // Edificios del usuario con el marketplace disponible. Vacio = no se muestra el acceso en la app.
    [HttpGet("buildings")]
    public async Task<ActionResult<List<MarketplaceBuildingDto>>> GetBuildings(CancellationToken cancellationToken)
    {
        var buildings = await scope.GetAvailableBuildingsAsync(cancellationToken);
        return Ok(buildings.Select(x => new MarketplaceBuildingDto
        {
            BuildingId = x.BuildingId,
            BuildingName = x.BuildingName,
            CanPublish = x.CanPublish
        }).ToList());
    }

    // Edificios del personal con el marketplace disponible y sus permisos. Vacio = no se muestran los accesos del personal.
    [HttpGet("staff-buildings")]
    public async Task<ActionResult<List<MarketplaceStaffBuildingDto>>> GetStaffBuildings(CancellationToken cancellationToken)
    {
        var buildings = await scope.GetStaffBuildingsAsync(cancellationToken);
        return Ok(buildings.Select(x => new MarketplaceStaffBuildingDto
        {
            BuildingId = x.BuildingId,
            BuildingName = x.BuildingName,
            CanReviewPayments = x.CanReviewPayments,
            CanViewAccount = x.CanViewAccount,
            CanEditAccount = x.CanEditAccount
        }).ToList());
    }
}
