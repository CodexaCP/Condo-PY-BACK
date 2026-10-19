namespace Condo.Domain.Enums;

// Quien es el titular de los libros contables del edificio (el que figura en el encabezado del Libro Diario). Se guarda como texto.
public enum AccountingBookHolder
{
    // El consorcio de propietarios, con su propio RUC.
    Consortium = 1,

    // La empresa administradora lleva el libro del edificio.
    AdminCompany = 2
}
