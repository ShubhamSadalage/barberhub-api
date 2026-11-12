using System.ComponentModel.DataAnnotations;
using BarberHub.Api.Domain.Entities;

namespace BarberHub.Api.Features.Bookings.Dtos;

public class CreateBookingDto
{
    [Required]
    public string BarberId { get; set; } = string.Empty;

    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public DateOnly AppointmentDate { get; set; }

    [Required]
    public TimeOnly StartTime { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class CreateBookingResponseDto
{
    public Guid BookingId { get; set; }
    public int SlotQueuePosition { get; set; } = 1;
    public bool IsQueued { get; set; }
    public string Message { get; set; } = "Booking request submitted.";
}

public class ShiftBookingSlotDto
{
    [Required]
    public Guid BookingId { get; set; }

    [Required]
    public TimeOnly NewStartTime { get; set; }

    public DateOnly? NewAppointmentDate { get; set; }
}

public class SlotTakenOfferDto
{
    public Guid BookingId { get; set; }
    public string BarberId { get; set; } = string.Empty;
    public string BarberName { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly OldSlot { get; set; }
    public TimeOnly? NextAvailableSlot { get; set; }
    public TimeOnly? NextAvailableSlotEnd { get; set; }
    public decimal TotalAmount { get; set; }
}

public class BookingListItemDto
{
    public Guid Id { get; set; }
    public string BarberId { get; set; } = string.Empty;
    public string BarberName { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserPhone { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public BookingStatus Status { get; set; }
    public int SlotQueuePosition { get; set; } = 1;
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateBookingStatusDto
{
    [Required]
    public Guid BookingId { get; set; }

    [Required]
    public BookingStatus NewStatus { get; set; }

    [StringLength(500)]
    public string? RejectionReason { get; set; }
}

/// <summary>Live queue position for a customer's own Accepted booking, for on-demand polling in addition to push notifications.</summary>
public class QueuePositionDto
{
    public Guid BookingId { get; set; }
    public BookingStatus Status { get; set; }
    /// <summary>0 = your turn, 1 = one ahead of you, etc. Null if not applicable (not Accepted, not today, or already passed).</summary>
    public int? Position { get; set; }
    public int PeopleAhead { get; set; }
}
