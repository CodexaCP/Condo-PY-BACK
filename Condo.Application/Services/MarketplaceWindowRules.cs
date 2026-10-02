namespace Condo.Application.Services;

/// <summary>
/// Reglas del tiempo del marketplace. Todo el horario se maneja en bloques de 30 minutos: la ventana publicada y cada
/// reserva empiezan y terminan en punto o y media, con los mismos minutos al inicio y al fin, de modo que siempre duran
/// horas enteras (nunca 2 h 30). Los intervalos son semiabiertos [inicio, fin): 19:00-22:00 no choca con 22:00-23:00.
/// La alineacion se valida en UTC: el offset de Paraguay es de horas enteras, asi que equivale a la hora local.
/// </summary>
public static class MarketplaceWindowRules
{
    public const int SlotMinutes = 30;

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>true si la hora cae en punto o y media exactos (sin segundos ni fracciones).</summary>
    public static bool IsAligned(DateTime value)
    {
        var utc = ToUtc(value);
        return utc.Minute % SlotMinutes == 0 && utc.Ticks % TimeSpan.TicksPerMinute == 0;
    }

    /// <summary>Primer bloque (en punto o y media) que empieza estrictamente despues del instante dado.</summary>
    public static DateTime NextSlotAfter(DateTime value)
    {
        var utc = ToUtc(value);
        var next = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        while (next <= utc)
        {
            next = next.AddMinutes(SlotMinutes);
        }

        return next;
    }

    /// <summary>Intervalos semiabiertos [inicio, fin): se tocan en un extremo pero no se solapan.</summary>
    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) =>
        ToUtc(aStart) < ToUtc(bEnd) && ToUtc(bStart) < ToUtc(aEnd);

    /// <summary>Cantidad de horas enteras entre dos horas alineadas con los mismos minutos.</summary>
    public static int WholeHours(DateTime startUtc, DateTime endUtc) =>
        (int)((ToUtc(endUtc) - ToUtc(startUtc)).TotalMinutes / 60);

    /// <summary>Inicio de cada bloque de 30 minutos entre inicio (incluido) y fin (excluido).</summary>
    public static IReadOnlyList<DateTime> Slots(DateTime startUtc, DateTime endUtc)
    {
        var start = ToUtc(startUtc);
        var end = ToUtc(endUtc);
        var slots = new List<DateTime>();
        for (var current = start; current < end; current = current.AddMinutes(SlotMinutes))
        {
            slots.Add(current);
        }

        return slots;
    }

    /// <summary>Mensaje de error si la ventana de una publicacion no es valida; null si lo es.</summary>
    public static string? ValidateListingWindow(DateTime startUtc, DateTime endUtc, DateTime nowUtc) =>
        ValidateInterval(startUtc, endUtc, nowUtc);

    /// <summary>
    /// Mensaje de error si la reserva no es valida dentro de la ventana de la publicacion; null si lo es.
    /// </summary>
    public static string? ValidateReservationInterval(
        DateTime windowStartUtc, DateTime windowEndUtc, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        var error = ValidateInterval(startUtc, endUtc, nowUtc);
        if (error is not null)
        {
            return error;
        }

        if (ToUtc(startUtc) < ToUtc(windowStartUtc) || ToUtc(endUtc) > ToUtc(windowEndUtc))
        {
            return "El horario elegido queda fuera del horario publicado.";
        }

        return null;
    }

    private static string? ValidateInterval(DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        var start = ToUtc(startUtc);
        var end = ToUtc(endUtc);

        if (!IsAligned(start) || !IsAligned(end))
        {
            return "Las horas deben ser en punto o y media.";
        }

        if (end <= start)
        {
            return "La hora de fin debe ser posterior a la de inicio.";
        }

        if (start.Minute != end.Minute)
        {
            return "Desde y hasta deben terminar en los mismos minutos: la duración debe ser de horas enteras.";
        }

        if (start <= ToUtc(nowUtc))
        {
            return "El horario debe empezar en el futuro.";
        }

        return null;
    }
}
