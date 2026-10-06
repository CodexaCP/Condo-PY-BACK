using System.Text.RegularExpressions;

namespace Condo.Api.Services;

// Reglas comunes de los datos de una persona (propietario, residente, datos de facturacion): documento segun su tipo,
// telefono con prefijo y correo. Los controladores las aplican solo cuando el dato cambia, para no bloquear la edicion de
// otros campos de una persona cargada antes con un documento en otro formato.
public static partial class PersonRules
{
    public const string Ruc = "RUC";
    public const string Cedula = "CedulaParaguaya";

    public static string? NormalizeType(string? raw) => raw?.Trim() is { Length: > 0 } t ? t : null;

    public static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Documento valido para su tipo; null = ok o vacio (si es obligatorio lo exige quien llama).
    //  - RUC: 80012345-0, con su digito verificador.
    //  - Cedula paraguaya: solo digitos (se aceptan puntos y espacios de separador).
    //  - Cualquier otro tipo (o sin tipo): letras, numeros, puntos y guiones.
    public static string? ValidateDocument(string? type, string? number, string label = "El documento")
    {
        var value = number?.Trim();
        if (string.IsNullOrEmpty(value)) return null;

        if (value.Length > 40) return $"{label} no puede superar los 40 caracteres.";

        if (string.Equals(type, Ruc, StringComparison.OrdinalIgnoreCase))
            return BuildingProfile.IsValidRuc(value)
                ? null
                : $"{label} (RUC) no es válido: usá el formato 80012345-0 con su dígito verificador correcto.";

        if (string.Equals(type, Cedula, StringComparison.OrdinalIgnoreCase))
            return CedulaRegex().IsMatch(value) ? null : $"{label} (cédula) solo puede tener números.";

        return GenericDocumentRegex().IsMatch(value) ? null : $"{label} solo puede contener letras, números, puntos o guiones.";
    }

    // Telefono completo con prefijo (+595981123456): se guarda sin espacios ni guiones.
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return new string(value.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')')).ToArray());
    }

    public static bool IsValidPhone(string? normalized) => normalized is null || FullPhoneRegex().IsMatch(normalized);

    public static bool IsValidEmail(string? value) => string.IsNullOrWhiteSpace(value) || EmailRegex().IsMatch(value.Trim());

    public static string? Max(string? value, int max, string label) =>
        value is not null && value.Trim().Length > max ? $"{label} no puede superar los {max} caracteres." : null;

    [GeneratedRegex(@"^[\d. ]{3,15}$")]
    private static partial Regex CedulaRegex();

    [GeneratedRegex(@"^[A-Za-z0-9.\-]+$")]
    private static partial Regex GenericDocumentRegex();

    [GeneratedRegex(@"^\+?\d{6,20}$")]
    private static partial Regex FullPhoneRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();
}
