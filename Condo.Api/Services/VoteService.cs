using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Error de negocio de las votaciones con el codigo HTTP que le corresponde.</summary>
public sealed record VoteError(int StatusCode, string Message)
{
    // Votacion inexistente o de un edificio sin acceso: 404 en ambos casos, para no revelar que existe.
    public static VoteError NotFound() => new(StatusCodes.Status404NotFound, "No se encontró la votación.");
    public static VoteError BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static VoteError Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static VoteError Forbidden(string message) => new(StatusCodes.Status403Forbidden, message);
}

public sealed class VoteResult<T>
{
    private VoteResult(T? value, VoteError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public VoteError? Error { get; }
    public bool Ok => Error is null;

    public static VoteResult<T> Success(T value) => new(value, null);
    public static VoteResult<T> Fail(VoteError error) => new(default, error);
    public static implicit operator VoteResult<T>(VoteError error) => Fail(error);
}

/// <summary>Resultado congelado al cerrar o anular una votacion: se guarda como JSON en <see cref="Vote.ResultJson"/>.</summary>
public sealed record VoteResultSnapshot(
    int Version,
    string Status,
    DateTime FrozenAtUtc,
    string WeightType,
    string MajorityRule,
    decimal QuorumPercentage,
    int TotalUnits,
    decimal TotalCoefficient,
    int ParticipatingUnits,
    decimal ParticipatingCoefficient,
    bool QuorumReached,
    Guid? WinningOptionId,
    bool IsTie,
    Guid? ApprovedOptionId,
    List<VoteResultSnapshotOption> Options,
    List<VoteResultSnapshotUnit> Units);

public sealed record VoteResultSnapshotOption(
    Guid Id, string Label, int DisplayOrder, int CastCount, decimal CastWeight, decimal Percentage,
    decimal PercentOfTotalUnits, decimal PercentOfTotalCoefficient, bool HasAbsoluteMajority);

public sealed record VoteResultSnapshotUnit(
    Guid UnitId, string UnitCode, decimal Coefficient, Guid? CastId, Guid? OptionId, string? OptionLabel, DateTime? CastAtUtc, string? Source);

/// <summary>
/// Votaciones del edificio: estados (Borrador -> Abierta -> Cerrada, o Anulada desde Abierta), permisos por accion, historial de votos
/// de solo insercion, resultado congelado al cerrar y control de concurrencia. El canal del propietario (Fase 3) reutiliza
/// <see cref="ApplyCastAsync"/> con <see cref="VoteCastSource.Owner"/>.
/// </summary>
public sealed class VoteService(
    ICondoDbContext db,
    IAccessScopeService accessScope,
    ITenantContext tenant,
    PushDispatcher pushDispatcher)
{
    public const int MaxOptions = 10;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxOptionLength = 200;
    public const int MaxCancelReasonLength = 500;

    // Ver y seguir las votaciones: el personal del edificio. Cargar votos presenciales: tambien el Operador.
    private static readonly string[] StaffRoles = ["SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager"];
    // Crear, editar, eliminar borrador, abrir, cerrar y anular.
    private static readonly string[] ManageRoles = ["SuperAdmin", "CompanyAdmin", "BuildingManager"];

    private const string ConcurrencyMessage =
        "La votación cambió mientras guardabas (otro usuario votó, la cerró o la editó). Actualizá la pantalla y volvé a intentar.";
    private const string DuplicateCastMessage =
        "Otro usuario registró un voto para esa unidad al mismo tiempo. Actualizá la pantalla y revisá el voto.";

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.General);

    private bool HasRole(string[] roles) => roles.Contains(tenant.Role, StringComparer.OrdinalIgnoreCase);
    public bool CanView => HasRole(StaffRoles);
    public bool CanManage => HasRole(ManageRoles);
    public bool CanCast => HasRole(StaffRoles);

    // ── Consultas ────────────────────────────────────────────────────────────

