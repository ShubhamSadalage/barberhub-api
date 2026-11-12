using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Barbers.Services;
using BarberHub.Api.Features.Bookings.Dtos;
using BarberHub.Api.Features.Bookings.Repositories;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Features.Bookings.Services;

public interface IBookingService
{
    Task<Result<CreateBookingResponseDto>> CreateAsync(string userId, CreateBookingDto dto);
    Task<List<BookingListItemDto>> GetUserBookingsAsync(string userId);
    Task<List<BookingListItemDto>> GetBarberBookingsAsync(string barberId);
    Task<Result> UpdateStatusAsync(string barberId, UpdateBookingStatusDto dto);
    Task<Result> CancelAsync(string userId, Guid bookingId);
    Task<Result<QueuePositionDto>> GetQueuePositionAsync(string userId, Guid bookingId);
    Task<Result> ShiftSlotAsync(string userId, ShiftBookingSlotDto dto);
}

public class BookingService : IBookingService
{
    private readonly IBookingRepository _repo;
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;
    private readonly IBarberService _barberService;
    private readonly IHubContext<NotificationHub> _hub;

    public BookingService(
        IBookingRepository repo,
        ApplicationDbContext context,
        INotificationService notifications,
        IBarberService barberService,
        IHubContext<NotificationHub> hub)
    {
        _repo = repo;
        _context = context;
        _notifications = notifications;
        _barberService = barberService;
        _hub = hub;
    }

    public async Task<Result<CreateBookingResponseDto>> CreateAsync(string userId, CreateBookingDto dto)
    {
        var service = await _context.Services
            .Include(s => s.Barber)
            .FirstOrDefaultAsync(s => s.Id == dto.ServiceId && s.BarberId == dto.BarberId);

        if (service is null)
            return Result<CreateBookingResponseDto>.Failure("Service not found.");

        if (dto.AppointmentDate < DateOnly.FromDateTime(DateTime.Today))
            return Result<CreateBookingResponseDto>.Failure("Cannot book appointments in the past.");

        var endTime = dto.StartTime.AddMinutes(service.DurationMinutes);

        // Weekly holiday check
        if (service.Barber.WeeklyHoliday.HasValue && dto.AppointmentDate.DayOfWeek == service.Barber.WeeklyHoliday.Value)
            return Result<CreateBookingResponseDto>.Failure("Barber is closed on this day.");

        // Leave day check
        var isLeave = await _context.LeaveDays.AnyAsync(l => l.BarberId == dto.BarberId && l.LeaveDate == dto.AppointmentDate);
        if (isLeave)
            return Result<CreateBookingResponseDto>.Failure("Barber is on leave on this day.");

        // Check if slot has already been firmly Accepted by another customer
        if (await _repo.HasAcceptedConflictAsync(dto.BarberId, dto.AppointmentDate, dto.StartTime, endTime))
            return Result<CreateBookingResponseDto>.Failure("This time slot has already been confirmed for another customer. Please select another slot.");

        // Count pending bookings to determine stack queue position
        var pendingCount = await _repo.GetPendingCountAsync(dto.BarberId, dto.AppointmentDate, dto.StartTime, endTime);
        if (pendingCount >= 3)
            return Result<CreateBookingResponseDto>.Failure("This time slot is full (maximum queue limit reached). Please select another slot.");

        var queuePosition = pendingCount + 1;

        var booking = new Booking
        {
            UserId = userId,
            BarberId = dto.BarberId,
            ServiceId = dto.ServiceId,
            AppointmentDate = dto.AppointmentDate,
            StartTime = dto.StartTime,
            EndTime = endTime,
            TotalAmount = service.Price,
            Notes = dto.Notes,
            Status = BookingStatus.Pending
        };

        await _repo.AddAsync(booking);
        await _repo.SaveChangesAsync();

        var user = await _context.Users.FindAsync(userId);
        var userName = user?.FullName ?? "A customer";

        // Notify barber
        if (queuePosition == 1)
        {
            await _notifications.NotifyAsync(
                dto.BarberId,
                NotificationType.BookingCreated,
                "New Booking Request",
                $"{userName} requested an appointment on {dto.AppointmentDate:MMM dd} at {dto.StartTime:HH:mm} ({service.Name}).",
                "/Bookings/Manage");
        }
        else
        {
            await _notifications.NotifyAsync(
                dto.BarberId,
                NotificationType.BookingCreated,
                $"[Queue #{queuePosition}] New Booking Request",
                $"{userName} is #{queuePosition} in queue for {dto.AppointmentDate:MMM dd} at {dto.StartTime:HH:mm} ({service.Name}).",
                "/Bookings/Manage");
        }

        return Result<CreateBookingResponseDto>.Success(new CreateBookingResponseDto
        {
            BookingId = booking.Id,
            SlotQueuePosition = queuePosition,
            IsQueued = queuePosition > 1,
            Message = queuePosition > 1
                ? $"You are #{queuePosition} in queue for this slot. We will notify you when the barber responds."
                : "Booking request submitted successfully."
        });
    }

