using Condo.Domain.Enums;

namespace Condo.Api.Services;

public static class CategoryLabels
{
    public static string ExpenseLabel(BuildingExpenseCategory category) => category switch
    {
        BuildingExpenseCategory.Utilities => "Servicios",
        BuildingExpenseCategory.Cleaning => "Limpieza",
        BuildingExpenseCategory.Security => "Seguridad",
        BuildingExpenseCategory.Maintenance => "Mantenimiento",
        BuildingExpenseCategory.Elevator => "Ascensor",
        BuildingExpenseCategory.Insurance => "Seguro",
        BuildingExpenseCategory.Payroll => "Salarios",
        BuildingExpenseCategory.Taxes => "Impuestos",
        BuildingExpenseCategory.Administration => "Administracion",
        BuildingExpenseCategory.ReserveFund => "Fondo de reserva",
        BuildingExpenseCategory.Extraordinary => "Extraordinario",
        BuildingExpenseCategory.Supplies => "Insumos",
        BuildingExpenseCategory.Ande => "ANDE",
        BuildingExpenseCategory.Essap => "ESSAP",
        BuildingExpenseCategory.InternetPhone => "Internet y telefonia",
        _ => "Otro"
    };

    public static string IncomeLabel(BuildingIncomeCategory category) => category switch
    {
        BuildingIncomeCategory.CommonAreaRental => "Alquiler area comun",
        BuildingIncomeCategory.Interest => "Interes",
        BuildingIncomeCategory.OperationalFund => "Fondo operativo",
        BuildingIncomeCategory.CreditAdjustment => "Ajuste a favor",
        _ => "Otro"
    };
}
