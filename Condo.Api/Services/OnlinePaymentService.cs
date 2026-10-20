using System.Data;
using System.Text.Json;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Application.Services;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Services;

/// <summary>Como termino de procesarse un aviso del proveedor. Se guarda en el registro de eventos y decide que se le responde.</summary>
public static class PaymentEventOutcomes
{
    public const string Applied = "Applied";
    public const string Simulated = "Simulated";
    public const string Duplicate = "Duplicate";
    public const string Ignored = "Ignored";
    public const string ProviderRejected = "ProviderRejected";
    public const string ReviewRequired = "ReviewRequired";
    public const string AmountMismatch = "AmountMismatch";
    public const string UnknownAttempt = "UnknownAttempt";
    public const string InvalidSignature = "InvalidSignature";
    public const string InvalidPayload = "InvalidPayload";
    public const string Failed = "Failed";
    public const string Created = "Created";
    public const string Expired = "Expired";
}

public sealed record OnlinePaymentResult<T>(bool Ok, int Status, string Code, string Message, T? Value)
{
    public static OnlinePaymentResult<T> Success(T value) => new(true, 200, string.Empty, string.Empty, value);

    public static OnlinePaymentResult<T> Fail(int status, string code, string message) => new(false, status, code, message, default);
}

/// <summary>Respuesta al proveedor por un aviso: <see cref="Success"/> es el {"status":"success"} que confirma; cualquier otra cosa le dice que no se aplico.</summary>
public sealed record WebhookResult(int StatusCode, bool Success, string? Message);

