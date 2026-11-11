namespace Condo.Domain.Enums;

// Se guardan como texto (HasConversion<string>) y viajan como texto en el JSON: cambiar un nombre rompe los datos y los clientes.

// Ciclo de vida: Draft -> Open -> Closed. Una votacion abierta se puede anular (Cancelled); cerrada o anulada ya no cambia.
public enum VoteStatus
{
    Draft = 0,
    Open = 1,
    Closed = 2,
    Cancelled = 3
}

// Como pesa cada voto en el quorum y en el resultado: una unidad = un voto, o segun el coeficiente de la unidad.
public enum VoteWeightType
{
    ByUnit = 0,
    ByCoefficient = 1
}

// Simple: quorum y gana la opcion con mas peso (empate = sin ganadora). DoubleAbsolute: mas del 50 % de TODAS las unidades y de TODO
// el coeficiente del edificio a la vez, hayan votado o no (CCyC art. 2060).
public enum VoteMajorityRule
{
    Simple = 0,
    DoubleAbsolute = 1
}

// Lo que paso con el voto de una unidad: primer voto, cambio de opcion o voto quitado.
public enum VoteCastAction
{
    Cast = 0,
    Change = 1,
    Remove = 2
}

// Quien cargo el voto: el propietario desde su cuenta, o el personal (voto de asamblea presencial).
public enum VoteCastSource
{
    Owner = 0,
    Staff = 1
}
