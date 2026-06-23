using Condo.Application.Abstractions;
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
public partial class CompaniesController(ICondoDbContext dbContext, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CompanyDto>>> GetAll(CancellationToken cancellationToken)
    {
        IQueryable<Company> query = dbContext.Companies.AsNoTracking().Where(x => !x.IsDeleted);

        if (!tenantContext.IsSuperAdmin)
        {
            return Forbid();
        }

        var companies = await query
            .OrderBy(x => x.Name)
            .Select(x => new CompanyDto
            {
                Id = x.Id,
                Name = x.Name,
                Slug = x.Slug,
                IsActive = x.IsActive,
                Description = x.Description,
                ContactPhonePrefix = x.ContactPhonePrefix,
                ContactPhone = x.ContactPhone,
                ContactEmail = x.ContactEmail
            })
            .ToListAsync(cancellationToken);

        return Ok(companies);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSuperAdmin)
        {
            return Forbid();
        }

        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Id == id)
            .Select(x => new CompanyDto
            {
                Id = x.Id,
                Name = x.Name,
                Slug = x.Slug,
                IsActive = x.IsActive,
                Description = x.Description,
                ContactPhonePrefix = x.ContactPhonePrefix,
                ContactPhone = x.ContactPhone,
                ContactEmail = x.ContactEmail
            })
            .FirstOrDefaultAsync(cancellationToken);

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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueSlugViolation(exception))
        {
            return Conflict("Ya existe una empresa con ese slug.");
        }

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

    private static CompanyDto ToDto(Company company) =>
        new()
        {
            Id = company.Id,
            Name = company.Name,
            Slug = company.Slug,
            IsActive = company.IsActive,
            Description = company.Description,
            ContactPhonePrefix = company.ContactPhonePrefix,
            ContactPhone = company.ContactPhone,
            ContactEmail = company.ContactEmail
        };
}
