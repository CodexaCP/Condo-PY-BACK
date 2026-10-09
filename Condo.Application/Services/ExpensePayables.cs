namespace Condo.Application.Services;

/// <summary>Estado de un gasto como cuenta por pagar. Sin vencimiento ni pago el gasto no es una cuenta por pagar: cuenta en su fecha.</summary>
public static class ExpensePayables
{
    public const string None = "None";
    public const string Pending = "Pending";
    public const string Overdue = "Overdue";
    public const string Paid = "Paid";

    public static string StatusOf(DateOnly? dueDate, DateOnly? paidAt, DateOnly today)
    {
        if (paidAt.HasValue) return Paid;
        if (!dueDate.HasValue) return None;
        return dueDate.Value < today ? Overdue : Pending;
    }

    /// <summary>Un gasto "a pagar" (con vencimiento y sin pago) no entra a la caja todavia.</summary>
    public static bool IsUnpaid(DateOnly? dueDate, DateOnly? paidAt) => dueDate.HasValue && !paidAt.HasValue;
}
