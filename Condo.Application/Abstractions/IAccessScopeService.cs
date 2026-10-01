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

    // Edificios con los que el usuario puede trabajar. Para Administrador de empresa, Operador y Encargado deja
    // fuera los edificios en restriccion total por plan vencido (ver PlanAccessPolicy): para el resto del sistema
    // es como si no los tuvieran asignados.
    Task<HashSet<Guid>> GetAccessibleBuildingIdsAsync(CancellationToken cancellationToken);

    // Igual que la anterior pero sin sacar los edificios restringidos por plan. Solo para "Mi plan" y los pagos
    // del plan, que tienen que seguir funcionando para poder regularizar la situacion.
    Task<HashSet<Guid>> GetAllAccessibleBuildingIdsAsync(CancellationToken cancellationToken);
    Task<bool> CanAccessBuildingAsync(Guid buildingId, CancellationToken cancellationToken);
    Task<bool> CanManageCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}
