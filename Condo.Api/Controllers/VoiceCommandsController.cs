using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Interpreta comandos de voz o texto por reglas propias. Es GET a propósito: no escribe nada
/// (las acciones reales las ejecuta el cliente tras confirmar) y así funciona con plan en solo lectura.
/// </summary>
[ApiController]
[Authorize(Roles = "SuperAdmin,CompanyAdmin,CompanyOperator,BuildingManager")]
[Route("api/voice-commands")]
public class VoiceCommandsController(ICondoDbContext dbContext, IAccessScopeService accessScope) : ControllerBase
{
    [HttpGet("interpret")]
    public async Task<ActionResult<VoiceCommandInterpretationDto>> Interpret(
        [FromQuery] string? text, [FromQuery] Guid? buildingId, CancellationToken cancellationToken)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return BadRequest("Decí o escribí un comando.");
        }

        if (text.Length > 500)
        {
            return BadRequest("El comando es demasiado largo.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var parsed = VoiceCommandParser.Parse(text, today);
        var dto = new VoiceCommandInterpretationDto { Text = text, Intent = parsed.Intent };

        Domain.Entities.Building? building = null;
        if (buildingId.HasValue)
        {
            building = await dbContext.Buildings.AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == buildingId.Value, cancellationToken);
            if (building is null)
            {
                return BadRequest("El edificio indicado no existe.");
            }

            if (!await accessScope.CanAccessBuildingAsync(building.Id, cancellationToken))
            {
                return Forbid();
            }

            dto.BuildingId = building.Id;
            dto.BuildingName = building.Name;
        }

        if (parsed.Intent == VoiceCommandIntent.Unknown)
        {
            dto.Message = parsed.MentionsPeriod
                ? "¿El período nuevo va vacío o igual al anterior? Decí, por ejemplo, «crear período vacío» o «crear período igual al anterior»."
                : "No entendí el comando. Probá con «crear período vacío», «crear período igual al anterior» o «morosos con 3 meses de atraso».";
            return Ok(dto);
        }

        if (parsed.Intent == VoiceCommandIntent.ListDelinquents)
        {
            dto.AgeAmount = parsed.AgeAmount;
            dto.AgeUnit = parsed.AgeUnit;
            dto.DueOnOrBefore = parsed.DueOnOrBefore;
            if (parsed.AgeAmount.HasValue && parsed.AgeUnit.HasValue)
            {
                var n = parsed.AgeAmount.Value;
                var label = parsed.AgeUnit.Value switch
                {
                    OverdueAgeUnit.Days => n == 1 ? "día" : "días",
                    OverdueAgeUnit.Weeks => n == 1 ? "semana" : "semanas",
                    OverdueAgeUnit.Months => n == 1 ? "mes" : "meses",
                    _ => n == 1 ? "año" : "años"
                };
                dto.Summary = $"Morosos con {n} {label} o más de atraso" + (building is null ? "" : $" en {building.Name}");
            }
            else
            {
                dto.Summary = "Todos los morosos" + (building is null ? "" : $" de {building.Name}");
            }

            dto.RequiresConfirmation = false;
            return Ok(dto);
        }

        // CreateEmptyPeriod / ClonePreviousPeriod
        if (building is null)
        {
            dto.Message = "Elegí un edificio antes de crear el período.";
            return Ok(dto);
        }

        var latest = await dbContext.ExpensePeriods.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildingId == building.Id)
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            dto.Message = $"{building.Name} todavía no tiene períodos: creá el primero desde Períodos de expensas.";
            return Ok(dto);
        }

        var (ty, tm) = latest.Month == 12 ? (latest.Year + 1, 1) : (latest.Year, latest.Month + 1);
        dto.TargetPeriodName = ExpensePeriodsController.ResolveName(ty, tm);
        dto.SourcePeriodId = latest.Id;
        dto.SourcePeriodName = latest.Name;
        dto.RequiresConfirmation = true;

        if (parsed.Intent == VoiceCommandIntent.ClonePreviousPeriod)
        {
            dto.Summary = $"Crear el período {dto.TargetPeriodName} en {building.Name} copiando los gastos e ingresos de {latest.Name}.";
        }
        else
        {
            dto.ProposedPeriod = new ExpensePeriodUpsertRequest
            {
                BuildingId = building.Id,
                Year = ty,
                Month = tm,
                Name = "",
                StartDate = latest.StartDate.AddMonths(1),
                EndDate = latest.EndDate.AddMonths(1),
                DueDate = latest.DueDate.AddMonths(1),
                LateFeeDate = latest.LateFeeDate?.AddMonths(1),
                Status = ExpensePeriodStatus.Draft,
                Notes = ""
            };
            dto.Summary = $"Crear el período {dto.TargetPeriodName} vacío (sin gastos) en {building.Name}, con vencimiento el {dto.ProposedPeriod.DueDate:dd/MM/yyyy}.";
        }

        return Ok(dto);
    }
}
