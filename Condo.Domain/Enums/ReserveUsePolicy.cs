namespace Condo.Domain.Enums;

// Quien tiene que aprobar el uso del fondo de reserva del edificio (politica informativa: se muestra en el Centro de configuracion).
public enum ReserveUsePolicy
{
    FreeUse = 1,
    RequiresPresidentApproval = 2,
    RequiresAssemblyApproval = 3
}
