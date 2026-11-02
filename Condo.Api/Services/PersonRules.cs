using System.Text.RegularExpressions;

namespace Condo.Api.Services;

// Reglas comunes de los datos de una persona (propietario, residente, datos de facturacion): documento segun su tipo,
// telefono con prefijo y correo. Los controladores las aplican solo cuando el dato cambia, para no bloquear la edicion de
// otros campos de una persona cargada antes con un documento en otro formato.
public static partial class PersonRules
{
    public const string Ruc = "RUC";
    public const string Cedula = "CedulaParaguaya";
    public const string Dni = "DNI";
    public const string Cuit = "CUIT";
    public const string Cuil = "CUIL";
    public const string CedulaVe = "CedulaVE";
    public const string Rif = "RIF";

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

        // Documentos de otros paises: se valida el formato (largo y caracteres); el digito verificador se agrega cuando cada organismo lo confirme.
        if (string.Equals(type, Dni, StringComparison.OrdinalIgnoreCase))
            return DniRegex().IsMatch(value) ? null : $"{label} (DNI) debe tener entre 6 y 9 números (se aceptan puntos).";

        if (string.Equals(type, Cuit, StringComparison.OrdinalIgnoreCase) || string.Equals(type, Cuil, StringComparison.OrdinalIgnoreCase))
            return value.Count(char.IsDigit) == 11 && value.All(c => char.IsDigit(c) || c == '-')
                ? null
                : $"{label} ({type?.ToUpperInvariant()}) debe tener 11 dígitos (por ejemplo 20-12345678-3).";

        if (string.Equals(type, CedulaVe, StringComparison.OrdinalIgnoreCase))
            return CedulaVeRegex().IsMatch(value) ? null : $"{label} (cédula) debe ser V o E seguida de números (por ejemplo V-12345678).";

        if (string.Equals(type, Rif, StringComparison.OrdinalIgnoreCase))
            return RifRegex().IsMatch(value) ? null : $"{label} (RIF) debe empezar con una letra (J, G, V, E, P o C) y seguir con números (por ejemplo J-12345678-9).";

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

    [GeneratedRegex(@"^[\d.]{6,12}$")]
    private static partial Regex DniRegex();

    [GeneratedRegex(@"^[VvEe]-?\d{5,9}$")]
    private static partial Regex CedulaVeRegex();

    [GeneratedRegex(@"^[JGVEPCjgvepc]-?\d{7,9}(-?\d)?$")]
    private static partial Regex RifRegex();

    [GeneratedRegex(@"^\+?\d{6,20}$")]
    private static partial Regex FullPhoneRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();
}
