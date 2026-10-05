namespace Condo.Domain.Enums;

public enum BuildingExpenseCreditNoteMode
{
    // Periodo sin publicar: se descuenta del monto del gasto y la liquidacion se calcula con el neto.
    Netted = 1,
    // Periodo ya publicado: se reparte entre las unidades como saldo a favor (todavia no implementado).
    Credited = 2
}
