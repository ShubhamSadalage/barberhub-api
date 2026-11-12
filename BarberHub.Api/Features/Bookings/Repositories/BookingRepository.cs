using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Features.Bookings.Repositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid id);
    Task<List<Booking>> GetByUserAsync(string userId);
    Task<List<Booking>> GetByBarberAsync(string barberId);
    Task<bool> HasAcceptedConflictAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end);
    Task<int> GetPendingCountAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end);
    Task<bool> HasConflictAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end);
    Task<List<Booking>> GetOverlappingPendingBookingsAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end, Guid? excludeBookingId = null);
    Task<int> GetSlotQueuePositionAsync(Guid bookingId);
    Task AddAsync(Booking booking);
    void Update(Booking booking);
    Task<int> SaveChangesAsync();
}

public class BookingRepository : IBookingRepository
{
    private readonly ApplicationDbContext _context;

    public BookingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Booking?> GetByIdAsync(Guid id)
    {
        return await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.Barber)
            .Include(b => b.Service)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<List<Booking>> GetByUserAsync(string userId)
    {
        return await _context.Bookings
            .Include(b => b.Barber)
            .Include(b => b.Service)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.AppointmentDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();
    }

    public async Task<List<Booking>> GetByBarberAsync(string barberId)
    {
        return await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.Service)
            .Where(b => b.BarberId == barberId)
            .OrderByDescending(b => b.AppointmentDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();
    }

    public async Task<bool> HasAcceptedConflictAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        return await _context.Bookings.AnyAsync(b =>
            b.BarberId == barberId &&
            b.AppointmentDate == date &&
            b.Status == BookingStatus.Accepted &&
            ((start >= b.StartTime && start < b.EndTime) ||
             (end > b.StartTime && end <= b.EndTime) ||
             (start <= b.StartTime && end >= b.EndTime)));
    }

    public async Task<int> GetPendingCountAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        return await _context.Bookings.CountAsync(b =>
            b.BarberId == barberId &&
            b.AppointmentDate == date &&
            b.Status == BookingStatus.Pending &&
            ((start >= b.StartTime && start < b.EndTime) ||
             (end > b.StartTime && end <= b.EndTime) ||
             (start <= b.StartTime && end >= b.EndTime)));
    }

    public async Task<bool> HasConflictAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        // Accepted booking always creates a conflict
        if (await HasAcceptedConflictAsync(barberId, date, start, end)) return true;

        // Max stacking capacity is 3 pending users per slot
        var pendingCount = await GetPendingCountAsync(barberId, date, start, end);
        return pendingCount >= 3;
    }

    public async Task<List<Booking>> GetOverlappingPendingBookingsAsync(string barberId, DateOnly date, TimeOnly start, TimeOnly end, Guid? excludeBookingId = null)
    {
        var query = _context.Bookings
            .Include(b => b.User)
            .Include(b => b.Barber)
            .Include(b => b.Service)
            .Where(b =>
                b.BarberId == barberId &&
                b.AppointmentDate == date &&
                b.Status == BookingStatus.Pending &&
                ((start >= b.StartTime && start < b.EndTime) ||
                 (end > b.StartTime && end <= b.EndTime) ||
                 (start <= b.StartTime && end >= b.EndTime)));

        if (excludeBookingId.HasValue)
        {
            query = query.Where(b => b.Id != excludeBookingId.Value);
        }

        return await query.OrderBy(b => b.CreatedAt).ToListAsync();
    }

    public async Task<int> GetSlotQueuePositionAsync(Guid bookingId)
    {
        var target = await _context.Bookings.FindAsync(bookingId);
        if (target is null || target.Status != BookingStatus.Pending) return 1;

        var overlapping = await _context.Bookings
            .Where(b =>
                b.BarberId == target.BarberId &&
                b.AppointmentDate == target.AppointmentDate &&
                b.Status == BookingStatus.Pending &&
                ((target.StartTime >= b.StartTime && target.StartTime < b.EndTime) ||
                 (target.EndTime > b.StartTime && target.EndTime <= b.EndTime) ||
                 (target.StartTime <= b.StartTime && target.EndTime >= b.EndTime)))
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .ToListAsync();

        var index = overlapping.IndexOf(bookingId);
        return index >= 0 ? index + 1 : 1;
    }

    public async Task AddAsync(Booking booking) => await _context.Bookings.AddAsync(booking);
    public void Update(Booking booking) => _context.Bookings.Update(booking);
    public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
}
