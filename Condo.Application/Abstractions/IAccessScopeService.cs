namespace Condo.Application.Abstractions;

public interface IAccessScopeService
{
    bool IsSuperAdmin { get; }
    bool IsCompanyAdmin { get; }
    Guid? CompanyId { get; }
    Guid? CondominiumId { get; }

    // true cuando el usuario no esta acotado por edificio: SuperAdmin o Administrador de empresa de toda la
    // empresa. Encargados, operadores y administradores acotados a un condominio trabajan solo con sus edificios.
    bool HasFullCompanyScope { get; }

    Task<HashSet<Guid>> GetAccessibleBuildingIdsAsync(CancellationToken cancellationToken);
    Task<bool> CanAccessBuildingAsync(Guid buildingId, CancellationToken cancellationToken);
    Task<bool> CanManageCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}
