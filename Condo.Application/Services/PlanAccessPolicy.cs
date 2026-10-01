namespace Condo.Application.Services;

/// <summary>
/// Etapa de acceso de un edificio segun el vencimiento de su plan.
/// Se calcula siempre a partir de las fechas (nunca se guarda), asi que se levanta sola apenas se
/// aprueba un pago que mueve el vencimiento hacia adelante.
/// </summary>
public enum PlanAccessPhase
{
    /// <summary>Plan vigente (todavia no paso la fecha de vencimiento).</summary>
    Normal = 0,

    /// <summary>Vencido, dentro de los dias de gracia: sigue operando normal, con avisos por correo.</summary>
    Grace = 1,

    /// <summary>Termino la gracia: solo consulta. Lo unico que se puede hacer es enviar el pago.</summary>
    ReadOnly = 2,

    /// <summary>Restriccion total hasta que se apruebe el pago. Solo queda iniciar sesion y enviar el pago.</summary>
    Blocked = 3
}

public static class PlanAccessPolicy
{
    /// <summary>
    /// Dias que dura la etapa de solo lectura, contados desde que termina la gracia.
    /// Con 5 dias de gracia: lectura del dia 5 al 9 y bloqueo total desde el dia 9.
    /// </summary>
    public const int ReadOnlyDays = 4;

    /// <summary>Dias de vencido (contados desde EndDate) en que cada etapa empieza.</summary>
    public static int ReadOnlyStartsAtDay(int graceDays) => Math.Max(0, graceDays);
    public static int BlockedStartsAtDay(int graceDays) => ReadOnlyStartsAtDay(graceDays) + ReadOnlyDays;

    public static PlanAccessPhase GetPhase(DateTime endDate, int graceDays, DateTime todayUtc)
    {
        var overdue = (int)(todayUtc.Date - endDate.Date).TotalDays;

        if (overdue <= 0) return PlanAccessPhase.Normal;
        if (overdue < ReadOnlyStartsAtDay(graceDays)) return PlanAccessPhase.Grace;
        if (overdue < BlockedStartsAtDay(graceDays)) return PlanAccessPhase.ReadOnly;
        return PlanAccessPhase.Blocked;
    }

    /// <summary>
    /// Nombre del estado que ven las pantallas: Archived, Active, ExpiringSoon (7 dias o menos), Expired (vencido,
    /// en gracia), ReadOnly o Blocked.
    /// </summary>
    public static string GetStatusName(bool isArchived, DateTime endDate, int graceDays, DateTime todayUtc)
    {
        if (isArchived) return "Archived";

        switch (GetPhase(endDate, graceDays, todayUtc))
        {
            case PlanAccessPhase.Blocked: return "Blocked";
            case PlanAccessPhase.ReadOnly: return "ReadOnly";
            case PlanAccessPhase.Grace: return "Expired";
        }

        var days = (int)(endDate.Date - todayUtc.Date).TotalDays;
        return days <= 7 ? "ExpiringSoon" : "Active";
    }

    /// <summary>Dias que faltan para que empiece el bloqueo total (0 si ya esta bloqueado o el plan esta vigente).</summary>
    public static int DaysUntilBlocked(DateTime endDate, int graceDays, DateTime todayUtc)
    {
        var overdue = (int)(todayUtc.Date - endDate.Date).TotalDays;
        if (overdue <= 0) return 0;
        return Math.Max(0, BlockedStartsAtDay(graceDays) - overdue);
    }
}
