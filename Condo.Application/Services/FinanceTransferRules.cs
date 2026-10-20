using Condo.Domain.Entities;
using Condo.Domain.Enums;

namespace Condo.Application.Services;

/// <summary>
/// Reglas puras de las transferencias entre cuentas propias (operacion guiada de Finanzas): nombre de la operacion segun las cuentas
/// que participan, glosa comun de los dos movimientos y validacion de los datos. No toca la base.
/// </summary>
public static class FinanceTransferRules
{
    public const int ReferenceMaxLength = 100;
    public const int NotesMaxLength = 500;
    public const int SupportMaxLength = 500;
    public const int ReasonMinLength = 5;
    public const int ReasonMaxLength = 500;

    /// <summary>Que operacion es, segun el tipo de las cuentas de origen y de destino.</summary>
    public static string KindLabel(FinancialAccountType from, FinancialAccountType to)
    {
        if (to == FinancialAccountType.ReserveFund && from != FinancialAccountType.ReserveFund) return "Aporte al fondo de reserva";
        if (from == FinancialAccountType.ReserveFund && to != FinancialAccountType.ReserveFund) return "Retiro del fondo de reserva";
        if (from == FinancialAccountType.Clearing) return "Liquidación de cobros online";
        if (from == FinancialAccountType.Cash && to == FinancialAccountType.Bank) return "Depósito de efectivo";
        if (from == FinancialAccountType.Bank && to == FinancialAccountType.Cash) return "Retiro de efectivo";
        return "Transferencia entre cuentas";
    }

    /// <summary>El fondo de reserva participa de la operacion: pide respaldo (acta o resolucion) y no puede quedar en negativo.</summary>
    public static bool InvolvesReserveFund(FinancialAccountType from, FinancialAccountType to) =>
        from == FinancialAccountType.ReserveFund || to == FinancialAccountType.ReserveFund;

    /// <summary>Glosa comun a los dos movimientos (asi el asiento los junta en una sola descripcion).</summary>
    public static string Description(FinancialAccount from, FinancialAccount to, string notes)
    {
        var text = $"{KindLabel(from.Type, to.Type)}: {from.Name} → {to.Name}";
        return string.IsNullOrWhiteSpace(notes) ? text : $"{text} — {notes.Trim()}";
    }

    /// <summary>
    /// Valida los datos de una transferencia nueva contra las cuentas del edificio. Devuelve el mensaje de error o nulo si esta bien.
    /// No mira saldos, meses cerrados ni permisos (eso lo hace el servicio, que tiene la base).
    /// </summary>
    public static string? Validate(
        FinancialAccount? from, FinancialAccount? to, decimal amount, DateOnly date, DateOnly startDate, DateOnly today,
        string reference, string notes, string support)
    {
        if (from is null || to is null) return "Elegí la cuenta de origen y la de destino de este edificio.";
        if (from.Id == to.Id) return "La cuenta de origen y la de destino tienen que ser distintas.";
        if (from.BuildingId != to.BuildingId) return "Las dos cuentas tienen que ser del mismo edificio.";
        if (!from.IsActive || !to.IsActive) return "Las dos cuentas tienen que estar activas.";
        if (to.Type == FinancialAccountType.Clearing)
        {
            return "La cuenta de cobros online por liquidar solo puede ser el origen: los cobros entran por el proveedor de pagos.";
        }

        if (amount <= 0m) return "El importe tiene que ser mayor que cero.";
        if (decimal.Round(amount, 2) != amount) return "El importe no puede tener más de dos decimales.";
        if (date > today) return "La fecha no puede ser futura.";
        if (date < startDate) return $"La fecha es anterior al arranque de las finanzas del edificio ({startDate:dd/MM/yyyy}).";
        if (reference.Trim().Length > ReferenceMaxLength) return $"La referencia no puede superar los {ReferenceMaxLength} caracteres.";
        if (notes.Trim().Length > NotesMaxLength) return $"La nota no puede superar los {NotesMaxLength} caracteres.";
        if (support.Trim().Length > SupportMaxLength) return $"El respaldo no puede superar los {SupportMaxLength} caracteres.";
        if (InvolvesReserveFund(from.Type, to.Type) && support.Trim().Length == 0)
        {
            return "Un movimiento del fondo de reserva exige el respaldo: número de acta o resolución de la asamblea.";
        }

        return null;
    }
}
