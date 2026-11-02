namespace Condo.Domain.Enums;

public enum LateFeeFrequency
{
    Daily = 1,
    Weekly = 2,
    Biweekly = 3,
    // Cada 30 dias desde el corte de mora: el intervalo mas usado fuera de Paraguay (interes mensual).
    Monthly = 4
}