    public async Task<List<BookingListItemDto>> GetUserBookingsAsync(string userId)
    {
        var list = await _repo.GetByUserAsync(userId);
        var result = new List<BookingListItemDto>();
        foreach (var b in list)
        {
            var dto = MapToDto(b);
            if (b.Status == BookingStatus.Pending)
            {
                dto.SlotQueuePosition = await _repo.GetSlotQueuePositionAsync(b.Id);
            }
            result.Add(dto);
        }
        return result;
    }

    public async Task<List<BookingListItemDto>> GetBarberBookingsAsync(string barberId)
    {
        var list = await _repo.GetByBarberAsync(barberId);
        var result = new List<BookingListItemDto>();
        foreach (var b in list)
        {
            var dto = MapToDto(b);
            if (b.Status == BookingStatus.Pending)
            {
                dto.SlotQueuePosition = await _repo.GetSlotQueuePositionAsync(b.Id);
            }
            result.Add(dto);
        }
        return result;
    }

    public async Task<Result> UpdateStatusAsync(string barberId, UpdateBookingStatusDto dto)
    {
        var booking = await _repo.GetByIdAsync(dto.BookingId);
        if (booking is null) return Result.Failure("Booking not found.");
        if (booking.BarberId != barberId) return Result.Failure("Not authorized.");

        var oldStatus = booking.Status;
        booking.Status = dto.NewStatus;
        booking.RejectionReason = dto.RejectionReason;
        booking.UpdatedAt = DateTime.UtcNow;

        _repo.Update(booking);
        await _repo.SaveChangesAsync();

        // 1. If Barber ACCEPTED this booking:
        if (dto.NewStatus == BookingStatus.Accepted)
        {
            // Notify the lucky customer
            await _notifications.NotifyAsync(
                booking.UserId,
                NotificationType.BookingAccepted,
                "Booking Confirmed! 🎉",
                $"Your appointment with {booking.Barber?.FullName ?? "your barber"} on {booking.AppointmentDate:MMM dd} at {booking.StartTime:HH:mm} is confirmed.",
                "/Bookings/MyBookings");

            // Find all competing Pending bookings for this same overlapping time slot
            var contestedBookings = await _repo.GetOverlappingPendingBookingsAsync(
                booking.BarberId,
                booking.AppointmentDate,
                booking.StartTime,
                booking.EndTime,
                booking.Id);

            foreach (var cb in contestedBookings)
            {
                // Calculate the next available slot for this barber
                var nextSlot = await _barberService.GetNextAvailableSlotAsync(
                    booking.BarberId,
                    booking.AppointmentDate,
                    cb.ServiceId,
                    booking.EndTime);

                var offer = new SlotTakenOfferDto
                {
                    BookingId = cb.Id,
                    BarberId = booking.BarberId,
                    BarberName = booking.Barber?.FullName ?? "Your barber",
                    ShopName = booking.Barber?.ShopName ?? booking.Barber?.FullName ?? "Barber Shop",
                    ServiceId = cb.ServiceId,
                    ServiceName = cb.Service?.Name ?? "Service",
                    Date = cb.AppointmentDate,
                    OldSlot = cb.StartTime,
                    NextAvailableSlot = nextSlot,
                    NextAvailableSlotEnd = nextSlot.HasValue ? nextSlot.Value.AddMinutes(cb.Service?.DurationMinutes ?? 30) : null,
                    TotalAmount = cb.TotalAmount
                };

                // Update contested booking status
                cb.Status = BookingStatus.Rejected;
                cb.RejectionReason = nextSlot.HasValue
                    ? $"Slot booked by another customer. Next available: {nextSlot.Value:HH:mm}"
                    : "Slot booked by another customer. No further slots available today.";
                cb.UpdatedAt = DateTime.UtcNow;
                _repo.Update(cb);

                // Broadcast real-time SignalR SlotTakenOfferNext event to the contested user
                await _hub.Clients.User(cb.UserId).SendAsync("SlotTakenOfferNext", offer);

                // Also send persistent in-app notification
                var message = nextSlot.HasValue
                    ? $"The {cb.StartTime:HH:mm} slot was taken by another customer. Tap here to book the next open slot at {nextSlot.Value:HH:mm}."
                    : $"The {cb.StartTime:HH:mm} slot was taken by another customer. Please choose a different date or time.";

                await _notifications.NotifyAsync(
                    cb.UserId,
                    NotificationType.SlotContested,
                    "Slot Booked — Next Slot Available ⚡",
                    message,
                    "/Bookings/MyBookings");
            }

            if (contestedBookings.Count > 0)
            {
                await _repo.SaveChangesAsync();
            }
        }
        // 2. If Barber REJECTED this booking (or was rejected):
        else if (dto.NewStatus == BookingStatus.Rejected)
        {
            await _notifications.NotifyAsync(
                booking.UserId,
                NotificationType.BookingRejected,
                "Booking Declined",
                $"Your booking request for {booking.AppointmentDate:MMM dd} at {booking.StartTime:HH:mm} was declined. {dto.RejectionReason ?? ""}".Trim(),
                "/Bookings/MyBookings");

            // Promote the next pending booking in the stack if any exists
            var nextInQueue = (await _repo.GetOverlappingPendingBookingsAsync(
                booking.BarberId,
                booking.AppointmentDate,
                booking.StartTime,
                booking.EndTime,
                booking.Id)).FirstOrDefault();

            if (nextInQueue is not null)
            {
                await _notifications.NotifyAsync(
                    nextInQueue.UserId,
                    NotificationType.SlotPromoted,
                    "You're #1 in Line! 🚀",
                    $"The {nextInQueue.StartTime:HH:mm} slot opened up and your request is now top priority for the barber.",
                    "/Bookings/MyBookings");

                await _notifications.NotifyAsync(
                    booking.BarberId,
                    NotificationType.BookingCreated,
                    "Next In Queue Promoted",
                    $"{nextInQueue.User?.FullName ?? "A customer"} is now primary for {nextInQueue.AppointmentDate:MMM dd} at {nextInQueue.StartTime:HH:mm}.",
                    "/Bookings/Manage");
            }
        }
        else if (dto.NewStatus == BookingStatus.Completed)
        {
            await _notifications.NotifyAsync(
                booking.UserId,
                NotificationType.BookingCompleted,
                "Appointment Completed ✂️",
                $"Thank you for visiting {booking.Barber?.ShopName ?? "the barber"}! We hope you loved your look.",
                "/Bookings/MyBookings");
        }

        return Result.Success();
    }

