namespace Condo.Application.Models;

/// <summary>Una fila de la vista previa de la carga masiva de propietarios o de residentes.</summary>
public class PeopleImportRowDto
{
    public int RowNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Building { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    // "Nueva" (se crea) o "Ya existe" (se reutiliza); vacío si la fila tiene error.
    public string Person { get; set; } = string.Empty;

    // "Se asigna", "Ya estaba asignada" o "Sin unidad" (solo alta de la persona); vacío si la fila tiene error.
    public string Assignment { get; set; } = string.Empty;

    public string? Error { get; set; }
}

public class PeopleImportResultDto
{
    public bool Confirmed { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ErrorRows { get; set; }

    // Lo que se creará (vista previa) o lo que se creó (tras confirmar).
    public int NewPeople { get; set; }
    public int ExistingPeople { get; set; }
    public int NewAssignments { get; set; }
    public int AlreadyAssigned { get; set; }

    // Solo propietarios: la contraseña inicial que reciben las cuentas nuevas (deben cambiarla al ingresar).
    public string? InitialPassword { get; set; }

    public bool Created { get; set; }
    public string? Message { get; set; }
    public List<PeopleImportRowDto> Rows { get; set; } = [];
}
