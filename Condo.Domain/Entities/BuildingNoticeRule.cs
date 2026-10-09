using Condo.Domain.Common;
using Condo.Domain.Enums;

namespace Condo.Domain.Entities;

// Regla de aviso automatico de un edificio (Centro de configuracion, seccion "Documentos y comunicacion"). Una fila por tipo y edificio.
// Sin fila se usa el comportamiento por defecto: los avisos que ya existian (pago recibido, periodo publicado) siguen encendidos y los
// nuevos (antes del vencimiento, el dia del vencimiento, mora aplicada) apagados, asi que no cambia nada hasta que alguien los configure.
public class BuildingNoticeRule : CompanyScopedEntity
{
    public Guid BuildingId { get; set; }
    public NoticeKind Kind { get; set; }

    // Solo para BeforeDue: cuantos dias antes del vencimiento se avisa (1 a 30).
    public int? OffsetDays { get; set; }

    public bool IsActive { get; set; }

    public Company? Company { get; set; }
    public Building? Building { get; set; }
}