    public async Task<Result> CancelAsync(string userId, Guid bookingId)
    {
        var booking = await _repo.GetByIdAsync(bookingId);
        if (booking is null) return Result.Failure("Booking not found.");
        if (booking.UserId != userId) return Result.Failure("Not authorized.");

        if (booking.Status == BookingStatus.Completed || booking.Status == BookingStatus.Cancelled)
            return Result.Failure("Cannot cancel this booking.");

        var wasPendingOrAccepted = booking.Status == BookingStatus.Pending || booking.Status == BookingStatus.Accepted;

        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAt = DateTime.UtcNow;
        _repo.Update(booking);
        await _repo.SaveChangesAsync();

        // If this cancelled booking had other pending users in stack, promote the next one
        if (wasPendingOrAccepted)
        {
            var nextInQueue = (await _repo.GetOverlappingPendingBookingsAsync(
                booking.BarberId,
                booking.AppointmentDate,
                booking.StartTime,
                booking.EndTime,
                booking.Id)).FirstOrDefault();

            if (nextInQueue is not null)
            {
                await _notifications.NotifyAsync(
                    nextInQueue.UserId,
                    NotificationType.SlotPromoted,
                    "Slot Opened Up! 🚀",
                    $"The {nextInQueue.StartTime:HH:mm} appointment is now ready for you as the primary request.",
                    "/Bookings/MyBookings");

                await _notifications.NotifyAsync(
                    booking.BarberId,
                    NotificationType.BookingCreated,
                    "Queue Shifted — New Primary Request",
                    $"{nextInQueue.User?.FullName ?? "Customer"} is now next in line for {nextInQueue.AppointmentDate:MMM dd} at {nextInQueue.StartTime:HH:mm}.",
                    "/Bookings/Manage");
            }
        }

        return Result.Success();
    }