    public async Task<VoteResult<List<VoteDto>>> ListAsync(Guid? buildingId, CancellationToken ct)
    {
        if (!CanView) return VoteError.Forbidden("No tenés permiso para ver las votaciones.");

        var accessibleIds = await accessScope.GetAccessibleBuildingIdsAsync(ct);
        if (buildingId.HasValue && !accessScope.IsSuperAdmin && !accessibleIds.Contains(buildingId.Value))
            return VoteError.Forbidden("No tenés acceso a ese edificio.");

        var query = db.Votes.AsNoTracking().Where(x => !x.IsDeleted);
        if (buildingId.HasValue)
            query = query.Where(x => x.BuildingId == buildingId.Value);
        else if (!accessScope.IsSuperAdmin)
            query = query.Where(x => accessibleIds.Contains(x.BuildingId));

        var votes = await query
            .Include(x => x.Building)
            .Include(x => x.CreatedBy)
            .Include(x => x.ClosedBy)
            .Include(x => x.Options)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        // Las que no tienen resultado congelado se cuentan con el padron actual.
        var liveIds = votes.Where(v => v.ResultJson is null).Select(v => v.Id).ToList();
        var liveCasts = await db.VoteCasts.AsNoTracking()
            .Where(x => liveIds.Contains(x.VoteId))
            .Select(x => new { x.VoteId, x.UnitId, x.VoteOptionId, x.CoefficientWeight })
            .ToListAsync(ct);
        var castsByVote = liveCasts.ToLookup(x => x.VoteId);

        var buildingIds = votes.Where(v => v.ResultJson is null).Select(x => x.BuildingId).Distinct().ToList();
        var electorates = (await db.Units.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive && buildingIds.Contains(x.BuildingId))
                .Select(x => new { x.BuildingId, x.Id, x.Coefficient })
                .ToListAsync(ct))
            .ToLookup(x => x.BuildingId);

        var result = votes.Select(v =>
        {
            var dto = BaseDto(v);
            var snapshot = ReadSnapshot(v);
            if (snapshot is not null)
            {
                ApplySnapshotTotals(dto, snapshot, v);
            }
            else
            {
                var tally = VoteTally.Compute(v.WeightType, v.MajorityRule, v.QuorumPercentage,
                    v.Options.Select(o => o.Id).ToList(),
                    electorates[v.BuildingId].Select(u => new VoteTallyUnit(u.Id, u.Coefficient)).ToList(),
                    castsByVote[v.Id].Select(c => new VoteTallyCast(c.UnitId, c.VoteOptionId, c.CoefficientWeight)).ToList());
                ApplyTallyTotals(dto, tally, v);
            }
            return dto;
        }).ToList();

