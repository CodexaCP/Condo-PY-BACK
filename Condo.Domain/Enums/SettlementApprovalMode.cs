namespace Condo.Domain.Enums;

// Cadena de aprobacion de la liquidacion de expensas de un edificio. Se guarda como texto.
public enum SettlementApprovalMode
{
    // Encargado de edificio -> presidente del consorcio -> Administrador de empresa (la cadena de siempre en Paraguay).
    ManagerPresidentAdmin = 1,
    // Encargado de edificio -> Administrador de empresa: sin revision del presidente. Para edificios sin presidente o consejo
    // (en Argentina el consejo es facultativo, CCyC art. 2064).
    ManagerAdmin = 2
}
