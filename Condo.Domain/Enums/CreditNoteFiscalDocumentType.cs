namespace Condo.Domain.Enums;

// De que tipo es el documento fiscal oficial de la NC que se registra a mano: en papel (timbrado
// preimpreso, sin CDC) o electronico (con CDC/XML). Solo afecta que campos sugiere la UI, ninguno
// de los campos fiscales de CreditNote es obligatorio segun este valor.
public enum CreditNoteFiscalDocumentType
{
    Paper = 1,
    Electronic = 2
}
