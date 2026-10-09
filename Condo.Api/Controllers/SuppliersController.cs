using System.Net.Mail;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Condo.Api.Controllers;

/// <summary>
/// Proveedores de la empresa, compartidos entre todos sus edificios (Centro de configuracion, seccion Proveedores). Los ven los cuatro
/// roles administrativos y los cargan y editan quienes ya cargan gastos (SuperAdmin, Administrador, Operador y Encargado). El RUC es unico
/// por empresa y el nombre tampoco se repite. No se eliminan: se desactivan (un proveedor desactivado deja de ofrecerse en los gastos nuevos
/// pero los gastos que ya lo usan lo conservan).
/// </summary>
[ApiController]
[Authorize]
[Route("api/suppliers")]
public class SuppliersController(ICondoDbContext dbContext, ITenantContext tenantContext) : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int NameMaxLength = 200;
    private const int PhoneMaxLength = 40;
    private const int EmailMaxLength = 160;
    private const int AddressMaxLength = 300;
    private const int MaxPaymentTermDays = 365;

    private static readonly string[] ViewRoles = ["SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager"];
    private static readonly string[] EditRoles = ["SuperAdmin", "CompanyAdmin", "CompanyOperator", "BuildingManager"];

    private bool CanView => ViewRoles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase);
    private bool CanEdit => EditRoles.Contains(tenantContext.Role, StringComparer.OrdinalIgnoreCase);

    private ObjectResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, new
    {
        error = "suppliers_forbidden",
        message = "Tu rol no puede administrar proveedores."
    });

    // La empresa sobre la que se opera: la del usuario; el SuperAdmin indica una.
    private Guid? EffectiveCompanyId(Guid? requested) => tenantContext.IsSuperAdmin ? requested : tenantContext.CompanyId;

    [HttpGet]
    public async Task<ActionResult<SupplierPageDto>> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] string? q,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!CanView) return Forbidden();

        var company = EffectiveCompanyId(companyId);
        if (!company.HasValue) return BadRequest("Indicá la empresa.");

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.Suppliers.AsNoTracking().Where(x => !x.IsDeleted && x.CompanyId == company.Value);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.Name.Contains(term) || (x.Ruc != null && x.Ruc.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(x => x.Id).ToList();
        var counts = (await dbContext.BuildingExpenses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.SupplierId != null && ids.Contains(x.SupplierId.Value))
                .Select(x => x.SupplierId!.Value)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new SupplierPageDto
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            CanEdit = CanEdit,
            Items = rows.Select(x => ToDto(x, counts.GetValueOrDefault(x.Id))).ToList()
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!CanView) return Forbidden();

        var supplier = await FindAsync(id, tracking: false, cancellationToken);
        if (supplier is null) return NotFound();

        var count = await dbContext.BuildingExpenses.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.SupplierId == id, cancellationToken);
        return Ok(ToDto(supplier, count));
    }

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] SupplierUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var company = EffectiveCompanyId(request.CompanyId);
        if (!company.HasValue) return BadRequest("Indicá la empresa.");
        if (tenantContext.IsSuperAdmin
            && !await dbContext.Companies.AsNoTracking().AnyAsync(x => !x.IsDeleted && x.Id == company.Value, cancellationToken))
        {
            return BadRequest("La empresa no existe.");
        }

        var (error, normalized) = Validate(request);
        if (error is not null) return BadRequest(error);

        var conflict = await FindConflictAsync(company.Value, normalized!.Name, normalized.Ruc, null, cancellationToken);
        if (conflict is not null) return Conflict(conflict);

        var supplier = new Supplier { CompanyId = company.Value };
        Apply(supplier, normalized);
        dbContext.Suppliers.Add(supplier);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("Otro proveedor con el mismo RUC se cargó al mismo tiempo. Actualizá la pantalla.");
        }

        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, ToDto(supplier, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, [FromBody] SupplierUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbidden();

        var supplier = await FindAsync(id, tracking: true, cancellationToken);
        if (supplier is null) return NotFound();

        var (error, normalized) = Validate(request);
        if (error is not null) return BadRequest(error);

        var conflict = await FindConflictAsync(supplier.CompanyId, normalized!.Name, normalized.Ruc, id, cancellationToken);
        if (conflict is not null) return Conflict(conflict);

        Apply(supplier, normalized);
        supplier.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("Otro proveedor con el mismo RUC se cargó al mismo tiempo. Actualizá la pantalla.");
        }

        var count = await dbContext.BuildingExpenses.AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.SupplierId == id, cancellationToken);
        return Ok(ToDto(supplier, count));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // El proveedor de la empresa del usuario (el SuperAdmin ve todos). Uno ajeno responde 404, igual que uno inexistente.
    private async Task<Supplier?> FindAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<Supplier> query = dbContext.Suppliers.Where(x => !x.IsDeleted && x.Id == id);
        if (!tracking) query = query.AsNoTracking();
        if (!tenantContext.IsSuperAdmin)
        {
            var company = tenantContext.CompanyId;
            if (!company.HasValue) return null;
            query = query.Where(x => x.CompanyId == company.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record Normalized(string Name, string? Ruc, string? Phone, string? Email, string? Address, int? PaymentTermDays, bool IsActive);

    private static (string? Error, Normalized? Value) Validate(SupplierUpsertRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return ("El nombre del proveedor es obligatorio.", null);
        if (name.Length > NameMaxLength) return ($"El nombre no puede superar los {NameMaxLength} caracteres.", null);

        // El RUC se guarda sin espacios y en mayusculas; con digito verificador, igual que el RUC del edificio.
        var ruc = string.IsNullOrWhiteSpace(request.Ruc) ? null : request.Ruc.Replace(" ", string.Empty).ToUpperInvariant();
        if (ruc is not null && !BuildingProfile.IsValidRuc(ruc)) return ("El RUC no es válido. Ejemplo: 80012345-6.", null);

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is { Length: > PhoneMaxLength }) return ($"El teléfono no puede superar los {PhoneMaxLength} caracteres.", null);

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        if (email is not null && (email.Length > EmailMaxLength || !IsValidEmail(email))) return ("El correo no es válido.", null);

        var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (address is { Length: > AddressMaxLength }) return ($"La dirección no puede superar los {AddressMaxLength} caracteres.", null);

        if (request.PaymentTermDays is < 0 or > MaxPaymentTermDays) return ($"El plazo de pago debe estar entre 0 y {MaxPaymentTermDays} días.", null);

        return (null, new Normalized(name, ruc, phone, email, address, request.PaymentTermDays, request.IsActive));
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // El RUC y el nombre no se repiten dentro de la empresa. Devuelve el mensaje del choque, o null si no hay.
    private async Task<string?> FindConflictAsync(Guid companyId, string name, string? ruc, Guid? excludeId, CancellationToken cancellationToken)
    {
        var others = dbContext.Suppliers.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CompanyId == companyId && (excludeId == null || x.Id != excludeId));

        if (ruc is not null)
        {
            var byRuc = await others.Where(x => x.Ruc == ruc).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);
            if (byRuc is not null) return $"Ya existe un proveedor con ese RUC: {byRuc}.";
        }

        var upper = name.ToUpper();
        if (await others.AnyAsync(x => x.Name.ToUpper() == upper, cancellationToken))
        {
            return "Ya existe un proveedor con ese nombre.";
        }

        return null;
    }

    private static void Apply(Supplier supplier, Normalized n)
    {
        supplier.Name = n.Name;
        supplier.Ruc = n.Ruc;
        supplier.Phone = n.Phone;
        supplier.Email = n.Email;
        supplier.Address = n.Address;
        supplier.PaymentTermDays = n.PaymentTermDays;
        supplier.IsActive = n.IsActive;
    }

    private static SupplierDto ToDto(Supplier x, int expenseCount) => new()
    {
        Id = x.Id,
        CompanyId = x.CompanyId,
        Name = x.Name,
        Ruc = x.Ruc,
        Phone = x.Phone,
        Email = x.Email,
        Address = x.Address,
        PaymentTermDays = x.PaymentTermDays,
        IsActive = x.IsActive,
        ExpenseCount = expenseCount
    };
}