    public async Task<Result> ShiftSlotAsync(string userId, ShiftBookingSlotDto dto)
    {
        var booking = await _repo.GetByIdAsync(dto.BookingId);
        if (booking is null) return Result.Failure("Booking not found.");
        if (booking.UserId != userId) return Result.Failure("Not authorized.");

        var duration = booking.Service?.DurationMinutes ?? 30;
        var targetDate = dto.NewAppointmentDate ?? booking.AppointmentDate;
        var targetEnd = dto.NewStartTime.AddMinutes(duration);

        if (targetDate < DateOnly.FromDateTime(DateTime.Today))
            return Result.Failure("Cannot shift booking to a past date.");

        // Check if new slot has an accepted conflict
        if (await _repo.HasAcceptedConflictAsync(booking.BarberId, targetDate, dto.NewStartTime, targetEnd))
            return Result.Failure("The selected slot is also confirmed for another customer. Please choose another time.");

        var pendingCount = await _repo.GetPendingCountAsync(booking.BarberId, targetDate, dto.NewStartTime, targetEnd);
        if (pendingCount >= 3)
            return Result.Failure("The selected slot queue is currently full. Please choose another time.");

        booking.AppointmentDate = targetDate;
        booking.StartTime = dto.NewStartTime;
        booking.EndTime = targetEnd;
        booking.Status = BookingStatus.Pending;
        booking.RejectionReason = null;
        booking.UpdatedAt = DateTime.UtcNow;

        _repo.Update(booking);
        await _repo.SaveChangesAsync();

        var queuePos = pendingCount + 1;

        // Notify Barber about the slot shift
        await _notifications.NotifyAsync(
            booking.BarberId,
            NotificationType.BookingCreated,
            "Booking Rescheduled ⚡",
            $"{booking.User?.FullName ?? "Customer"} accepted the next available slot for {targetDate:MMM dd} at {dto.NewStartTime:HH:mm}.",
            "/Bookings/Manage");

        return Result.Success();
    }

    public async Task<Result<QueuePositionDto>> GetQueuePositionAsync(string userId, Guid bookingId)
    {
        var booking = await _repo.GetByIdAsync(bookingId);
        if (booking is null) return Result<QueuePositionDto>.Failure("Booking not found.");
        if (booking.UserId != userId) return Result<QueuePositionDto>.Failure("Not authorized.");

        if (booking.Status != BookingStatus.Accepted)
        {
            return Result<QueuePositionDto>.Success(new QueuePositionDto
            {
                BookingId = booking.Id,
                Status = booking.Status,
                Position = null,
                PeopleAhead = 0
            });
        }

        var nowTime = TimeOnly.FromDateTime(DateTime.Now);
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (booking.AppointmentDate != today)
        {
            return Result<QueuePositionDto>.Success(new QueuePositionDto
            {
                BookingId = booking.Id,
                Status = booking.Status,
                Position = null,
                PeopleAhead = 0
            });
        }

        var todaysAccepted = await _context.Bookings
            .Where(b => b.BarberId == booking.BarberId
                     && b.Status == BookingStatus.Accepted
                     && b.AppointmentDate == today
                     && b.EndTime > nowTime)
            .OrderBy(b => b.StartTime)
            .ToListAsync();

        var position = todaysAccepted.FindIndex(b => b.Id == booking.Id);
        if (position < 0)
        {
            // Booking's slot has already passed
            return Result<QueuePositionDto>.Success(new QueuePositionDto
            {
                BookingId = booking.Id,
                Status = booking.Status,
                Position = null,
                PeopleAhead = 0
            });
        }

        return Result<QueuePositionDto>.Success(new QueuePositionDto
        {
            BookingId = booking.Id,
            Status = booking.Status,
            Position = position,
            PeopleAhead = position
        });
    }

    private static BookingListItemDto MapToDto(Booking b) => new()
    {
        Id = b.Id,
        BarberId = b.BarberId,
        BarberName = b.Barber?.FullName ?? "",
        ShopName = b.Barber?.ShopName ?? b.Barber?.FullName ?? "",
        UserId = b.UserId,
        UserName = b.User?.FullName ?? "",
        UserPhone = b.User?.PhoneNumber ?? "",
        ServiceName = b.Service?.Name ?? "",
        AppointmentDate = b.AppointmentDate,
        StartTime = b.StartTime,
        EndTime = b.EndTime,
        Status = b.Status,
        SlotQueuePosition = 1,
        TotalAmount = b.TotalAmount,
        Notes = b.Notes,
        RejectionReason = b.RejectionReason,
        CreatedAt = b.CreatedAt
    };
}
