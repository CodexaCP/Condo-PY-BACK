namespace Condo.Api.Services;

// Catalogo en codigo de los proveedores de pago y sus modalidades. Cada modalidad declara los campos de credencial que pide (la pantalla de
// configuracion se dibuja a partir de este catalogo) y sus capacidades. Sumar un proveedor es sumar una entrada aqui y su adaptador; no toca
// el modelo de datos. Es un catalogo en codigo y no una tabla a proposito: no cambia en tiempo de ejecucion.

/// <summary>Un campo de credencial. Secret: clave privada (cifrada, de solo escritura). Required: hace falta para poder activar la conexion.</summary>
public sealed record PaymentField(string Key, string Label, bool Secret, bool Required, int MaxLength, string? Help = null);

/// <summary>Lo que cada modalidad puede o no hacer. Informativo para quien configura; los adaptadores lo respetan.</summary>
public sealed record PaymentCapabilities(
    bool SignedCallback, bool SupportsReversal, string ReversalNote, bool EmbeddedCheckout, string[] Notes);

public sealed record PaymentProductDefinition(
    string Code, string Name, string Description, IReadOnlyList<PaymentField> Fields, PaymentCapabilities Capabilities);

public sealed record PaymentProviderDefinition(string Code, string Name, IReadOnlyList<PaymentProductDefinition> Products);

public static class PaymentProviderCatalog
{
    public const string Bancard = "Bancard";
    public const string Tpago = "Tpago";
    public const string VPos = "VPos";

    public static readonly IReadOnlyList<PaymentProviderDefinition> All =
    [
        new(Bancard, "Bancard",
        [
            new(Tpago, "Tpago (link de pago y API)",
                "El propietario paga en una página de Bancard a la que se lo redirige. Bancard avisa el resultado a Condo-PY por una dirección de callback.",
                [
                    new("commerceCode", "Código de comercio (commerce_code)", false, true, 40,
                        "Lo entrega Bancard al dar de alta el comercio."),
                    new("branchCode", "Código de sucursal (commerce_branch_code)", false, true, 40,
                        "Sucursal del comercio. Si Bancard no asignó sucursales, usá el código que figura en el portal."),
                    new("publicKey", "Clave pública", false, true, 200,
                        "No es secreta: identifica al comercio."),
                    new("privateKey", "Clave privada", true, true, 200,
                        "Es secreta: se guarda cifrada y no se vuelve a mostrar. Para cambiarla, cargá una nueva.")
                ],
                new PaymentCapabilities(
                    SignedCallback: false,
                    SupportsReversal: true,
                    ReversalNote: "Solo el mismo día del cobro.",
                    EmbeddedCheckout: false,
                    Notes:
                    [
                        "El aviso de Bancard no va firmado: se acepta solo desde sus direcciones IP, por TLS 1.2 y puerto 443, y cada pago se confirma consultando a Bancard.",
                        "Registrá la dirección de callback en el portal de comercios de Bancard antes de activar la conexión.",
                        "Usá conexiones distintas para el ambiente de pruebas (sandbox) y para producción: tienen claves distintas."
                    ])),
            new(VPos, "vPOS 2.0 (formulario de pago)",
                "El formulario de pago de Bancard se muestra dentro de Condo-PY. Bancard confirma el resultado a Condo-PY con un token firmado.",
                [
                    new("publicKey", "Clave pública", false, true, 200,
                        "No es secreta: identifica al comercio."),
                    new("privateKey", "Clave privada", true, true, 200,
                        "Es secreta: se guarda cifrada y no se vuelve a mostrar. Para cambiarla, cargá una nueva.")
                ],
                new PaymentCapabilities(
                    SignedCallback: true,
                    SupportsReversal: true,
                    ReversalNote: "Solo antes de que la operación se cupone.",
                    EmbeddedCheckout: true,
                    Notes:
                    [
                        "Bancard puede exigir que cada comercio pase su propia certificación antes de pasar a producción: consultalo con ellos.",
                        "Usá conexiones distintas para el ambiente de pruebas (sandbox) y para producción: tienen claves distintas."
                    ]))
        ]),
        new(FakeProvider, "Simulador (solo pruebas)",
        [
            new(FakeProduct, "Simulador de cobros",
                "Proveedor de mentira para probar el cobro completo sin dinero ni Bancard. Solo existe en entornos de prueba y nunca en producción.",
                [
                    new("privateKey", "Clave de firma", true, true, 200,
                        "Cualquier texto largo: con ella se firma el aviso del simulador. Es secreta y se guarda cifrada.")
                ],
                new PaymentCapabilities(
                    SignedCallback: true,
                    SupportsReversal: false,
                    ReversalNote: "El simulador no revierte cobros.",
                    EmbeddedCheckout: false,
                    Notes:
                    [
                        "Un cobro del simulador solo se aplica a la deuda en edificios marcados como de prueba; en cualquier otro queda registrado como simulado y no crea ningún pago."
                    ]))
        ])
    ];

    public const string FakeProvider = "Fake";
    public const string FakeProduct = "Simulator";

    /// <summary>Proveedores que se ofrecen para crear conexiones: el simulador solo si el servidor lo habilitó (Payments:EnableFakeProvider).</summary>
    public static IEnumerable<PaymentProviderDefinition> Visible(bool fakeEnabled) =>
        All.Where(p => fakeEnabled || !string.Equals(p.Code, FakeProvider, StringComparison.OrdinalIgnoreCase));

    public static PaymentProviderDefinition? FindProvider(string? providerCode) =>
        All.FirstOrDefault(x => string.Equals(x.Code, providerCode, StringComparison.OrdinalIgnoreCase));

    public static PaymentProductDefinition? FindProduct(string? providerCode, string? productCode) =>
        FindProvider(providerCode)?.Products.FirstOrDefault(x => string.Equals(x.Code, productCode, StringComparison.OrdinalIgnoreCase));
}