        return VoteResult<List<VoteDto>>.Success(result);
    }

    public async Task<VoteResult<VoteDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var vote = await db.Votes.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: false, ct);
        if (access is not null) return access;
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(id, ct));
    }

    public async Task<VoteResult<List<VoteCastEventDto>>> GetHistoryAsync(Guid id, Guid? unitId, CancellationToken ct)
    {
        var vote = await db.Votes.AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: false, ct);
        if (access is not null) return access;

        var query = db.VoteCastEvents.AsNoTracking().Where(x => x.VoteId == id);
        if (unitId.HasValue) query = query.Where(x => x.UnitId == unitId.Value);

        var events = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new VoteCastEventDto
            {
                Id = x.Id,
                UnitId = x.UnitId,
                UnitCode = x.Unit.Code,
                Action = x.Action.ToString(),
                PreviousOptionLabel = x.PreviousOption != null ? x.PreviousOption.Label : null,
                NewOptionLabel = x.NewOption != null ? x.NewOption.Label : null,
                CoefficientWeight = x.CoefficientWeight,
                Source = x.Source.ToString(),
                ActingUserName = x.ActingUser != null ? x.ActingUser.FullName : string.Empty,
                OwnerUserName = x.OwnerUser != null ? x.OwnerUser.FullName : string.Empty,
                OccurredAtUtc = x.OccurredAtUtc
            })
            .ToListAsync(ct);

        return VoteResult<List<VoteCastEventDto>>.Success(events);
    }

    // ── Borrador ─────────────────────────────────────────────────────────────

    public async Task<VoteResult<VoteDto>> CreateAsync(VoteUpsertRequest request, CancellationToken ct)
    {
        if (!CanManage) return VoteError.Forbidden("Solo el encargado o el administrador pueden crear votaciones.");
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(request.BuildingId, ct))
            return VoteError.Forbidden("No tenés acceso a ese edificio.");

        var building = await db.Buildings.AsNoTracking().AnyAsync(x => !x.IsDeleted && x.Id == request.BuildingId, ct);
        if (!building) return VoteError.BadRequest("El edificio no existe.");

        var (parsed, error) = Validate(request);
        if (error is not null) return VoteError.BadRequest(error);

        var vote = new Vote
        {
            BuildingId = request.BuildingId,
            Title = parsed!.Title,
            Description = parsed.Description,
            QuorumPercentage = request.QuorumPercentage,
            WeightType = parsed.WeightType,
            MajorityRule = parsed.MajorityRule ?? VoteMajorityRule.Simple,
            // Informativa por defecto en todos los paises hasta que un abogado confirme la validez del voto en la app.
            IsInformative = request.IsInformative ?? true,
            Status = VoteStatus.Draft,
            CreatedByUserId = tenant.UserId
        };
        for (var i = 0; i < parsed.Options.Count; i++)
            vote.Options.Add(new VoteOption { Label = parsed.Options[i], DisplayOrder = i });

        db.Votes.Add(vote);
        await db.SaveChangesAsync(ct);
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));
    }

    public async Task<VoteResult<VoteDto>> UpdateAsync(Guid id, VoteUpsertRequest request, CancellationToken ct)
    {
        var vote = await db.Votes.Include(x => x.Options).FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: true, ct);
        if (access is not null) return access;
        if (vote!.Status != VoteStatus.Draft) return VoteError.BadRequest("Solo se pueden editar votaciones en estado Borrador.");

        var (parsed, error) = Validate(request);
        if (error is not null) return VoteError.BadRequest(error);

        vote.Title = parsed!.Title;
        vote.Description = parsed.Description;
        vote.QuorumPercentage = request.QuorumPercentage;
        vote.WeightType = parsed.WeightType;
        if (parsed.MajorityRule.HasValue) vote.MajorityRule = parsed.MajorityRule.Value;
        if (request.IsInformative.HasValue) vote.IsInformative = request.IsInformative.Value;

        // En Borrador no hay votos: las opciones se reemplazan.
        db.VoteOptions.RemoveRange(vote.Options);
        vote.Options.Clear();
        for (var i = 0; i < parsed.Options.Count; i++)
            vote.Options.Add(new VoteOption { VoteId = vote.Id, Label = parsed.Options[i], DisplayOrder = i });

        var saveError = await SaveAsync(ct);
        if (saveError is not null) return saveError;
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));
    }

    public async Task<VoteResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var vote = await db.Votes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: true, ct);
        if (access is not null) return access;
        if (vote!.Status != VoteStatus.Draft) return VoteError.BadRequest("Solo se pueden eliminar votaciones en estado Borrador.");

        vote.IsDeleted = true;
        var saveError = await SaveAsync(ct);
        if (saveError is not null) return saveError;
        return VoteResult<bool>.Success(true);
    }

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    public async Task<VoteResult<VoteDto>> OpenAsync(Guid id, CancellationToken ct)
    {
        var vote = await db.Votes.Include(x => x.Options).FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: true, ct);
        if (access is not null) return access;
        if (vote!.Status != VoteStatus.Draft) return VoteError.BadRequest("La votación ya fue abierta, cerrada o anulada.");
        if (vote.Options.Count < 2) return VoteError.BadRequest("Se necesitan al menos 2 opciones para abrir la votación.");
        if (!await db.Units.AnyAsync(x => !x.IsDeleted && x.IsActive && x.BuildingId == vote.BuildingId, ct))
            return VoteError.BadRequest("El edificio no tiene unidades activas: no hay quién vote.");

        vote.Status = VoteStatus.Open;
        vote.OpenedAtUtc = DateTime.UtcNow;
        vote.OpenedByUserId = tenant.UserId;
        var saveError = await SaveAsync(ct);
        if (saveError is not null) return saveError;

        await NotifyOpenedAsync(vote, ct);
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));
    }

    public async Task<VoteResult<VoteDto>> CloseAsync(Guid id, CancellationToken ct)
    {
        var vote = await db.Votes
            .Include(x => x.Options)
            .Include(x => x.Casts)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: true, ct);
        if (access is not null) return access;
        if (vote!.Status != VoteStatus.Open) return VoteError.BadRequest("La votación no está abierta.");

        var now = DateTime.UtcNow;
        await FreezeResultAsync(vote, VoteStatus.Closed, now, ct);
        vote.Status = VoteStatus.Closed;
        vote.ClosedAtUtc = now;
        vote.ClosedByUserId = tenant.UserId;

        var saveError = await SaveAsync(ct);
        if (saveError is not null) return saveError;
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));
    }

    public async Task<VoteResult<VoteDto>> CancelAsync(Guid id, VoteCancelRequest request, CancellationToken ct)
    {
        var vote = await db.Votes
            .Include(x => x.Options)
            .Include(x => x.Casts)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: true, ct);
        if (access is not null) return access;
        if (vote!.Status != VoteStatus.Open)
            return VoteError.BadRequest("Solo se puede anular una votación abierta (un borrador se elimina; una cerrada ya no cambia).");

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0) return VoteError.BadRequest("Indicá el motivo de la anulación.");
        if (reason.Length > MaxCancelReasonLength) return VoteError.BadRequest($"El motivo no puede superar {MaxCancelReasonLength} caracteres.");

        // Se congela tambien lo que habia al anular, como constancia; una votacion anulada no tiene opcion aprobada.
        var now = DateTime.UtcNow;
        await FreezeResultAsync(vote, VoteStatus.Cancelled, now, ct);
        vote.Status = VoteStatus.Cancelled;
        vote.CancelledAtUtc = now;
        vote.CancelledByUserId = tenant.UserId;
        vote.CancelReason = reason;

        var saveError = await SaveAsync(ct);
        if (saveError is not null) return saveError;
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));
    }

    // ── Votos ────────────────────────────────────────────────────────────────

    // Voto cargado por el personal (asamblea presencial).
    public Task<VoteResult<VoteDto>> CastAsync(Guid id, VoteCastRequest request, CancellationToken ct) =>
        ApplyCastAsync(id, request.UnitId, request.VoteOptionId, VoteCastSource.Staff, ct);

    /// <summary>
    /// Registra o cambia el voto de una unidad, dejando el evento en el historial. Votar la misma opcion que ya tiene no cambia nada.
    /// Los permisos de <paramref name="source"/> Staff son los del personal; el canal del propietario valida la titularidad antes.
    /// </summary>
    public async Task<VoteResult<VoteDto>> ApplyCastAsync(Guid id, Guid unitId, Guid optionId, VoteCastSource source, CancellationToken ct)
    {
        var vote = await db.Votes
            .Include(x => x.Options)
            .Include(x => x.Casts)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: false, ct);
        if (access is not null) return access;
        if (source == VoteCastSource.Staff && !CanCast) return VoteError.Forbidden("No tenés permiso para cargar votos.");
        if (vote!.Status != VoteStatus.Open) return VoteError.BadRequest("La votación no está abierta.");

        if (vote.Options.All(x => x.Id != optionId)) return VoteError.BadRequest("Opción inválida.");

        var unit = await db.Units.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == unitId && x.BuildingId == vote.BuildingId, ct);
        if (unit is null) return VoteError.BadRequest("La unidad no pertenece al edificio de esta votación.");
        if (!unit.IsActive) return VoteError.BadRequest("La unidad está inactiva: no forma parte del padrón de la votación.");

        var existing = vote.Casts.FirstOrDefault(x => x.UnitId == unitId);
        if (existing is not null && existing.VoteOptionId == optionId)
            return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));

        var now = DateTime.UtcNow;
        var ownerId = await PrimaryOwnerIdAsync(unitId, ct);
        var previousOptionId = existing?.VoteOptionId;
        if (existing is null)
        {
            db.VoteCasts.Add(new VoteCast
            {
                VoteId = vote.Id,
                VoteOptionId = optionId,
                UnitId = unitId,
                CoefficientWeight = unit.Coefficient,
                CastAtUtc = now,
                CastByUserId = tenant.UserId,
                Source = source
            });
        }
        else
        {
            existing.VoteOptionId = optionId;
            existing.CoefficientWeight = unit.Coefficient;
            existing.CastAtUtc = now;
            existing.CastByUserId = tenant.UserId;
            existing.Source = source;
        }

        db.VoteCastEvents.Add(new VoteCastEvent
        {
            VoteId = vote.Id,
            UnitId = unitId,
            Action = existing is null ? VoteCastAction.Cast : VoteCastAction.Change,
            PreviousOptionId = previousOptionId,
            NewOptionId = optionId,
            CoefficientWeight = unit.Coefficient,
            Source = source,
            ActingUserId = tenant.UserId,
            OwnerUserId = ownerId,
            OccurredAtUtc = now
        });

        Touch(vote, now);
        var saveError = await SaveAsync(ct, castConflict: true);
        if (saveError is not null) return saveError;
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));
    }

    public async Task<VoteResult<VoteDto>> RemoveCastAsync(Guid id, Guid castId, CancellationToken ct)
    {
        var vote = await db.Votes.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);
        var access = await CheckAccessAsync(vote, manage: false, ct);
        if (access is not null) return access;
        if (!CanCast) return VoteError.Forbidden("No tenés permiso para quitar votos.");
        if (vote!.Status != VoteStatus.Open) return VoteError.BadRequest("Solo se pueden quitar votos de una votación abierta.");

        var cast = await db.VoteCasts.FirstOrDefaultAsync(x => x.Id == castId && x.VoteId == id, ct);
        if (cast is null) return VoteError.NotFound();

        var now = DateTime.UtcNow;
        db.VoteCastEvents.Add(new VoteCastEvent
        {
            VoteId = vote.Id,
            UnitId = cast.UnitId,
            Action = VoteCastAction.Remove,
            PreviousOptionId = cast.VoteOptionId,
            NewOptionId = null,
            CoefficientWeight = cast.CoefficientWeight,
            Source = VoteCastSource.Staff,
            ActingUserId = tenant.UserId,
            OwnerUserId = await PrimaryOwnerIdAsync(cast.UnitId, ct),
            OccurredAtUtc = now
        });
        db.VoteCasts.Remove(cast);

        Touch(vote, now);
        var saveError = await SaveAsync(ct, castConflict: true);
        if (saveError is not null) return saveError;
        return VoteResult<VoteDto>.Success(await LoadDetailAsync(vote.Id, ct));
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    // Primero el acceso (sin acceso = 404, no se revela si existe ni su estado); despues el rol para la accion.
    private async Task<VoteError?> CheckAccessAsync(Vote? vote, bool manage, CancellationToken ct)
    {
        if (vote is null || !CanView) return VoteError.NotFound();
        if (!accessScope.IsSuperAdmin && !await accessScope.CanAccessBuildingAsync(vote.BuildingId, ct)) return VoteError.NotFound();
        if (manage && !CanManage) return VoteError.Forbidden("Solo el encargado o el administrador pueden gestionar la votación.");
        return null;
    }

    // Cada voto modifica la votacion: con RowVersion, un voto y el cierre (o dos votos) que se cruzan no se pisan en silencio.
    private static void Touch(Vote vote, DateTime now) => vote.UpdatedAtUtc = now;

    private async Task<VoteError?> SaveAsync(CancellationToken ct, bool castConflict = false)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return VoteError.Conflict(ConcurrencyMessage);
        }
        catch (DbUpdateException) when (castConflict)
        {
            // El indice unico (votacion, unidad) frena dos primeros votos simultaneos de la misma unidad.
            return VoteError.Conflict(DuplicateCastMessage);
        }
    }

    private async Task<Guid?> PrimaryOwnerIdAsync(Guid unitId, CancellationToken ct) =>
        await db.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && x.UnitId == unitId && x.IsPrimary && x.EndDate == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.OwnerId)
            .FirstOrDefaultAsync(ct);

    private async Task<List<Unit>> LoadElectorateAsync(Guid buildingId, CancellationToken ct) =>
        await db.Units.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.BuildingId == buildingId)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);

    // Congela el conteo con el padron de este momento y guarda el JSON con su hash.
    private async Task FreezeResultAsync(Vote vote, VoteStatus status, DateTime now, CancellationToken ct)
    {
        var electorate = await LoadElectorateAsync(vote.BuildingId, ct);
        var options = vote.Options.OrderBy(o => o.DisplayOrder).ToList();
        var tally = Tally(vote, options, electorate, vote.Casts);
        var castByUnit = vote.Casts.ToDictionary(x => x.UnitId);
        var labels = options.ToDictionary(o => o.Id, o => o.Label);
        var cancelled = status == VoteStatus.Cancelled;

        var snapshot = new VoteResultSnapshot(
            Version: 1,
            Status: status.ToString(),
            FrozenAtUtc: now,
            WeightType: vote.WeightType.ToString(),
            MajorityRule: vote.MajorityRule.ToString(),
            QuorumPercentage: vote.QuorumPercentage,
            TotalUnits: tally.TotalUnits,
            TotalCoefficient: tally.TotalCoefficient,
            ParticipatingUnits: tally.ParticipatingUnits,
            ParticipatingCoefficient: tally.ParticipatingCoefficient,
            QuorumReached: tally.QuorumReached,
            WinningOptionId: cancelled ? null : tally.WinningOptionId,
            IsTie: !cancelled && tally.IsTie,
            ApprovedOptionId: cancelled ? null : tally.ApprovedOptionId,
            Options: options.Select(o =>
            {
                var t = tally.Options.First(x => x.OptionId == o.Id);
                return new VoteResultSnapshotOption(o.Id, o.Label, o.DisplayOrder, t.CastCount, t.CastWeight, t.Percentage,
                    t.PercentOfTotalUnits, t.PercentOfTotalCoefficient, t.HasAbsoluteMajority);
            }).ToList(),
            Units: electorate.Select(u =>
            {
                castByUnit.TryGetValue(u.Id, out var cast);
                return new VoteResultSnapshotUnit(u.Id, u.Code, u.Coefficient, cast?.Id, cast?.VoteOptionId,
                    cast is null ? null : labels.GetValueOrDefault(cast.VoteOptionId), cast?.CastAtUtc, cast?.Source.ToString());
            }).ToList());

        var json = JsonSerializer.Serialize(snapshot, SnapshotJson);
        vote.ResultJson = json;
        vote.ResultHash = ComputeHash(json);
        vote.ResultTotalUnits = tally.TotalUnits;
        vote.ResultTotalCoefficient = tally.TotalCoefficient;
        vote.ResultParticipatingUnits = tally.ParticipatingUnits;
        vote.ResultParticipatingCoefficient = tally.ParticipatingCoefficient;
    }

    public static string ComputeHash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static VoteResultSnapshot? ReadSnapshot(Vote vote) =>
        vote.ResultJson is null ? null : JsonSerializer.Deserialize<VoteResultSnapshot>(vote.ResultJson, SnapshotJson);

    private static VoteTallyResult Tally(Vote vote, IEnumerable<VoteOption> options, IEnumerable<Unit> electorate, IEnumerable<VoteCast> casts) =>
        VoteTally.Compute(vote.WeightType, vote.MajorityRule, vote.QuorumPercentage,
            options.Select(o => o.Id).ToList(),
            electorate.Select(u => new VoteTallyUnit(u.Id, u.Coefficient)).ToList(),
            casts.Select(c => new VoteTallyCast(c.UnitId, c.VoteOptionId, c.CoefficientWeight)).ToList());

    private VoteDto BaseDto(Vote v) => new()
    {
        Id = v.Id,
        BuildingId = v.BuildingId,
        BuildingName = v.Building?.Name ?? string.Empty,
        Title = v.Title,
        Description = v.Description,
        QuorumPercentage = v.QuorumPercentage,
        WeightType = v.WeightType.ToString(),
        MajorityRule = v.MajorityRule.ToString(),
        IsInformative = v.IsInformative,
        Status = v.Status.ToString(),
        OpenedAtUtc = v.OpenedAtUtc,
        ClosedAtUtc = v.ClosedAtUtc,
        ClosedByName = v.ClosedBy?.FullName ?? string.Empty,
        CancelledAtUtc = v.CancelledAtUtc,
        CancelReason = v.CancelReason,
        CreatedByName = v.CreatedBy?.FullName ?? string.Empty,
        CreatedAtUtc = v.CreatedAtUtc,
        ResultHash = v.ResultHash,
        CanManage = CanManage,
        CanCast = CanCast
    };

    private static void ApplySnapshotTotals(VoteDto dto, VoteResultSnapshot s, Vote v)
    {
        dto.ResultFrozen = true;
        dto.TotalUnits = s.TotalUnits;
        dto.TotalCoefficient = s.TotalCoefficient;
        dto.ParticipatingUnits = s.ParticipatingUnits;
        dto.ParticipatingCoefficient = s.ParticipatingCoefficient;
        dto.QuorumReached = s.QuorumReached;
        dto.WinningOptionId = s.WinningOptionId;
        dto.IsTie = s.IsTie;
        dto.ApprovedOptionId = v.Status == VoteStatus.Cancelled ? null : s.ApprovedOptionId;
    }

    private static void ApplyTallyTotals(VoteDto dto, VoteTallyResult t, Vote v)
    {
        var cancelled = v.Status == VoteStatus.Cancelled;
        dto.ResultFrozen = false;
        dto.TotalUnits = t.TotalUnits;
        dto.TotalCoefficient = t.TotalCoefficient;
        dto.ParticipatingUnits = t.ParticipatingUnits;
        dto.ParticipatingCoefficient = t.ParticipatingCoefficient;
        dto.QuorumReached = t.QuorumReached;
        dto.WinningOptionId = cancelled ? null : t.WinningOptionId;
        dto.IsTie = !cancelled && t.IsTie;
        dto.ApprovedOptionId = cancelled ? null : t.ApprovedOptionId;
    }

    private async Task<VoteDto> LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var vote = await db.Votes
            .AsNoTracking()
            .Include(x => x.Building)
            .Include(x => x.Options)
            .Include(x => x.Casts).ThenInclude(c => c.Option)
            .Include(x => x.CreatedBy)
            .Include(x => x.ClosedBy)
            .FirstAsync(x => x.Id == id, ct);

        var historyCounts = await db.VoteCastEvents.AsNoTracking()
            .Where(x => x.VoteId == id)
            .GroupBy(x => x.UnitId)
            .Select(g => new { UnitId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UnitId, x => x.Count, ct);

        var dto = BaseDto(vote);
        var snapshot = ReadSnapshot(vote);
        if (snapshot is not null)
        {
            ApplySnapshotTotals(dto, snapshot, vote);
            dto.Options = snapshot.Options.OrderBy(o => o.DisplayOrder).Select(o => new VoteOptionDto
            {
                Id = o.Id, Label = o.Label, DisplayOrder = o.DisplayOrder, CastCount = o.CastCount, CastWeight = o.CastWeight,
                Percentage = o.Percentage, PercentOfTotalUnits = o.PercentOfTotalUnits, PercentOfTotalCoefficient = o.PercentOfTotalCoefficient,
                HasAbsoluteMajority = o.HasAbsoluteMajority
            }).ToList();
            dto.Units = snapshot.Units.Select(u => new VoteUnitSummaryDto
            {
                UnitId = u.UnitId, UnitCode = u.UnitCode, Coefficient = u.Coefficient, CastId = u.CastId, VotedOptionId = u.OptionId,
                VotedOptionLabel = u.OptionLabel, CastAtUtc = u.CastAtUtc, CastSource = u.Source,
                HistoryCount = historyCounts.GetValueOrDefault(u.UnitId)
            }).ToList();
            return dto;
        }

        var electorate = await LoadElectorateAsync(vote.BuildingId, ct);
        var options = vote.Options.OrderBy(o => o.DisplayOrder).ToList();
        var tally = Tally(vote, options, electorate, vote.Casts);
        ApplyTallyTotals(dto, tally, vote);

        dto.Options = options.Select(o =>
        {
            var t = tally.Options.First(x => x.OptionId == o.Id);
            return new VoteOptionDto
            {
                Id = o.Id, Label = o.Label, DisplayOrder = o.DisplayOrder, CastCount = t.CastCount, CastWeight = t.CastWeight,
                Percentage = t.Percentage, PercentOfTotalUnits = t.PercentOfTotalUnits, PercentOfTotalCoefficient = t.PercentOfTotalCoefficient,
                HasAbsoluteMajority = t.HasAbsoluteMajority
            };
        }).ToList();

        var castByUnit = vote.Casts.ToDictionary(x => x.UnitId);
        dto.Units = electorate.Select(u =>
        {
            castByUnit.TryGetValue(u.Id, out var cast);
            return new VoteUnitSummaryDto
            {
                UnitId = u.Id, UnitCode = u.Code, Coefficient = u.Coefficient,
                CastId = cast?.Id, VotedOptionId = cast?.VoteOptionId, VotedOptionLabel = cast?.Option?.Label, CastAtUtc = cast?.CastAtUtc,
                CastSource = cast?.Source.ToString(),
                HistoryCount = historyCounts.GetValueOrDefault(u.Id)
            };
        }).ToList();
        return dto;
    }

    private sealed record ParsedVote(string Title, string? Description, VoteWeightType WeightType, VoteMajorityRule? MajorityRule, List<string> Options);

    private static bool TryParseName<T>(string? value, out T result) where T : struct, Enum
    {
        result = default;
        // Solo los nombres exactos: Enum.TryParse aceptaria tambien numeros ("1") y mayusculas distintas.
        return value is not null && Enum.GetNames<T>().Contains(value) && Enum.TryParse(value, out result);
    }

    private static (ParsedVote? Parsed, string? Error) Validate(VoteUpsertRequest r)
    {
        var title = r.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return (null, "El título es obligatorio.");
        if (title.Length > MaxTitleLength) return (null, $"El título no puede superar {MaxTitleLength} caracteres.");

        var description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
            return (null, $"La descripción no puede superar {MaxDescriptionLength} caracteres.");

        if (r.QuorumPercentage < 1 || r.QuorumPercentage > 100) return (null, "El quórum debe estar entre 1 y 100.");
        if (decimal.Round(r.QuorumPercentage, 2) != r.QuorumPercentage) return (null, "El quórum admite como máximo 2 decimales.");

        if (!TryParseName<VoteWeightType>(r.WeightType, out var weightType)) return (null, "Tipo de peso inválido.");

        VoteMajorityRule? majorityRule = null;
        if (r.MajorityRule is not null)
        {
            if (!TryParseName<VoteMajorityRule>(r.MajorityRule, out var rule)) return (null, "Regla de mayoría inválida.");
            majorityRule = rule;
        }

        var labels = (r.OptionLabels ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        if (labels.Count < 2) return (null, "Se necesitan al menos 2 opciones.");
        if (labels.Count > MaxOptions) return (null, $"Se admiten como máximo {MaxOptions} opciones.");
        if (labels.Any(x => x.Length > MaxOptionLength)) return (null, $"Cada opción puede tener como máximo {MaxOptionLength} caracteres.");
        if (labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Count) return (null, "Hay opciones repetidas: cada opción tiene que ser distinta.");

        return (new ParsedVote(title, description, weightType, majorityRule, labels), null);
    }

    private async Task NotifyOpenedAsync(Vote vote, CancellationToken ct)
    {
        var companyId = await db.Buildings.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == vote.BuildingId)
            .Select(x => x.CompanyId ?? (x.Condominium != null ? x.Condominium.CompanyId : null))
            .FirstOrDefaultAsync(ct);
        if (companyId is null) return;

        // Solo los propietarios vigentes (los dados de baja por cambio de titular ya no reciben el aviso).
        var ownerIds = await db.UnitOwners.AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == vote.BuildingId)
            .Select(x => x.OwnerId)
            .ToListAsync(ct);
        var residentUserIds = await db.UnitResidents.AsNoTracking()
            .Where(x => !x.IsDeleted && x.EndDate == null
                     && x.Unit != null && !x.Unit.IsDeleted && x.Unit.BuildingId == vote.BuildingId
                     && x.Resident != null && !x.Resident.IsDeleted && x.Resident.ApplicationUserId != null)
            .Select(x => x.Resident!.ApplicationUserId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var recipientIds = ownerIds.Concat(residentUserIds).Distinct().ToList();
        if (recipientIds.Count == 0) return;

        const string title = "Nueva votación abierta";
        foreach (var rid in recipientIds)
        {
            db.Notifications.Add(new Notification
            {
                CompanyId = companyId.Value,
                RecipientId = rid,
                Type = NotificationType.VoteOpened,
                Title = title,
                Body = vote.Title,
                EntityType = "Vote",
                EntityId = vote.Id
            });
        }
        await db.SaveChangesAsync(ct);
        await pushDispatcher.NotifyUsersAsync(recipientIds, title, vote.Title, "Vote", vote.Id, ct);
    }
}