/// <summary>
/// Orquestador de cobros online: crea el cobro de un propietario y procesa los avisos del proveedor de pagos. No conoce a ningun proveedor
/// (habla con <see cref="IPaymentProvider"/>) y no liquida por su cuenta: aplica el pago con <see cref="OwnerPaymentSettlementService"/>, la misma
/// liquidacion de siempre. El dinero no pasa por Condo-PY. Un aviso se procesa UNA sola vez (clave de deduplicacion unica, transicion
/// condicional del intento e indice unico del pago por cobro) y se responde exito al proveedor recien despues de confirmar la transaccion.
/// </summary>
public class OnlinePaymentService(
    ICondoDbContext db,
    OwnerCreditService credits,
    ComprobanteService comprobantes,
    OwnerPaymentSettlementService settlement,
    InvoiceDraftService invoiceDrafts,
    PushDispatcher push,
    ISecretProtector protector,
    PaymentProviderRegistry providers,
    OnlinePaymentSettings settings,
    ILogger<OnlinePaymentService> logger)
{
    private const int MaxNumberRetries = 5;

    // ═════════════════════════════════════════════════════════════════════════
    // Opciones del propietario
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<OnlinePaymentOptionsDto> GetOptionsAsync(Guid ownerId, Guid companyId, CancellationToken ct)
    {
        await ExpireOverdueAsync(ownerId, ct);
        var options = new OnlinePaymentOptionsDto { CollectionEnabled = settings.CollectionEnabled };

        var unitIds = await credits.LoadLinkedUnitIdsAsync(ownerId, companyId, ct);
        if (unitIds.Count == 0) return options;

        var all = await comprobantes.LoadAsync(unitIds, companyId, false, ct);
        var buildingIds = all.Select(c => c.BuildingId).Distinct().ToList();
        if (buildingIds.Count == 0) return options;

        var declaredPending = await db.OwnerPayments.AsNoTracking().AnyAsync(
            x => !x.IsDeleted && x.CompanyId == companyId && x.OwnerId == ownerId
                 && (x.Status == OwnerPaymentStatus.Pending || x.Status == OwnerPaymentStatus.UnderReview), ct);
        var openItems = (await db.PaymentIntentItems.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsOpen && unitIds.Contains(x.UnitId))
                .Select(x => new { x.UnitId, x.ExpensePeriodId })
                .ToListAsync(ct))
            .Select(x => (x.UnitId, x.ExpensePeriodId)).ToHashSet();

        foreach (var buildingId in buildingIds)
        {
            var eligible = await EligibleConnectionsAsync(companyId, buildingId, ct);
            if (eligible.Count == 0) continue;

            var inBuilding = all.Where(c => c.BuildingId == buildingId).ToList();
            var option = new OnlinePaymentBuildingOptionDto
            {
                BuildingId = buildingId,
                BuildingName = inBuilding[0].BuildingName,
                Connections = eligible.Select(e => new OnlinePaymentConnectionOptionDto
                {
                    Id = e.Connection.Id,
                    Name = e.Connection.Name,
                    ProviderName = PaymentProviderCatalog.FindProvider(e.Connection.ProviderCode)?.Name ?? e.Connection.ProviderCode,
                    ProductName = PaymentProviderCatalog.FindProduct(e.Connection.ProviderCode, e.Connection.ProductCode)?.Name ?? e.Connection.ProductCode,
                    HolderName = e.Connection.HolderName ?? string.Empty,
                    HolderTaxId = e.Connection.HolderTaxId ?? string.Empty,
                    IsSimulated = IsSimulated(e.Connection, e.Config)
                }).ToList()
            };

            var running = 0m;
            foreach (var c in inBuilding)
            {
                running += c.Total;
                option.Comprobantes.Add(new OnlinePaymentComprobanteDto
                {
                    UnitId = c.UnitId, UnitCode = c.UnitCode, ExpensePeriodId = c.ExpensePeriodId, PeriodYear = c.Year, PeriodMonth = c.Month,
                    Total = c.Total, CumulativeTotal = RoundGs(running)
                });
            }

            if (declaredPending)
                option.BlockedReason = "Tenés un pago declarado pendiente de revisión. Esperá a que se resuelva antes de pagar online.";
            else if (inBuilding.Any(c => openItems.Contains((c.UnitId, c.ExpensePeriodId))))
                option.BlockedReason = "Ya tenés un pago online en curso en este edificio. Terminalo o esperá a que venza.";

            options.Buildings.Add(option);
        }

        return options;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Crear el cobro
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<OnlinePaymentResult<OnlinePaymentDto>> CreateAsync(
        Guid ownerId, Guid companyId, CreateOnlinePaymentRequest request, string callbackBaseUrl, string? ip, CancellationToken ct)
    {
        if (!settings.CollectionEnabled)
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "collection_disabled", "El pago online todavía no está habilitado.");

        await ExpireOverdueAsync(ownerId, ct);

        var unitIds = await credits.LoadLinkedUnitIdsAsync(ownerId, companyId, ct);
        var all = unitIds.Count == 0 ? [] : await comprobantes.LoadAsync(unitIds, companyId, false, ct);
        var inBuilding = all.Where(c => c.BuildingId == request.BuildingId).ToList();
        if (inBuilding.Count == 0)
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(400, "no_debt", "No tenés comprobantes pendientes en este edificio.");

        var index = inBuilding.FindIndex(c => c.UnitId == request.ThroughUnitId && c.ExpensePeriodId == request.ThroughExpensePeriodId);
        if (index < 0)
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(400, "comprobante_not_found", "El comprobante elegido no está entre tus pendientes de este edificio.");
        var chosen = inBuilding.Take(index + 1).ToList();

        if (await db.OwnerPayments.AsNoTracking().AnyAsync(
                x => !x.IsDeleted && x.CompanyId == companyId && x.OwnerId == ownerId
                     && (x.Status == OwnerPaymentStatus.Pending || x.Status == OwnerPaymentStatus.UnderReview), ct))
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "declared_payment_pending",
                "Tenés un pago declarado pendiente de revisión. Esperá a que se resuelva antes de pagar online.");

        var chosenUnits = chosen.Select(c => c.UnitId).Distinct().ToList();
        var busy = (await db.PaymentIntentItems.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsOpen && chosenUnits.Contains(x.UnitId))
                .Select(x => new { x.UnitId, x.ExpensePeriodId })
                .ToListAsync(ct))
            .Select(x => (x.UnitId, x.ExpensePeriodId)).ToHashSet();
        if (chosen.Any(c => busy.Contains((c.UnitId, c.ExpensePeriodId))))
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "payment_in_progress",
                "Ya tenés un pago online en curso por estos comprobantes. Terminalo o esperá a que venza.");

        var eligible = await EligibleConnectionsAsync(companyId, request.BuildingId, ct);
        if (eligible.Count == 0)
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "no_connection", "Este edificio no tiene pago online habilitado.");

        (PaymentConnection Connection, BuildingPaymentConfig Config) pick;
        if (request.ConnectionId.HasValue)
        {
            var found = eligible.FirstOrDefault(e => e.Connection.Id == request.ConnectionId.Value);
            if (found.Connection is null)
                return OnlinePaymentResult<OnlinePaymentDto>.Fail(400, "connection_not_available", "El medio de pago elegido no está disponible en este edificio.");
            pick = found;
        }
        else if (eligible.Count == 1)
        {
            pick = eligible[0];
        }
        else
        {
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(400, "connection_required", "Elegí con qué medio de pago querés pagar.");
        }

        var connection = pick.Connection;
        if (string.IsNullOrWhiteSpace(connection.HolderName) || string.IsNullOrWhiteSpace(connection.HolderTaxId))
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "holder_missing", "La conexión de pago no tiene cargado el titular del comercio.");

        var provider = providers.Find(connection.ProviderCode, connection.ProductCode);
        if (provider is null)
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "provider_unavailable", "El proveedor de esta conexión todavía no está disponible para cobrar.");

        var info = ReadConnectionInfo(connection);
        if (info is null)
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "secrets_unreadable", "No se pudieron leer las claves de la conexión de pago. Avisá a la administración.");

        var amount = RoundGs(chosen.Sum(c => c.Total));
        if (amount <= 0)
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(400, "no_debt", "No hay un importe para cobrar.");

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.IntentMinutes);
        var intent = new PaymentIntent
        {
            CompanyId = companyId,
            BuildingId = request.BuildingId,
            OwnerId = ownerId,
            ConnectionId = connection.Id,
            Amount = amount,
            Status = PaymentIntentStatus.Open,
            ExpiresAtUtc = expires,
            HolderName = connection.HolderName!.Trim(),
            HolderTaxId = connection.HolderTaxId!.Trim(),
            IsSimulated = IsSimulated(connection, pick.Config)
        };
        foreach (var c in chosen)
        {
            intent.Items.Add(new PaymentIntentItem
            {
                CompanyId = companyId,
                UnitId = c.UnitId,
                ExpensePeriodId = c.ExpensePeriodId,
                Amount = c.Total,
                IsOpen = true,
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    label = c.Label,
                    lines = c.Lines.Select(l => new { chargeId = l.Charge.Id, concept = l.Charge.Concept, pending = l.Pending })
                })
            });
        }

        var attempt = new PaymentAttempt
        {
            CompanyId = companyId,
            ConnectionId = connection.Id,
            Status = PaymentAttemptStatus.Created,
            Amount = amount,
            ExpiresAtUtc = expires
        };
        intent.Attempts.Add(attempt);
        db.PaymentIntents.Add(intent);

        // Los numeros salen de "maximo + 1": si dos cobros se crean a la vez y chocan, el indice unico rechaza al segundo y se reintenta.
        for (var tries = 1; ; tries++)
        {
            intent.Number = (await db.PaymentIntents.IgnoreQueryFilters().MaxAsync(x => (long?)x.Number, ct) ?? 0) + 1;
            attempt.AttemptNumber = (await db.PaymentAttempts.IgnoreQueryFilters().MaxAsync(x => (long?)x.AttemptNumber, ct) ?? 0) + 1;
            attempt.ProviderReference = attempt.AttemptNumber.ToString();
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException exception) when (IsItemConflict(exception))
            {
                db.ChangeTracker.Clear();
                return OnlinePaymentResult<OnlinePaymentDto>.Fail(409, "payment_in_progress",
                    "Ya tenés un pago online en curso por estos comprobantes. Terminalo o esperá a que venza.");
            }
            catch (DbUpdateException exception) when (tries < MaxNumberRetries && IsNumberCollision(exception))
            {
                // Las entidades siguen agregadas: alcanza con recalcular los numeros y volver a guardar.
            }
        }

        var sessionRequest = new CreateSessionRequest(
            attempt.ProviderReference, attempt.AttemptNumber, amount, intent.Currency,
            $"Expensas - {chosen[0].BuildingName} - {chosen.Count} comprobante(s)",
            $"{callbackBaseUrl.TrimEnd('/')}/api/payment-webhooks/{connection.PublicId}", expires);

        try
        {
            var session = await provider.CreateSessionAsync(info, sessionRequest, ct);
            attempt.ProviderSessionId = session.SessionId;
            attempt.CheckoutUrl = session.CheckoutUrl;
            attempt.Status = PaymentAttemptStatus.Pending;
            attempt.ExpiresAtUtc = session.ExpiresAtUtc ?? expires;
            db.PaymentEvents.Add(NewEvent(connection, attempt.Id, PaymentEventDirection.Outbound, "CreateSession", null, null, ip,
                JsonSerializer.Serialize(new { attempt = attempt.AttemptNumber, amount, session = session.SessionId }), PaymentEventOutcomes.Created, null));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "El proveedor no pudo abrir la sesion de cobro del intento {Attempt}.", attempt.AttemptNumber);
            attempt.Status = PaymentAttemptStatus.Rejected;
            attempt.ProviderStatusRaw = "CreateSessionError";
            intent.Status = PaymentIntentStatus.Closed;
            intent.CloseReason = "ProviderError";
            foreach (var item in intent.Items) item.IsOpen = false;
            db.PaymentEvents.Add(NewEvent(connection, attempt.Id, PaymentEventDirection.Outbound, "CreateSession", null, null, ip,
                null, PaymentEventOutcomes.Failed, Truncate(exception.Message, 1000)));
            await db.SaveChangesAsync(ct);
            return OnlinePaymentResult<OnlinePaymentDto>.Fail(502, "provider_error",
                "No se pudo iniciar el pago con el proveedor. Probá de nuevo en unos minutos.");
        }

        return OnlinePaymentResult<OnlinePaymentDto>.Success((await LoadDtoAsync(intent.Id, includeEvents: false, ct))!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Consultas
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<OnlinePaymentDto?> GetForOwnerAsync(Guid id, Guid ownerId, CancellationToken ct)
    {
        await ExpireOverdueAsync(ownerId, ct);
        var dto = await LoadDtoAsync(id, includeEvents: false, ct);
        return dto is not null && dto.OwnerId == ownerId ? dto : null;
    }

    /// <summary>Cobros de una empresa para el personal. <paramref name="buildingScope"/> nulo = todos los edificios.</summary>
    public async Task<List<OnlinePaymentDto>> ListAsync(
        Guid companyId, HashSet<Guid>? buildingScope, Guid? buildingId, string? status, int take, CancellationToken ct)
    {
        var query = db.PaymentIntents.AsNoTracking().Where(x => !x.IsDeleted && x.CompanyId == companyId);
        if (buildingScope is not null) query = query.Where(x => buildingScope.Contains(x.BuildingId));
        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId.Value);
        if (Enum.TryParse<PaymentIntentStatus>(status, ignoreCase: true, out var parsed)) query = query.Where(x => x.Status == parsed);

        var ids = await query.OrderByDescending(x => x.CreatedAtUtc).Take(Math.Clamp(take, 1, 200)).Select(x => x.Id).ToListAsync(ct);
        var result = new List<OnlinePaymentDto>();
        foreach (var id in ids)
        {
            var dto = await LoadDtoAsync(id, includeEvents: false, ct);
            if (dto is not null) result.Add(dto);
        }

        return result;
    }

    public async Task<OnlinePaymentDto?> GetForStaffAsync(
        Guid id, Guid companyId, HashSet<Guid>? buildingScope, bool includeEvents, CancellationToken ct)
    {
        var dto = await LoadDtoAsync(id, includeEvents, ct);
        if (dto is null) return null;
        var owns = await db.PaymentIntents.AsNoTracking().AnyAsync(x => x.Id == id && x.CompanyId == companyId, ct);
        if (!owns) return null;
        if (buildingScope is not null && !buildingScope.Contains(dto.BuildingId)) return null;
        return dto;
    }

    private async Task<OnlinePaymentDto?> LoadDtoAsync(Guid id, bool includeEvents, CancellationToken ct)
    {
        var intent = await db.PaymentIntents.AsNoTracking()
            .Include(x => x.Building)
            .Include(x => x.Owner)
            .Include(x => x.Connection)
            .Include(x => x.Items).ThenInclude(i => i.Unit)
            .Include(x => x.Items).ThenInclude(i => i.ExpensePeriod)
            .Include(x => x.Attempts)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (intent is null) return null;

        var attempt = intent.Attempts.Where(a => !a.IsDeleted).OrderByDescending(a => a.AttemptNumber).FirstOrDefault();
        string? paymentReference = null;
        if (intent.OwnerPaymentId.HasValue)
        {
            paymentReference = await db.OwnerPayments.AsNoTracking().Where(x => x.Id == intent.OwnerPaymentId.Value)
                .Select(x => x.Reference).FirstOrDefaultAsync(ct);
        }

        var dto = new OnlinePaymentDto
        {
            Id = intent.Id,
            Number = intent.Number,
            BuildingId = intent.BuildingId,
            BuildingName = intent.Building?.Name ?? string.Empty,
            OwnerId = intent.OwnerId,
            OwnerFullName = intent.Owner?.FullName ?? string.Empty,
            Amount = intent.Amount,
            Currency = intent.Currency,
            Status = intent.Status.ToString(),
            CloseReason = intent.CloseReason,
            ReviewReason = intent.ReviewReason,
            HolderName = intent.HolderName,
            HolderTaxId = intent.HolderTaxId,
            IsSimulated = intent.IsSimulated,
            ExpiresAtUtc = intent.ExpiresAtUtc,
            CreatedAtUtc = intent.CreatedAtUtc,
            ConnectionId = intent.ConnectionId,
            ConnectionName = intent.Connection?.Name ?? string.Empty,
            ConnectionProviderCode = intent.Connection?.ProviderCode ?? string.Empty,
            AttemptNumber = attempt?.AttemptNumber,
            AttemptStatus = attempt?.Status.ToString(),
            CheckoutUrl = attempt is { Status: PaymentAttemptStatus.Pending } ? attempt.CheckoutUrl : null,
            ConfirmedAtUtc = attempt?.ConfirmedAtUtc,
            ProviderTransactionId = attempt?.ProviderTransactionId,
            AuthorizationCode = attempt?.AuthorizationCode,
            CardBrand = attempt?.CardBrand,
            CardLast4 = attempt?.CardLast4,
            OwnerPaymentId = intent.OwnerPaymentId,
            OwnerPaymentReference = paymentReference,
            Items = intent.Items.Where(i => !i.IsDeleted)
                .OrderBy(i => i.ExpensePeriod?.Year).ThenBy(i => i.ExpensePeriod?.Month).ThenBy(i => i.Unit?.Code)
                .Select(i => new OnlinePaymentItemDto
                {
                    UnitId = i.UnitId, UnitCode = i.Unit?.Code ?? string.Empty, ExpensePeriodId = i.ExpensePeriodId,
                    PeriodYear = i.ExpensePeriod?.Year ?? 0, PeriodMonth = i.ExpensePeriod?.Month ?? 0, Amount = i.Amount
                }).ToList()
        };

        if (includeEvents)
        {
            var attemptIds = intent.Attempts.Select(a => a.Id).ToList();
            dto.Events = await db.PaymentEvents.AsNoTracking()
                .Where(e => e.AttemptId != null && attemptIds.Contains(e.AttemptId.Value))
                .OrderBy(e => e.ReceivedAtUtc)
                .Select(e => new OnlinePaymentEventDto
                {
                    ReceivedAtUtc = e.ReceivedAtUtc, Direction = e.Direction.ToString(), Kind = e.Kind, Outcome = e.Outcome,
                    Error = e.Error, SignatureOk = e.SignatureOk, Ip = e.Ip
                })
                .ToListAsync(ct);
        }

        return dto;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Vencimiento
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cierra los cobros abiertos cuyo plazo vencio (intento vencido, comprobantes liberados). Un aviso que llegue despues igual se aplica si
    /// todo sigue coincidiendo. Sin <paramref name="ownerId"/> revisa todos (lo usa el proceso en segundo plano).
    /// </summary>
    public async Task<int> ExpireOverdueAsync(Guid? ownerId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var query = db.PaymentIntents.Include(x => x.Items).Include(x => x.Attempts)
            .Where(x => !x.IsDeleted && x.Status == PaymentIntentStatus.Open && x.ExpiresAtUtc != null && x.ExpiresAtUtc < now);
        if (ownerId.HasValue) query = query.Where(x => x.OwnerId == ownerId.Value);

        var overdue = await query.ToListAsync(ct);
        foreach (var intent in overdue)
        {
            intent.Status = PaymentIntentStatus.Closed;
            intent.CloseReason = "Expired";
            foreach (var item in intent.Items) item.IsOpen = false;
            var live = intent.Attempts.Where(a => a.Status is PaymentAttemptStatus.Created or PaymentAttemptStatus.Pending).ToList();
            foreach (var attempt in live)
            {
                attempt.Status = PaymentAttemptStatus.Expired;
                db.PaymentEvents.Add(new PaymentEvent
                {
                    CompanyId = intent.CompanyId, ConnectionId = intent.ConnectionId, AttemptId = attempt.Id,
                    Direction = PaymentEventDirection.Internal, Kind = "Expire", Outcome = PaymentEventOutcomes.Expired
                });
            }
        }

        if (overdue.Count > 0) await db.SaveChangesAsync(ct);
        return overdue.Count;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Aviso del proveedor
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Procesa un aviso del proveedor, anonimo y sin sesion. Siempre deja un evento. Responde exito solo despues de confirmar la transaccion
    /// que aplico el pago; un aviso repetido responde igual que la primera vez y no vuelve a aplicar nada.
    /// </summary>
    public async Task<WebhookResult> HandleCallbackAsync(
        string publicId, string rawBody, IReadOnlyDictionary<string, string> headers, string? ip, CancellationToken ct)
    {
        if (publicId.Length != 32 || !publicId.All(Uri.IsHexDigit))
            return new WebhookResult(404, false, "Conexión desconocida.");

        var connection = await db.PaymentConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.PublicId == publicId, ct);
        if (connection is null || connection.Status == PaymentConnectionStatus.Draft)
            return new WebhookResult(404, false, "Conexión desconocida.");

        var provider = providers.Find(connection.ProviderCode, connection.ProductCode);
        var info = ReadConnectionInfo(connection);
        if (provider is null || info is null)
        {
            logger.LogError("No se puede procesar el aviso de la conexion {ConnectionId}: proveedor o claves no disponibles.", connection.Id);
            await RecordStandaloneEventAsync(connection, null, null, ip, null, PaymentEventOutcomes.Failed, "Proveedor o claves no disponibles.", ct);
            return new WebhookResult(500, false, "No se pudo procesar el aviso.");
        }

        var parsed = provider.ParseCallback(info, rawBody, headers);
        if (!parsed.Valid || parsed.Callback is null)
        {
            var outcome = parsed.SignatureOk == false ? PaymentEventOutcomes.InvalidSignature : PaymentEventOutcomes.InvalidPayload;
            await RecordStandaloneEventAsync(connection, null, parsed.SignatureOk, ip, $"{{\"length\":{rawBody.Length}}}", outcome, parsed.Error, ct);
            return outcome == PaymentEventOutcomes.InvalidSignature
                ? new WebhookResult(401, false, "Firma inválida.")
                : new WebhookResult(400, false, "Aviso inválido.");
        }

        var callback = parsed.Callback;
        string finalOutcome = PaymentEventOutcomes.Failed;
        PostCommitWork? postCommit = null;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                postCommit = null;
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                // Idempotencia: el mismo aviso (proveedor + transaccion + estado) se procesa una sola vez.
                var existing = await db.PaymentEvents.AsNoTracking().Where(x => x.DedupeKey == callback.DedupeKey)
                    .Select(x => x.Outcome).FirstOrDefaultAsync(ct);
                if (existing is not null)
                {
                    db.PaymentEvents.Add(NewEvent(connection, null, PaymentEventDirection.Inbound, "Callback", null, parsed.SignatureOk, ip,
                        callback.RedactedPayloadJson, PaymentEventOutcomes.Duplicate, null));
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    finalOutcome = existing;
                    return;
                }

                var outcome = await ApplyCallbackAsync(connection, callback, parsed.SignatureOk, ip, p => postCommit = p, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                finalOutcome = outcome;
            });
        }
        catch (DbUpdateException exception) when (IsDedupeConflict(exception))
        {
            // Otro aviso igual se proceso al mismo tiempo y gano: este es un duplicado.
            db.ChangeTracker.Clear();
            var existing = await db.PaymentEvents.AsNoTracking().Where(x => x.DedupeKey == callback.DedupeKey)
                .Select(x => x.Outcome).FirstOrDefaultAsync(ct);
            return ResponseFor(existing ?? PaymentEventOutcomes.Duplicate, provider.Capabilities);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Fallo al aplicar el aviso {Key} de la conexion {ConnectionId}.", callback.DedupeKey, connection.Id);
            db.ChangeTracker.Clear();
            await RecordStandaloneEventAsync(connection, null, parsed.SignatureOk, ip, callback.RedactedPayloadJson,
                PaymentEventOutcomes.Failed, Truncate(exception.Message, 1000), ct);
            return new WebhookResult(500, false, "No se pudo aplicar el aviso.");
        }

        // Despues de confirmar: avisos al propietario y a la administracion, y borradores de factura. Si algo falla aca el pago ya esta aplicado.
        if (postCommit is not null)
        {
            try
            {
                await postCommit(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "El pago online se aplico pero fallaron los avisos o los borradores de factura.");
            }
        }

        return ResponseFor(finalOutcome, provider.Capabilities);
    }

    private delegate Task PostCommitWork(CancellationToken ct);

    // Decide y aplica lo que corresponde a un aviso, dentro de la transaccion abierta. Agrega el evento con su resultado final (los eventos
    // son de solo insercion, por eso se escribe una sola vez). No guarda: lo hace quien llama.
    private async Task<string> ApplyCallbackAsync(
        PaymentConnection connection, ProviderCallback callback, bool? signatureOk, string? ip,
        Action<PostCommitWork> setPostCommit, CancellationToken ct)
    {
        var attempt = await FindAttemptAsync(connection.Id, callback, ct);
        if (attempt is null)
            return AddEvent(connection, null, callback, signatureOk, ip, PaymentEventOutcomes.UnknownAttempt, "No hay un intento de cobro para este aviso.");

        var intent = attempt.Intent!;

        switch (callback.Status)
        {
            case ProviderPaymentStatus.Pending:
                return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.Ignored, null);

            case ProviderPaymentStatus.Reversed:
                // La reversa de un cobro ya aplicado todavia no se gestiona desde Condo-PY: queda registrada para seguimiento manual.
                return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.Ignored,
                    "Reversa informada por el proveedor: se gestiona manualmente.");

            case ProviderPaymentStatus.Rejected:
                if (attempt.Status is PaymentAttemptStatus.Created or PaymentAttemptStatus.Pending)
                {
                    attempt.Status = PaymentAttemptStatus.Rejected;
                    attempt.ProviderStatusRaw = callback.StatusRaw;
                    if (intent.Status == PaymentIntentStatus.Open)
                    {
                        intent.Status = PaymentIntentStatus.Closed;
                        intent.CloseReason = "Rejected";
                        foreach (var item in intent.Items) item.IsOpen = false;
                    }
                }

                return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.ProviderRejected, null);
        }

        // ── Confirmado ──────────────────────────────────────────────────────
        if (callback.Amount != attempt.Amount || !string.Equals(callback.Currency, intent.Currency, StringComparison.OrdinalIgnoreCase))
            return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.AmountMismatch,
                $"Importe o moneda distintos: se esperaba {attempt.Amount:N0} {intent.Currency} y llegó {callback.Amount:N0} {callback.Currency}.");

        if (attempt.Status == PaymentAttemptStatus.Confirmed)
            return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.Duplicate, "El intento ya estaba confirmado.");

        // Quien gana la transicion a Confirmado es el unico que liquida: actualizacion condicional de una sola instruccion.
        var attemptId = attempt.Id;
        var won = await db.PaymentAttempts
            .Where(x => x.Id == attemptId && (x.Status == PaymentAttemptStatus.Created || x.Status == PaymentAttemptStatus.Pending
                                              || x.Status == PaymentAttemptStatus.Expired || x.Status == PaymentAttemptStatus.Cancelled))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PaymentAttemptStatus.Confirmed), ct);
        if (won == 0)
            return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.AmountMismatch,
                $"El intento estaba en estado {attempt.Status} y no admite una confirmación.");

        // La fila ya cambio en la base; se alinea la copia que EF tiene en memoria (el guardado repetiria el mismo valor).
        attempt.Status = PaymentAttemptStatus.Confirmed;
        attempt.ProviderTransactionId = callback.TransactionId;
        attempt.AuthorizationCode = callback.AuthorizationCode;
        attempt.ProviderStatusRaw = callback.StatusRaw;
        attempt.ConfirmedAtUtc = callback.ConfirmedAtUtc ?? DateTime.UtcNow;
        attempt.CardBrand = callback.CardBrand;
        attempt.CardLast4 = callback.CardLast4;

        // Un cobro de prueba (simulador o sandbox) en un edificio que no es de prueba se registra pero no toca la deuda real.
        if (intent.IsSimulated)
        {
            intent.Status = PaymentIntentStatus.Closed;
            intent.CloseReason = "Simulated";
            foreach (var item in intent.Items) item.IsOpen = false;
            return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.Simulated, null);
        }

        if (intent.Status == PaymentIntentStatus.Paid)
            return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.AmountMismatch, "El cobro ya estaba pagado.");

        // Un aviso tardio (el cobro ya habia vencido): solo se aplica si los comprobantes no estan en otro cobro abierto.
        var itemKeys = intent.Items.Where(i => !i.IsDeleted).Select(i => (i.UnitId, i.ExpensePeriodId)).ToHashSet();
        if (intent.Status != PaymentIntentStatus.Open)
        {
            var unitIds = itemKeys.Select(k => k.UnitId).Distinct().ToList();
            var others = (await db.PaymentIntentItems.AsNoTracking()
                    .Where(x => !x.IsDeleted && x.IsOpen && x.IntentId != intent.Id && unitIds.Contains(x.UnitId))
                    .Select(x => new { x.UnitId, x.ExpensePeriodId })
                    .ToListAsync(ct))
                .Select(x => (x.UnitId, x.ExpensePeriodId));
            if (others.Any(itemKeys.Contains))
                return Review(connection, attempt, intent, callback, signatureOk, ip, "Los comprobantes ya están en otro cobro abierto.", setPostCommit);
        }

        // Revalidar contra la deuda de ahora: misma cobertura exacta que cuando se abrio el cobro.
        var linked = await credits.LoadLinkedUnitIdsAsync(intent.OwnerId, intent.CompanyId, ct);
        foreach (var key in itemKeys) if (!linked.Contains(key.UnitId)) linked.Add(key.UnitId);
        var open = (await comprobantes.LoadAsync(linked, intent.CompanyId, false, ct)).Where(c => c.BuildingId == intent.BuildingId).ToList();
        var cover = ComprobanteService.Cover(intent.Amount, open);
        var covered = cover.Covered.Select(c => (c.UnitId, c.ExpensePeriodId)).ToHashSet();
        if (!cover.Exact || !itemKeys.SetEquals(covered))
        {
            var stillOwed = open.Select(c => (c.UnitId, c.ExpensePeriodId)).ToHashSet();
            var reason = itemKeys.Any(k => !stillOwed.Contains(k))
                ? "Alguno de los comprobantes ya estaba pagado."
                : "El importe de los comprobantes cambió desde que se abrió el cobro (por ejemplo, por un recargo de mora).";
            return Review(connection, attempt, intent, callback, signatureOk, ip, reason, setPostCommit);
        }

        var paymentDate = FinancePeriods.Today();
        var closed = await settlement.FindClosedForCoverageAsync(cover, paymentDate, ct);
        if (closed is not null)
            return Review(connection, attempt, intent, callback, signatureOk, ip, "El mes contable del pago ya está cerrado.", setPostCommit);

        var year = DateTime.UtcNow.Year;
        var ownerPayment = new OwnerPayment
        {
            CompanyId = intent.CompanyId,
            OwnerId = intent.OwnerId,
            PaymentDate = paymentDate,
            DeclaredAmount = intent.Amount,
            ReviewedAmount = intent.Amount,
            Status = OwnerPaymentStatus.UnderReview,
            Channel = OwnerPaymentChannel.Online,
            Method = PaymentMethod.Online,
            ExternalReference = Truncate(callback.TransactionId ?? string.Empty, 100),
            Notes = $"Cobro online {intent.Number}. Titular del comercio: {intent.HolderName}.",
            PaymentIntentId = intent.Id,
            ReviewedAt = DateTime.UtcNow,
            Reference = await settlement.NextReferenceAsync(intent.CompanyId, year, ct)
        };
        // Una fila por unidad (aunque el cobro incluya varios periodos de la misma): la liquidacion reparte el importe por unidad.
        foreach (var unitId in itemKeys.Select(k => k.UnitId).Distinct())
            ownerPayment.Units.Add(new OwnerPaymentUnit { CompanyId = intent.CompanyId, UnitId = unitId, AllocatedAmount = 0 });
        db.OwnerPayments.Add(ownerPayment);
        await settlement.SaveNewAsync(ownerPayment, intent.CompanyId, year, ct);

        await settlement.SettleAsync(ownerPayment, new SettlementContext(intent.CompanyId, null), ct);

        ownerPayment.Status = OwnerPaymentStatus.Approved;
        ownerPayment.ResolvedAt = DateTime.UtcNow;
        intent.Status = PaymentIntentStatus.Paid;
        intent.OwnerPaymentId = ownerPayment.Id;
        intent.CloseReason = null;
        foreach (var item in intent.Items) item.IsOpen = false;

        var result = AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.Applied, null);
        var actingUser = connection.StatusChangedByUserId ?? intent.OwnerId;
        var ownerPaymentId = ownerPayment.Id;
        setPostCommit(async token =>
        {
            var payment = await db.OwnerPayments.Include(x => x.Units).FirstAsync(x => x.Id == ownerPaymentId, token);
            await NotifyOwnerApprovedAsync(payment, intent.BuildingId, token);
            await invoiceDrafts.CreateDraftsFromOwnerPaymentAsync(payment, actingUser, token, systemContext: true);
        });
        return result;
    }

    // El proveedor cobro pero no se pudo aplicar: queda para revision de la administracion.
    private string Review(
        PaymentConnection connection, PaymentAttempt attempt, PaymentIntent intent, ProviderCallback callback, bool? signatureOk, string? ip,
        string reason, Action<PostCommitWork> setPostCommit)
    {
        intent.Status = PaymentIntentStatus.ReviewRequired;
        intent.ReviewReason = Truncate(reason, 500);
        foreach (var item in intent.Items) item.IsOpen = false;
        var intentId = intent.Id;
        setPostCommit(token => NotifyReviewRequiredAsync(intentId, token));
        return AddEvent(connection, attempt, callback, signatureOk, ip, PaymentEventOutcomes.ReviewRequired, reason);
    }

    private string AddEvent(
        PaymentConnection connection, PaymentAttempt? attempt, ProviderCallback callback, bool? signatureOk, string? ip, string outcome, string? error)
    {
        db.PaymentEvents.Add(NewEvent(connection, attempt?.Id, PaymentEventDirection.Inbound, "Callback", callback.DedupeKey, signatureOk, ip,
            callback.RedactedPayloadJson, outcome, error));
        return outcome;
    }

    private static WebhookResult ResponseFor(string outcome, ProviderCapabilities capabilities) => outcome switch
    {
        PaymentEventOutcomes.Applied or PaymentEventOutcomes.Simulated or PaymentEventOutcomes.Duplicate
            or PaymentEventOutcomes.Ignored or PaymentEventOutcomes.ProviderRejected => new WebhookResult(200, true, null),
        // El cobro no se pudo aplicar: si el proveedor devuelve el dinero cuando no se le responde exito, se lo rechaza para que lo devuelva;
        // si no, se acepta el aviso y la devolucion queda como tarea manual de la administracion.
        PaymentEventOutcomes.ReviewRequired => capabilities.RefundsWhenNotAcknowledged
            ? new WebhookResult(409, false, "El cobro no se pudo aplicar.")
            : new WebhookResult(200, true, null),
        PaymentEventOutcomes.UnknownAttempt => new WebhookResult(404, false, "Cobro desconocido."),
        PaymentEventOutcomes.AmountMismatch => new WebhookResult(409, false, "El aviso no coincide con el cobro."),
        PaymentEventOutcomes.InvalidSignature => new WebhookResult(401, false, "Firma inválida."),
        PaymentEventOutcomes.InvalidPayload => new WebhookResult(400, false, "Aviso inválido."),
        _ => new WebhookResult(500, false, "No se pudo procesar el aviso.")
    };

    private async Task<PaymentAttempt?> FindAttemptAsync(Guid connectionId, ProviderCallback callback, CancellationToken ct)
    {
        var sessionId = callback.SessionId;
        var reference = callback.ProviderReference;
        if (string.IsNullOrEmpty(sessionId) && string.IsNullOrEmpty(reference)) return null;

        return await db.PaymentAttempts
            .Include(x => x.Intent).ThenInclude(i => i!.Items)
            .Where(x => !x.IsDeleted && x.ConnectionId == connectionId
                        && ((sessionId != null && x.ProviderSessionId == sessionId) || (reference != null && x.ProviderReference == reference)))
            .FirstOrDefaultAsync(ct);
    }

    // Evento fuera de la transaccion del pago (aviso invalido, falla tecnica): sin clave de deduplicacion, para no bloquear un reintento.
    private async Task RecordStandaloneEventAsync(
        PaymentConnection connection, Guid? attemptId, bool? signatureOk, string? ip, string? payload, string outcome, string? error, CancellationToken ct)
    {
        try
        {
            db.ChangeTracker.Clear();
            db.PaymentEvents.Add(NewEvent(connection, attemptId, PaymentEventDirection.Inbound, "Callback", null, signatureOk, ip, payload, outcome, error));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "No se pudo registrar el evento {Outcome} de la conexion {ConnectionId}.", outcome, connection.Id);
        }
    }

    private static PaymentEvent NewEvent(
        PaymentConnection connection, Guid? attemptId, PaymentEventDirection direction, string kind, string? dedupeKey, bool? signatureOk,
        string? ip, string? payload, string outcome, string? error) => new()
    {
        CompanyId = connection.CompanyId,
        ConnectionId = connection.Id,
        AttemptId = attemptId,
        Direction = direction,
        Kind = kind,
        DedupeKey = dedupeKey is null ? null : Truncate(dedupeKey, 200),
        SignatureOk = signatureOk,
        Ip = ip is null ? null : Truncate(ip, 64),
        PayloadJson = payload,
        Outcome = outcome,
        Error = error is null ? null : Truncate(error, 1000),
        ReceivedAtUtc = DateTime.UtcNow,
        ProcessedAtUtc = DateTime.UtcNow
    };

    // ═════════════════════════════════════════════════════════════════════════
    // Avisos despues de aplicar
    // ═════════════════════════════════════════════════════════════════════════

    private async Task NotifyOwnerApprovedAsync(OwnerPayment payment, Guid buildingId, CancellationToken ct)
    {
        // El aviso de pago recibido se puede apagar por edificio (Centro de configuracion).
        if ((await NoticeRules.BuildingsWithActiveAsync(db, [buildingId], NoticeKind.PaymentReceived, ct)).Count == 0) return;

        var title = "Tu pago online fue aprobado";
        var body = $"Tu pago {payment.Reference} por Gs. {ComprobanteService.Gs(payment.DeclaredAmount)} fue aprobado. Toca para ver más detalles.";
        db.Notifications.Add(new Notification
        {
            CompanyId = payment.CompanyId, RecipientId = payment.OwnerId, Type = NotificationType.PaymentApproved,
            Title = title, Body = body, EntityType = "OwnerPayment", EntityId = payment.Id
        });
        await db.SaveChangesAsync(ct);
        await push.NotifyUserAsync(payment.OwnerId, title, body, "OwnerPayment", payment.Id, ct);
    }

    /// <summary>Avisa a la administracion de los cobros que el proveedor confirmo y no se pudieron aplicar (para que los resuelva).</summary>
    public async Task NotifyReviewRequiredAsync(Guid intentId, CancellationToken ct)
    {
        var intent = await db.PaymentIntents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == intentId, ct);
        if (intent is null) return;

        var building = await db.Buildings.AsNoTracking().Where(x => x.Id == intent.BuildingId)
            .Select(x => new { x.Id, x.CondominiumId, x.Name }).FirstAsync(ct);
        var managerIds = await db.ApplicationUsers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == intent.CompanyId && x.IsActive &&
                        ((x.Role == UserRole.CompanyAdmin && (x.CondominiumId == null || x.CondominiumId == building.CondominiumId)) ||
                         ((x.Role == UserRole.BuildingManager || x.Role == UserRole.CompanyOperator) &&
                          x.BuildingAccesses.Any(a => !a.IsDeleted && a.IsActive && a.BuildingId == building.Id))))
            .Select(x => x.Id)
            .ToListAsync(ct);

        var title = "Pago online para revisar";
        var body = $"El pago online {intent.Number} de {building.Name} por Gs. {ComprobanteService.Gs(intent.Amount)} no se pudo aplicar: {intent.ReviewReason}";
        foreach (var managerId in managerIds)
        {
            db.Notifications.Add(new Notification
            {
                CompanyId = intent.CompanyId, RecipientId = managerId, Type = NotificationType.OnlinePaymentReviewRequired,
                Title = title, Body = Truncate(body, 1000), EntityType = "PaymentIntent", EntityId = intent.Id
            });
        }

        await db.SaveChangesAsync(ct);
        await push.NotifyUsersAsync(managerIds, title, Truncate(body, 1000), "PaymentIntent", intent.Id, ct);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Auxiliares
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<List<(PaymentConnection Connection, BuildingPaymentConfig Config)>> EligibleConnectionsAsync(
        Guid companyId, Guid buildingId, CancellationToken ct)
    {
        var configs = await db.BuildingPaymentConfigs.AsNoTracking()
            .Include(x => x.Connection)
            .Where(x => !x.IsDeleted && x.IsEnabled && x.BuildingId == buildingId && x.CompanyId == companyId
                        && x.Connection != null && !x.Connection.IsDeleted && x.Connection.CompanyId == companyId
                        && x.Connection.Status == PaymentConnectionStatus.Active)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        return configs
            .Where(x => providers.Find(x.Connection!.ProviderCode, x.Connection.ProductCode) is not null)
            .Where(x => settings.FakeProviderEnabled
                        || !string.Equals(x.Connection!.ProviderCode, PaymentProviderCatalog.FakeProvider, StringComparison.OrdinalIgnoreCase))
            .Select(x => (x.Connection!, x))
            .ToList();
    }

    // Un cobro de prueba (proveedor falso o sandbox) solo se aplica a la deuda en edificios marcados como de prueba.
    private static bool IsSimulated(PaymentConnection connection, BuildingPaymentConfig config) =>
        (string.Equals(connection.ProviderCode, PaymentProviderCatalog.FakeProvider, StringComparison.OrdinalIgnoreCase)
         || connection.Environment == PaymentEnvironment.Sandbox)
        && !config.IsTest;

    private ProviderConnectionInfo? ReadConnectionInfo(PaymentConnection connection)
    {
        Dictionary<string, string> secrets = [];
        if (!string.IsNullOrEmpty(connection.SecretsCipher))
        {
            try
            {
                secrets = protector.Unprotect(connection.SecretsCipher, connection.KeyId ?? string.Empty, connection.PublicId);
            }
            catch (SecretProtectorException exception)
            {
                logger.LogError(exception, "No se pudieron descifrar los secretos de la conexion {ConnectionId}.", connection.Id);
                return null;
            }
        }

        var plain = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(connection.SettingsJson))
        {
            try
            {
                plain = JsonSerializer.Deserialize<Dictionary<string, string>>(connection.SettingsJson) ?? [];
            }
            catch (JsonException)
            {
                plain = [];
            }
        }

        return new ProviderConnectionInfo(connection.Id, connection.PublicId, connection.Environment, plain, secrets);
    }

    private static decimal RoundGs(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static bool IsItemConflict(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("PaymentIntentItems", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsNumberCollision(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? string.Empty;
        return message.Contains("PaymentIntents", StringComparison.OrdinalIgnoreCase) && message.Contains("Number", StringComparison.OrdinalIgnoreCase)
               || message.Contains("AttemptNumber", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDedupeConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? string.Empty;
        return message.Contains("DedupeKey", StringComparison.OrdinalIgnoreCase);
    }
}
