using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Localization;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Condo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/companies")]
public partial class CompaniesController(ICondoDbContext dbContext, ITenantContext tenantContext, CompanyLocaleService localeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyDto>>> GetAll(CancellationToken cancellationToken)
    {
        IQueryable<Company> query = dbContext.Companies.AsNoTracking().Where(x => !x.IsDeleted);

        if (!tenantContext.IsSuperAdmin)
        {
            return Forbid();
        }

        var companies = (await query
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken))
            .Select(ToDto)
            .ToList();

        return Ok(companies);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return Forbid();
        }

        var entity = await dbContext.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        var company = entity is null ? null : ToDto(entity);

        return company is null ? NotFound() : Ok(company);
    }

    [HttpPost]
    public async Task<ActionResult<CompanyDto>> Create([FromBody] CompanyUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return Forbid();
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();

        var duplicatedSlug = await dbContext.Companies
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.Slug == normalizedSlug, cancellationToken);

        if (duplicatedSlug)
        {
            return Conflict("Ya existe una empresa con ese slug.");
        }

        var company = new Company
        {
            Name = normalizedName,
            Slug = normalizedSlug,
            IsActive = request.IsActive,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ContactPhonePrefix = string.IsNullOrWhiteSpace(request.ContactPhonePrefix) ? null : request.ContactPhonePrefix.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant()
        };

        var localeError = await ApplyLocaleAsync(company, request, isNew: true, cancellationToken);
        if (localeError is not null)
        {
            return BadRequest(localeError);
        }

        dbContext.Companies.Add(company);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueSlugViolation(exception))
        {
            return Conflict("Ya existe una empresa con ese slug.");
        }

        return CreatedAtAction(nameof(GetById), new { id = company.Id }, ToDto(company));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return Forbid();
        }

        var company = await dbContext.Companies.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (company is null)
        {
            return NotFound();
        }

        var condominiums = await dbContext.Condominiums
            .Where(x => !x.IsDeleted && x.CompanyId == id)
            .ToListAsync(cancellationToken);

        foreach (var condo in condominiums)
        {
            condo.CompanyId = null;
        }

        var buildings = await dbContext.Buildings
            .Where(x => !x.IsDeleted && x.CompanyId == id)
            .ToListAsync(cancellationToken);

        foreach (var building in buildings)
        {
            building.CompanyId = null;
        }

        var users = await dbContext.ApplicationUsers
            .Where(x => !x.IsDeleted && x.CompanyId == id)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            user.CompanyId = null;
        }

        company.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> Update(Guid id, [FromBody] CompanyUpsertRequest request, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return Forbid();
        }

        var company = await dbContext.Companies.FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, cancellationToken);
        if (company is null)
        {
            return NotFound();
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedName = request.Name.Trim();
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();

        var duplicatedSlug = await dbContext.Companies
            .AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.Id != id && x.Slug == normalizedSlug, cancellationToken);

        if (duplicatedSlug)
        {
            return Conflict("Ya existe una empresa con ese slug.");
        }

        company.Name = normalizedName;
        company.Slug = normalizedSlug;
        company.IsActive = request.IsActive;
        company.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        company.ContactPhonePrefix = string.IsNullOrWhiteSpace(request.ContactPhonePrefix) ? null : request.ContactPhonePrefix.Trim();
        company.ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? null : request.ContactPhone.Trim();
        company.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim().ToLowerInvariant();

        var localeError = await ApplyLocaleAsync(company, request, isNew: false, cancellationToken);
        if (localeError is not null)
        {
            return BadRequest(localeError);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueSlugViolation(exception))
        {
            return Conflict("Ya existe una empresa con ese slug.");
        }

        localeService.Invalidate(company.Id);
        return Ok(ToDto(company));
    }

    private static string? ValidateRequest(CompanyUpsertRequest request)
    {
        var normalizedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return "El nombre es obligatorio.";
        }

        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return "El slug es obligatorio.";
        }

        if (!SlugRegex().IsMatch(normalizedSlug))
        {
            return "El slug solo puede contener letras minusculas, numeros y guiones medios.";
        }

        return null;
    }

    private static bool IsUniqueSlugViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains("IX_Companies_Slug", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();

    /// <summary>
    /// Pais, moneda e impuesto de la empresa. Al crearla se completan desde el pais elegido (Paraguay si no se manda ninguno). Al editarla,
    /// el pais y la moneda solo cambian mientras la empresa no tenga cargos ni gastos: despues de eso la moneda queda fija.
    /// </summary>
    private async Task<string?> ApplyLocaleAsync(Company company, CompanyUpsertRequest request, bool isNew, CancellationToken cancellationToken)
    {
        var requestedCountry = string.IsNullOrWhiteSpace(request.CountryCode) ? (isNew ? CountryProfiles.Paraguay : company.CountryCode) : request.CountryCode.Trim().ToUpperInvariant();
        var profile = CountryProfiles.Find(requestedCountry);
        if (profile is null)
        {
            return "El país no está disponible.";
        }

        var requestedCurrency = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? (isNew || !string.Equals(requestedCountry, company.CountryCode, StringComparison.OrdinalIgnoreCase) ? profile.DefaultCurrencyCode : company.CurrencyCode)
            : request.CurrencyCode.Trim().ToUpperInvariant();
        var currency = Currencies.Find(requestedCurrency);
        if (currency is null)
        {
            return "La moneda no está disponible.";
        }

        if (!isNew
            && (!string.Equals(profile.Code, company.CountryCode, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(currency.Code, company.CurrencyCode, StringComparison.OrdinalIgnoreCase)))
        {
            var hasMovements = await dbContext.ExpenseCharges.AnyAsync(x => !x.IsDeleted && x.CompanyId == company.Id, cancellationToken)
                               || await dbContext.BuildingExpenses.AnyAsync(x => !x.IsDeleted && x.CompanyId == company.Id, cancellationToken);
            if (hasMovements)
            {
                return "La empresa ya tiene cargos o gastos: no se puede cambiar el país ni la moneda.";
            }
        }

        company.CountryCode = profile.Code;
        company.CurrencyCode = currency.Code;
        company.LocaleCode = profile.LocaleCode;

        if (isNew || request.TaxConfigured)
        {
            var taxName = string.IsNullOrWhiteSpace(request.TaxName) ? profile.TaxName : request.TaxName.Trim();
            if (taxName.Length > 30)
            {
                return "El nombre del impuesto no puede superar los 30 caracteres.";
            }

            var general = request.TaxConfigured ? request.TaxGeneralRate : profile.TaxGeneralRate;
            var reduced = request.TaxConfigured ? request.TaxReducedRate : profile.TaxReducedRate;
            if (general is < 0m or > 100m || reduced is < 0m or > 100m)
            {
                return "Las tasas del impuesto deben estar entre 0 y 100.";
            }

            if (general is not null && reduced is not null && reduced >= general)
            {
                return "La tasa reducida debe ser menor que la tasa general.";
            }

            company.TaxName = taxName;
            company.TaxGeneralRate = general;
            company.TaxReducedRate = reduced;
        }

        return null;
    }

    private static CompanyDto ToDto(Company company)
    {
        var profile = CountryProfiles.FindOrDefault(company.CountryCode);
        return new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Slug = company.Slug,
            IsActive = company.IsActive,
            Description = company.Description,
            ContactPhonePrefix = company.ContactPhonePrefix,
            ContactPhone = company.ContactPhone,
            ContactEmail = company.ContactEmail,
            CountryCode = profile.Code,
            CountryName = profile.Name,
            IsDraftCountry = profile.IsDraft,
            CurrencyCode = company.CurrencyCode,
            LocaleCode = company.LocaleCode,
            TaxName = company.TaxName,
            TaxGeneralRate = company.TaxGeneralRate,
            TaxReducedRate = company.TaxReducedRate
        };
    }
}
