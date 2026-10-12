using System.Text.Json;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Condo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Configuracion de las conexiones de pago online de la empresa (Centro de configuracion, seccion Pagos online): credenciales del comercio
/// en el proveedor (por ahora Bancard Tpago y vPOS) y los edificios en los que se usan. La empresa es la titular del comercio, asi que
/// la configuran el SuperAdmin y el Administrador de toda la empresa; el Operador y el Encargado solo ven el estado. Las claves secretas se
/// guardan cifradas, son de solo escritura (nunca se devuelven) y cada cambio queda en el historial. Esta etapa NO cobra nada: deja la
/// conexion lista y validada para cuando se habilite el cobro.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payment-connections")]
public class PaymentConnectionsController(
    ICondoDbContext dbContext, ITenantContext tenantContext, IAccessScopeService accessScope,
    ISecretProtector protector, IConfiguration configuration, ILogger<PaymentConnectionsController> logger) : ControllerBase
{
    /// <summary>El cobro online todavia no existe en la plataforma: se actualiza cuando se habilite.</summary>
    public const bool CollectionEnabled = false;

    private const int NameMaxLength = 120;
    private const int HistoryLimit = 200;

    private static readonly string[] ViewRoles = ["SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private bool CanView => ViewRoles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase);

    // Las claves son de la empresa entera: solo el SuperAdmin y el Administrador sin acotar a un condominio las tocan.
    private bool CanEdit => tenantContext.IsSuperAdmin || (tenantContext.IsCompanyAdmin && accessScope.HasFullCompanyScope);

    private ObjectResult Forbidden(string message = "Solo el Superadministrador y el Administrador de toda la empresa pueden configurar los pagos.") =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "payments_forbidden", message });

    private ConflictObjectResult Problem409(string error, string message) => Conflict(new { error, message });

    // La empresa sobre la que se opera: la del usuario; el SuperAdmin indica una.
    private Guid? EffectiveCompanyId(Guid? requested) => tenantContext.IsSuperAdmin ? requested : tenantContext.CompanyId;

    // ─── Catalogo ────────────────────────────────────────────────────────────

    [HttpGet("catalog")]
    public ActionResult<PaymentCatalogDto> GetCatalog()
    {
        if (!CanView) return Forbidden("Tu rol no puede ver la configuración de pagos.");

        return Ok(new PaymentCatalogDto
        {
            MasterKeyConfigured = protector.IsConfigured,
            Providers = PaymentProviderCatalog.All.Select(p => new PaymentProviderDto
            {
                Code = p.Code,
                Name = p.Name,
                Products = p.Products.Select(x => new PaymentProductDto
                {
                    Code = x.Code,
                    Name = x.Name,
                    Description = x.Description,
                    Fields = x.Fields.Select(f => new PaymentFieldDto
                    {
                        Key = f.Key, Label = f.Label, Secret = f.Secret, Required = f.Required, MaxLength = f.MaxLength, Help = f.Help
                    }).ToList(),
                    SignedCallback = x.Capabilities.SignedCallback,
                    SupportsReversal = x.Capabilities.SupportsReversal,
                    ReversalNote = x.Capabilities.ReversalNote,
                    EmbeddedCheckout = x.Capabilities.EmbeddedCheckout,
                    Notes = x.Capabilities.Notes.ToList()
                }).ToList()
            }).ToList()
        });
    }

    // ─── Listado ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<PaymentConnectionListDto>> GetAll([FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        if (!CanView) return Forbidden("Tu rol no puede ver la configuración de pagos.");

        var company = EffectiveCompanyId(companyId);
        if (!company.HasValue) return BadRequest("Indicá la empresa.");

        var canEdit = CanEdit;
        var connections = await QueryConnections(company.Value).OrderBy(x => x.Name).ToListAsync(cancellationToken);

        // Quien no edita ve solo las conexiones con algun edificio de su alcance, y solo esos edificios.
        HashSet<Guid>? scope = null;
        if (!canEdit && !accessScope.HasFullCompanyScope && !tenantContext.IsSuperAdmin)
            scope = await accessScope.GetAccessibleBuildingIdsAsync(cancellationToken);

        var items = new List<PaymentConnectionDto>();
        foreach (var connection in connections)
        {
            var dto = ToDto(connection, canEdit, scope);
            if (!canEdit && scope is not null && dto.Buildings.Count == 0) continue;
            items.Add(dto);
        }

        return Ok(new PaymentConnectionListDto
        {
            Items = items,
            CanEdit = canEdit,
            MasterKeyConfigured = protector.IsConfigured,
            CollectionEnabled = CollectionEnabled
        });
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<PaymentConnectionAuditDto>>> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden("Solo quienes configuran los pagos ven su historial.");

        var connection = await FindAsync(id, tracking: false, cancellationToken);
        if (connection is null) return NotFound();

        var items = await dbContext.PaymentConnectionAudits.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ConnectionId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(HistoryLimit)
            .Select(x => new PaymentConnectionAuditDto
            {
                Id = x.Id, CreatedAtUtc = x.CreatedAtUtc, Action = x.Action, Summary = x.Summary,
                ChangesJson = x.ChangesJson, UserEmail = x.UserEmail, UserRole = x.UserRole
            })
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    // ─── Alta y edicion ──────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<PaymentConnectionDto>> Create([FromBody] PaymentConnectionRequest request, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var company = EffectiveCompanyId(request.CompanyId);
        if (!company.HasValue) return BadRequest("Indicá la empresa.");
        if (tenantContext.IsSuperAdmin
            && !await dbContext.Companies.AsNoTracking().AnyAsync(x => x.Id == company.Value && !x.IsDeleted, cancellationToken))
            return BadRequest("La empresa indicada no existe.");

        var product = PaymentProviderCatalog.FindProduct(request.ProviderCode, request.ProductCode);
        if (product is null) return BadRequest("Elegí un proveedor y una modalidad de la lista.");

        var validation = Validate(request, product, out var environment);
        if (validation is not null) return BadRequest(validation);

        var name = request.Name.Trim();
        if (await NameTakenAsync(company.Value, name, null, cancellationToken))
            return Conflict(new { error = "payments_name_taken", message = "Ya hay una conexión con ese nombre en la empresa." });

        var provider = PaymentProviderCatalog.FindProvider(request.ProviderCode)!;
        var entity = new PaymentConnection
        {
            CompanyId = company.Value,
            Name = name,
            ProviderCode = provider.Code,
            ProductCode = product.Code,
            Environment = environment,
            Status = PaymentConnectionStatus.Draft
        };

        var settings = CleanValues(request.Settings, product, secret: false);
        entity.SettingsJson = settings.Count > 0 ? JsonSerializer.Serialize(settings) : null;

        var secrets = CleanValues(request.Secrets, product, secret: true);
        var changes = new List<ConfigChange>
        {
            new("name", "Nombre", null, name),
            new("product", "Proveedor", null, $"{provider.Name} · {product.Name}"),
            new("environment", "Ambiente", null, EnvironmentLabel(environment))
        };
        foreach (var (key, value) in settings) changes.Add(new ConfigChange(key, FieldLabel(product, key), null, value));

        if (secrets.Count > 0)
        {
            var secretError = ApplySecrets(entity, product, new Dictionary<string, string>(), secrets, changes);
            if (secretError is not null) return secretError;
        }

        entity.Status = IsComplete(entity, product) ? PaymentConnectionStatus.PendingValidation : PaymentConnectionStatus.Draft;
        entity.StatusChangedAtUtc = DateTime.UtcNow;
        entity.StatusChangedByUserId = tenantContext.UserId;

        dbContext.PaymentConnections.Add(entity);
        AddAudit(entity, "Created", $"Se creó la conexión «{name}» ({provider.Name} · {product.Name}, {EnvironmentLabel(environment)}).", changes);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), canEdit: true, scope: null));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PaymentConnectionDto>> Update(Guid id, [FromBody] PaymentConnectionRequest request, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var entity = await FindAsync(id, tracking: true, cancellationToken);
        if (entity is null) return NotFound();

        var product = PaymentProviderCatalog.FindProduct(entity.ProviderCode, entity.ProductCode);
        if (product is null) return Problem409("payments_unknown_product", "El proveedor de esta conexión ya no está en el catálogo.");

        // El proveedor y la modalidad se fijan al crear: otra modalidad pide otras credenciales, se crea una conexión nueva.
        if (!string.IsNullOrWhiteSpace(request.ProviderCode) &&
            (!string.Equals(request.ProviderCode, entity.ProviderCode, StringComparison.OrdinalIgnoreCase)
             || !string.Equals(request.ProductCode, entity.ProductCode, StringComparison.OrdinalIgnoreCase)))
            return BadRequest("El proveedor y la modalidad no se cambian: creá otra conexión.");

        var validation = Validate(request, product, out var environment);
        if (validation is not null) return BadRequest(validation);

        var name = request.Name.Trim();
        if (await NameTakenAsync(entity.CompanyId, name, entity.Id, cancellationToken))
            return Conflict(new { error = "payments_name_taken", message = "Ya hay una conexión con ese nombre en la empresa." });

        var changes = new List<ConfigChange>();
        var credentialsChanged = false;

        if (entity.Name != name)
        {
            changes.Add(new ConfigChange("name", "Nombre", entity.Name, name));
            entity.Name = name;
        }

        if (entity.Environment != environment)
        {
            changes.Add(new ConfigChange("environment", "Ambiente", EnvironmentLabel(entity.Environment), EnvironmentLabel(environment)));
            entity.Environment = environment;
            credentialsChanged = true;
        }

        // Datos no secretos: lo que llega pisa; un valor vacio borra el dato.
        var current = ReadValues(entity.SettingsJson);
        var incoming = CleanValues(request.Settings, product, secret: false, keepEmpty: true);
        foreach (var field in product.Fields.Where(f => !f.Secret))
        {
            if (!incoming.TryGetValue(field.Key, out var value)) continue;
            current.TryGetValue(field.Key, out var before);
            if (string.IsNullOrEmpty(value)) current.Remove(field.Key); else current[field.Key] = value;
            if ((before ?? string.Empty) != value)
            {
                changes.Add(new ConfigChange(field.Key, field.Label, before, string.IsNullOrEmpty(value) ? null : value));
                credentialsChanged = true;
            }
        }
        entity.SettingsJson = current.Count > 0 ? JsonSerializer.Serialize(current) : null;

        var secrets = CleanValues(request.Secrets, product, secret: true);
        if (secrets.Count > 0)
        {
            var existing = ReadSecrets(entity, out var readError);
            if (readError is not null) return readError;

            // Una clave que llega igual a la guardada no es un cambio (por ejemplo, si el formulario la reenvia).
            foreach (var key in secrets.Keys.ToList())
            {
                if (existing.TryGetValue(key, out var stored) && stored == secrets[key]) secrets.Remove(key);
            }

            if (secrets.Count > 0)
            {
                var secretError = ApplySecrets(entity, product, existing, secrets, changes);
                if (secretError is not null) return secretError;
                credentialsChanged = true;
            }
        }

        if (changes.Count == 0) return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), canEdit: true, scope: null));

        // Una conexion activa que cambia de credenciales o de ambiente vuelve a validacion: se activa de nuevo a proposito.
        var statusNote = string.Empty;
        var newStatus = entity.Status;
        if (!IsComplete(entity, product))
        {
            if (entity.Status is PaymentConnectionStatus.PendingValidation or PaymentConnectionStatus.Active) newStatus = PaymentConnectionStatus.Draft;
        }
        else if (entity.Status == PaymentConnectionStatus.Draft)
        {
            newStatus = PaymentConnectionStatus.PendingValidation;
        }
        else if (entity.Status == PaymentConnectionStatus.Active && credentialsChanged)
        {
            newStatus = PaymentConnectionStatus.PendingValidation;
        }

        if (newStatus != entity.Status)
        {
            statusNote = $" Pasó de {StatusLabel(entity.Status)} a {StatusLabel(newStatus)}.";
            SetStatus(entity, newStatus);
        }

        var labels = string.Join(", ", changes.Select(x => x.Label));
        AddAudit(entity, secrets.Count > 0 ? "SecretsReplaced" : "Updated", $"Se editó la conexión «{entity.Name}»: cambió {labels}.{statusNote}", changes);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), canEdit: true, scope: null));
    }

    // ─── Edificios ───────────────────────────────────────────────────────────

    [HttpPut("{id:guid}/buildings")]
    public async Task<ActionResult<PaymentConnectionDto>> SetBuildings(
        Guid id, [FromBody] PaymentConnectionBuildingsRequest request, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var entity = await FindAsync(id, tracking: true, cancellationToken);
        if (entity is null) return NotFound();

        var wanted = request.BuildingIds.Distinct().ToList();

        // Regla de integridad: la conexion y el edificio son de la misma empresa.
        var companyBuildings = await dbContext.Buildings.AsNoTracking()
            .Where(x => !x.IsDeleted && (x.CompanyId == entity.CompanyId || (x.Condominium != null && x.Condominium.CompanyId == entity.CompanyId)))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        var names = companyBuildings.ToDictionary(x => x.Id, x => x.Name);
        if (wanted.Any(b => !names.ContainsKey(b)))
            return BadRequest("Algún edificio no existe o no es de la empresa de la conexión.");

        var active = entity.BuildingConfigs.Where(x => !x.IsDeleted).ToList();
        var toAdd = wanted.Where(b => active.All(x => x.BuildingId != b)).ToList();
        var toRemove = active.Where(x => !wanted.Contains(x.BuildingId)).ToList();
        if (toAdd.Count == 0 && toRemove.Count == 0) return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), canEdit: true, scope: null));

        var nextOrder = active.Count == 0 ? 1 : active.Max(x => x.SortOrder) + 1;
        foreach (var buildingId in toAdd)
        {
            dbContext.BuildingPaymentConfigs.Add(new BuildingPaymentConfig
            {
                CompanyId = entity.CompanyId, BuildingId = buildingId, ConnectionId = entity.Id, SortOrder = nextOrder++
            });
        }

        foreach (var link in toRemove)
        {
            link.IsDeleted = true;
            link.UpdatedAtUtc = DateTime.UtcNow;
        }

        var changes = new List<ConfigChange>();
        foreach (var b in toAdd) changes.Add(new ConfigChange("building", "Edificio", null, names[b]));
        foreach (var l in toRemove) changes.Add(new ConfigChange("building", "Edificio", names.GetValueOrDefault(l.BuildingId, l.BuildingId.ToString()), null));

        // Una conexion activa que se queda sin edificios ya no sirve a nadie: vuelve a validacion.
        var statusNote = string.Empty;
        if (entity.Status == PaymentConnectionStatus.Active && wanted.Count == 0)
        {
            SetStatus(entity, PaymentConnectionStatus.PendingValidation);
            statusNote = " Se quedó sin edificios y volvió a validación.";
        }

        AddAudit(entity, "Assigned",
            $"Edificios de la conexión «{entity.Name}»: {(toAdd.Count > 0 ? $"se agregaron {toAdd.Count}" : "no se agregaron")}, {(toRemove.Count > 0 ? $"se quitaron {toRemove.Count}" : "no se quitaron")}.{statusNote}",
            changes);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), canEdit: true, scope: null));
    }

    // ─── Estado ──────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<PaymentConnectionDto>> Activate(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var entity = await FindAsync(id, tracking: true, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.Status == PaymentConnectionStatus.Active) return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), true, null));

        var product = PaymentProviderCatalog.FindProduct(entity.ProviderCode, entity.ProductCode);
        if (product is null) return Problem409("payments_unknown_product", "El proveedor de esta conexión ya no está en el catálogo.");

        var missing = MissingFields(entity, product);
        if (missing.Count > 0)
            return Problem409("payments_incomplete", $"Faltan datos para activar la conexión: {string.Join(", ", missing)}.");

        if (!entity.BuildingConfigs.Any(x => !x.IsDeleted && x.IsEnabled))
            return Problem409("payments_no_buildings", "Asigná al menos un edificio antes de activar la conexión.");

        // Las claves tienen que poder leerse con la clave maestra del servidor: si no, el cobro fallaria recien al intentar pagar.
        ReadSecrets(entity, out var readError);
        if (readError is not null) return readError;

        var before = entity.Status;
        SetStatus(entity, PaymentConnectionStatus.Active);
        AddAudit(entity, "Activated", $"Se activó la conexión «{entity.Name}» ({EnvironmentLabel(entity.Environment)}). Estaba {StatusLabel(before)}.");
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), canEdit: true, scope: null));
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<ActionResult<PaymentConnectionDto>> Disable(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var entity = await FindAsync(id, tracking: true, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.Status == PaymentConnectionStatus.Disabled) return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), true, null));

        var before = entity.Status;
        SetStatus(entity, PaymentConnectionStatus.Disabled);
        AddAudit(entity, "Disabled", $"Se desactivó la conexión «{entity.Name}». Estaba {StatusLabel(before)}; la configuración se conserva.");
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(await ReloadAsync(entity.Id, cancellationToken), canEdit: true, scope: null));
    }

    // Una conexion activa no se elimina: primero se desactiva. Cuando existan cobros, una con historial tampoco se podra eliminar.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var entity = await FindAsync(id, tracking: true, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.Status == PaymentConnectionStatus.Active)
            return Problem409("payments_active", "La conexión está activa: desactivala antes de eliminarla.");

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        foreach (var link in entity.BuildingConfigs.Where(x => !x.IsDeleted))
        {
            link.IsDeleted = true;
            link.UpdatedAtUtc = DateTime.UtcNow;
        }

        AddAudit(entity, "Deleted", $"Se eliminó la conexión «{entity.Name}».");
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ─── Auxiliares ──────────────────────────────────────────────────────────

    private IQueryable<PaymentConnection> QueryConnections(Guid companyId) =>
        dbContext.PaymentConnections
            .Include(x => x.BuildingConfigs.Where(c => !c.IsDeleted)).ThenInclude(c => c.Building)
            .Where(x => !x.IsDeleted && x.CompanyId == companyId);

    private async Task<PaymentConnection?> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<PaymentConnection> query = dbContext.PaymentConnections
            .Include(x => x.BuildingConfigs.Where(c => !c.IsDeleted)).ThenInclude(c => c.Building)
            .Where(x => !x.IsDeleted && x.Id == id);
        if (!tracking) query = query.AsNoTracking();

        var entity = await query.FirstOrDefaultAsync(ct);
        if (entity is null) return null;

        // Sin revelar si existe: una conexion de otra empresa es "no encontrada".
        if (!tenantContext.IsSuperAdmin && entity.CompanyId != tenantContext.CompanyId) return null;
        return entity;
    }

    private async Task<PaymentConnection> ReloadAsync(Guid id, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        return await dbContext.PaymentConnections.AsNoTracking()
            .Include(x => x.BuildingConfigs.Where(c => !c.IsDeleted)).ThenInclude(c => c.Building)
            .FirstAsync(x => x.Id == id, ct);
    }

    private Task<bool> NameTakenAsync(Guid companyId, string name, Guid? excludeId, CancellationToken ct) =>
        dbContext.PaymentConnections.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.CompanyId == companyId && x.Name == name && (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    private static string? Validate(PaymentConnectionRequest request, PaymentProductDefinition product, out PaymentEnvironment environment)
    {
        environment = PaymentEnvironment.Sandbox;

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return "El nombre de la conexión es obligatorio.";
        if (name.Length > NameMaxLength) return $"El nombre no puede superar los {NameMaxLength} caracteres.";

        if (!Enum.TryParse(request.Environment, ignoreCase: true, out environment) || !Enum.IsDefined(environment))
            return "El ambiente debe ser Sandbox (pruebas) o Production (producción).";

        foreach (var (key, value) in request.Settings ?? [])
        {
            var field = product.Fields.FirstOrDefault(f => f.Key == key && !f.Secret);
            if (field is null) return $"El campo \"{key}\" no corresponde a esta modalidad.";
            var error = ValidateValue(field, value);
            if (error is not null) return error;
        }

        foreach (var (key, value) in request.Secrets ?? [])
        {
            var field = product.Fields.FirstOrDefault(f => f.Key == key && f.Secret);
            if (field is null) return $"El campo \"{key}\" no corresponde a esta modalidad.";
            var error = ValidateValue(field, value);
            if (error is not null) return error;
        }

        return null;
    }

    private static string? ValidateValue(PaymentField field, string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length > field.MaxLength) return $"{field.Label} no puede superar los {field.MaxLength} caracteres.";
        if (value.Any(char.IsControl)) return $"{field.Label} no puede tener saltos de línea ni caracteres de control.";
        if (value.Any(char.IsWhiteSpace)) return $"{field.Label} no puede tener espacios.";
        return null;
    }

    // Valores limpios (sin espacios) de los campos secretos o no secretos del catalogo. keepEmpty conserva los vacios (para borrar un dato).
    private static Dictionary<string, string> CleanValues(
        Dictionary<string, string>? values, PaymentProductDefinition product, bool secret, bool keepEmpty = false)
    {
        var result = new Dictionary<string, string>();
        foreach (var field in product.Fields.Where(f => f.Secret == secret))
        {
            if (values is null || !values.TryGetValue(field.Key, out var raw)) continue;
            var value = raw?.Trim() ?? string.Empty;
            if (value.Length == 0 && !keepEmpty) continue;
            result[field.Key] = value;
        }
        return result;
    }

    private static Dictionary<string, string> ReadValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Lee los secretos guardados (para sumarles los nuevos). Si no se pueden descifrar, no se pisan a ciegas.
    private Dictionary<string, string> ReadSecrets(PaymentConnection entity, out ObjectResult? error)
    {
        error = null;
        if (string.IsNullOrEmpty(entity.SecretsCipher)) return [];

        try
        {
            return protector.Unprotect(entity.SecretsCipher, entity.KeyId ?? string.Empty, entity.PublicId);
        }
        catch (SecretProtectorException exception)
        {
            logger.LogError(exception, "No se pudieron descifrar los secretos de la conexion de pago {ConnectionId}.", entity.Id);
            error = Problem409("payments_secrets_unreadable",
                "No se pueden leer las claves guardadas de esta conexión: la clave maestra del servidor no coincide con la que las cifró. Avisá a soporte.");
            return [];
        }
    }

    // Suma los secretos nuevos a los que ya habia, los cifra y guarda solo el final enmascarado. Registra en el historial QUE se
    // reemplazo (y su final), nunca el valor.
    private ObjectResult? ApplySecrets(
        PaymentConnection entity, PaymentProductDefinition product, Dictionary<string, string> existing,
        Dictionary<string, string> incoming, List<ConfigChange> changes)
    {
        if (!protector.IsConfigured)
            return Problem409("payments_master_key_missing",
                "El servidor no tiene configurada la clave maestra para proteger las claves del comercio. Avisá a soporte: no se guardó ninguna clave.");

        var oldMasks = ReadValues(entity.SecretsFingerprint);
        var merged = new Dictionary<string, string>(existing);
        var masks = new Dictionary<string, string>(oldMasks);
        foreach (var (key, value) in incoming)
        {
            merged[key] = value;
            masks[key] = Mask(value);
            changes.Add(new ConfigChange(key, FieldLabel(product, key) + " (secreta)",
                oldMasks.GetValueOrDefault(key), masks[key]));
        }

        entity.SecretsCipher = protector.Protect(merged, entity.PublicId);
        entity.KeyId = protector.CurrentKeyId;
        entity.SecretsFingerprint = JsonSerializer.Serialize(masks);
        entity.SecretsUpdatedAtUtc = DateTime.UtcNow;
        return null;
    }

    // ••••1234: el final alcanza para reconocer cual es sin acercarse a revelarla (si es corta, no se muestra nada de ella).
    internal static string Mask(string secret) => secret.Length >= 12 ? "••••" + secret[^4..] : "••••";

    private static bool IsComplete(PaymentConnection entity, PaymentProductDefinition product) => MissingFields(entity, product).Count == 0;

    private static List<string> MissingFields(PaymentConnection entity, PaymentProductDefinition product)
    {
        var settings = ReadValues(entity.SettingsJson);
        var masks = ReadValues(entity.SecretsFingerprint);
        return product.Fields
            .Where(f => f.Required && !(f.Secret ? masks.ContainsKey(f.Key) : settings.TryGetValue(f.Key, out var v) && !string.IsNullOrWhiteSpace(v)))
            .Select(f => f.Label)
            .ToList();
    }

    private void SetStatus(PaymentConnection entity, PaymentConnectionStatus status)
    {
        entity.Status = status;
        entity.StatusChangedAtUtc = DateTime.UtcNow;
        entity.StatusChangedByUserId = tenantContext.UserId;
    }

    private void AddAudit(PaymentConnection entity, string action, string summary, IReadOnlyList<ConfigChange>? changes = null)
    {
        dbContext.PaymentConnectionAudits.Add(new PaymentConnectionAudit
        {
            CompanyId = entity.CompanyId,
            ConnectionId = entity.Id,
            Action = action,
            Summary = summary.Length <= 500 ? summary : summary[..500],
            ChangesJson = changes is { Count: > 0 } ? JsonSerializer.Serialize(changes, JsonOptions) : null,
            UserId = tenantContext.UserId,
            UserEmail = Truncate(tenantContext.Email ?? string.Empty, 256),
            UserRole = Truncate(tenantContext.Role ?? string.Empty, 40),
            Ip = HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private PaymentConnectionDto ToDto(PaymentConnection entity, bool canEdit, HashSet<Guid>? scope)
    {
        var provider = PaymentProviderCatalog.FindProvider(entity.ProviderCode);
        var product = PaymentProviderCatalog.FindProduct(entity.ProviderCode, entity.ProductCode);

        var buildings = entity.BuildingConfigs
            .Where(x => !x.IsDeleted && (scope is null || scope.Contains(x.BuildingId)))
            .OrderBy(x => x.SortOrder)
            .Select(x => new PaymentConnectionBuildingDto
            {
                BuildingId = x.BuildingId, BuildingName = x.Building?.Name ?? string.Empty, IsEnabled = x.IsEnabled
            })
            .ToList();

        var dto = new PaymentConnectionDto
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            Name = entity.Name,
            ProviderCode = entity.ProviderCode,
            ProviderName = provider?.Name ?? entity.ProviderCode,
            ProductCode = entity.ProductCode,
            ProductName = product?.Name ?? entity.ProductCode,
            Environment = entity.Environment.ToString(),
            Status = entity.Status.ToString(),
            StatusChangedAtUtc = entity.StatusChangedAtUtc,
            Buildings = buildings
        };

        if (!canEdit) return dto;

        // Solo quien configura ve los datos del comercio, el final de las claves y la direccion de callback; las claves nunca salen.
        dto.Settings = ReadValues(entity.SettingsJson);
        dto.SecretsMasked = ReadValues(entity.SecretsFingerprint);
        dto.SecretsUpdatedAtUtc = entity.SecretsUpdatedAtUtc;
        dto.CallbackUrl = CallbackUrlFor(entity);
        dto.MissingFields = product is null ? [] : MissingFields(entity, product);
        return dto;
    }

    // La direccion que se registra en el portal del proveedor. Siempre https (el proveedor lo exige): se toma de la configuracion o,
    // si no esta, del host con el que se llamo a la API.
    private string CallbackUrlFor(PaymentConnection entity)
    {
        var baseUrl = configuration["Payments:CallbackBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = $"https://{Request.Host}";
        return $"{baseUrl.TrimEnd('/')}/api/payment-webhooks/{entity.PublicId}";
    }

    private static string FieldLabel(PaymentProductDefinition product, string key) =>
        product.Fields.FirstOrDefault(f => f.Key == key)?.Label ?? key;

    private static string EnvironmentLabel(PaymentEnvironment environment) =>
        environment == PaymentEnvironment.Production ? "Producción" : "Sandbox (pruebas)";

    private static string StatusLabel(PaymentConnectionStatus status) => status switch
    {
        PaymentConnectionStatus.Draft => "incompleta",
        PaymentConnectionStatus.PendingValidation => "pendiente de validación",
        PaymentConnectionStatus.Active => "activa",
        PaymentConnectionStatus.Disabled => "desactivada",
        _ => status.ToString()
    };
}
