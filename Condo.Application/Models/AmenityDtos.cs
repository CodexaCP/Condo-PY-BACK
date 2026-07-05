using Condo.Domain.Enums;

namespace Condo.Application.Models;

public class AmenityDto
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal ReservationPrice { get; set; }
    public bool IsActive { get; set; }
}

public class AmenityUpsertRequest
{
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal ReservationPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AmenityReservationDto
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public string AmenityName { get; set; } = string.Empty;
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public decimal Price { get; set; }
    public AmenityReservationStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? ComprobanteUrl { get; set; }
    public string ReservedByName { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Franja ocupada visible para cualquier usuario del edificio (sin datos del reservante).</summary>
public class AmenityScheduleSlotDto
{
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public AmenityReservationStatus Status { get; set; }
}

public class CreateAmenityReservationRequest
{
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class AmenityReservationComprobanteRequest
{
    public string ComprobanteUrl { get; set; } = string.Empty;
}

public class AmenityReservationReviewRequest
{
    public bool Approve { get; set; }
    public string? RejectionReason { get; set; }
}
