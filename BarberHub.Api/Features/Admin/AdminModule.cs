using System.ComponentModel.DataAnnotations;
using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Config;
using BarberHub.Api.Shared.Filters;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarberHub.Api.Features.Admin;

// =================== DTOs ===================

public class UserListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? City { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}

public class LeaveDayDto
{
    public Guid Id { get; set; }
    [Required] public DateOnly LeaveDate { get; set; }
    [StringLength(300)] public string? Reason { get; set; }
}

public class WeeklyHolidayDto
{
    [Required] public DayOfWeek WeeklyHoliday { get; set; }
}

public class AdminAddUserDto
{
    [Required, StringLength(120)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; } = string.Empty;
    [Phone, StringLength(20)] public string? PhoneNumber { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [Required, StringLength(100, MinimumLength = 6)] public string Password { get; set; } = "Welcome@123";
}

public class AdminCreateBookingDto
{
    [Required] public string UserId { get; set; } = string.Empty;
    [Required] public Guid ServiceId { get; set; }
    [Required] public DateOnly AppointmentDate { get; set; }
    [Required] public TimeOnly StartTime { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}

public class ServiceOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class UserOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class DashboardSummaryDto
{
    public int BookingsCount { get; set; }
    public int ProductsCount { get; set; }
    public int PendingBookings { get; set; }
    public int TodayBookings { get; set; }
    public decimal TotalEarnings { get; set; }
}

public class EarningsDto
{
    // ----- Service Earnings (Tab 1) -----
    public decimal ServiceTotalEarnings { get; set; }
    public decimal ServiceTodayEarnings { get; set; }
    public decimal ServiceThisWeekEarnings { get; set; }
    public decimal ServiceThisMonthEarnings { get; set; }
    public int ServiceTotalCompleted { get; set; }
    public int ServiceTotalAccepted { get; set; }
    public List<DailyEarningRow> DailyBreakdown { get; set; } = new();
    public List<ServiceEarningRow> ByService { get; set; } = new();

    // Compatibility aliases
    public decimal TotalEarnings { get; set; }
    public decimal TodayEarnings { get; set; }
    public decimal ThisWeekEarnings { get; set; }
    public decimal ThisMonthEarnings { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalAccepted { get; set; }

    // ----- Product Selling Financials (Tab 2) -----
    public decimal ProductTotalRevenue { get; set; }
    public decimal ProductTodayRevenue { get; set; }
    public decimal ProductThisWeekRevenue { get; set; }
    public decimal ProductThisMonthRevenue { get; set; }
    public decimal ProductTotalCost { get; set; }
    public decimal ProductNetProfit { get; set; }
    public decimal ProductProfitMarginPercentage { get; set; }
    public int ProductTotalUnitsSold { get; set; }
    public int ProductTotalOrders { get; set; }
    public List<ProductFinancialRow> ByProduct { get; set; } = new();
    public List<DailyProductEarningRow> ProductDailyBreakdown { get; set; } = new();

    // ----- Combined Overview -----
    public decimal CombinedTotalRevenue { get; set; }
    public decimal CombinedTotalProfit { get; set; }
}

public class DailyEarningRow
{
    public DateOnly Date { get; set; }
    public int BookingsCount { get; set; }
    public decimal Amount { get; set; }
}

public class ServiceEarningRow
{
    public string ServiceName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public class ProductFinancialRow
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int UnitsSold { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal NetProfit { get; set; }
    public decimal ProfitMarginPercentage { get; set; }
    public int CurrentStock { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? CostPrice { get; set; }
}

public class DailyProductEarningRow
{
    public DateOnly Date { get; set; }
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
}

// =================== Controller ===================

[ApiController]
[Route("api/admin")]
[Authorize(Roles = AppRoles.Admin)]
[RequireApprovedBarber]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;
    private readonly FeatureFlags _features;

    public AdminController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUser,
        INotificationService notifications,
        IOptions<FeatureFlags> features)
    {
        _context = context;
        _userManager = userManager;
        _currentUser = currentUser;
        _notifications = notifications;
        _features = features.Value;
    }

    // ----------- Dashboard -----------
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var barberId = _currentUser.UserId!;
        var dto = new DashboardSummaryDto
        {
            BookingsCount = await _context.Bookings.CountAsync(b => b.BarberId == barberId),
            ProductsCount = _features.ProductsEnabled ? await _context.Products.CountAsync(p => p.BarberId == barberId) : 0,
            PendingBookings = await _context.Bookings.CountAsync(b => b.BarberId == barberId && b.Status == BookingStatus.Pending),
            TodayBookings = await _context.Bookings.CountAsync(b => b.BarberId == barberId && b.AppointmentDate == DateOnly.FromDateTime(DateTime.Today)),
            TotalEarnings = _features.EarningsFinancialsEnabled
                ? (await _context.Bookings
                    .Where(b => b.BarberId == barberId && (b.Status == BookingStatus.Completed || b.Status == BookingStatus.Accepted))
                    .SumAsync(b => (decimal?)b.TotalAmount) ?? 0m)
                : 0m
        };
        return Ok(dto);
    }

    // ----------- Customers -----------
    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        var usersInRole = await _userManager.GetUsersInRoleAsync(AppRoles.User);
        var list = usersInRole.Select(u => new UserListItemDto
        {
            Id = u.Id, FullName = u.FullName, Email = u.Email ?? "", PhoneNumber = u.PhoneNumber,
            City = u.City, CreatedAt = u.CreatedAt, IsActive = u.IsActive
        }).OrderByDescending(u => u.CreatedAt).ToList();
        return Ok(list);
    }

    [HttpPost("users")]
    public async Task<IActionResult> AddUser(AdminAddUserDto dto)
    {
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing is not null)
            return BadRequest(new { error = "A user with this email already exists." });

        var user = new ApplicationUser
        {
            UserName = dto.Email, Email = dto.Email, EmailConfirmed = true, FullName = dto.FullName,
            PhoneNumber = dto.PhoneNumber, PhoneNumberConfirmed = !string.IsNullOrEmpty(dto.PhoneNumber),
            City = dto.City, IsActive = true, CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, AppRoles.User);
        return Ok(new { message = $"Customer '{user.FullName}' added successfully.", userId = user.Id });
    }

    // ----------- Book on behalf of a customer -----------
    [HttpGet("bookings/create-options")]
    public async Task<IActionResult> CreateBookingOptions()
    {
        var barberId = _currentUser.UserId!;
        var users = await _userManager.GetUsersInRoleAsync(AppRoles.User);
        var userOptions = users.OrderBy(u => u.FullName)
            .Select(u => new UserOptionDto { Id = u.Id, Label = $"{u.FullName} ({u.Email})" }).ToList();

        var services = await _context.Services
            .Where(s => s.BarberId == barberId && s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new ServiceOptionDto { Id = s.Id, Label = $"{s.Name} — ₹{s.Price} ({s.DurationMinutes} min)" })
            .ToListAsync();

        return Ok(new { users = userOptions, services });
    }

    [HttpPost("bookings")]
    public async Task<IActionResult> CreateBooking(AdminCreateBookingDto dto)
    {
        var barberId = _currentUser.UserId!;

        var service = await _context.Services
            .FirstOrDefaultAsync(s => s.Id == dto.ServiceId && s.BarberId == barberId && s.IsActive);
        if (service is null) return BadRequest(new { error = "Selected service not found." });

        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user is null) return BadRequest(new { error = "Selected customer not found." });

        var endTime = dto.StartTime.AddMinutes(service.DurationMinutes);

        var conflict = await _context.Bookings.AnyAsync(b =>
            b.BarberId == barberId &&
            b.AppointmentDate == dto.AppointmentDate &&
            b.Status != BookingStatus.Cancelled &&
            b.Status != BookingStatus.Rejected &&
            b.StartTime < endTime &&
            b.EndTime > dto.StartTime);
        if (conflict) return BadRequest(new { error = "This time slot conflicts with an existing booking." });

        var barber = await _userManager.FindByIdAsync(barberId);
        if (barber?.WeeklyHoliday is not null && (int)barber.WeeklyHoliday == (int)dto.AppointmentDate.DayOfWeek)
            return BadRequest(new { error = "Selected date is your weekly holiday." });

        var onLeave = await _context.LeaveDays.AnyAsync(l => l.BarberId == barberId && l.LeaveDate == dto.AppointmentDate);
        if (onLeave) return BadRequest(new { error = "You have marked this date as a leave day." });

        var booking = new Booking
        {
            UserId = dto.UserId,
            BarberId = barberId,
            ServiceId = service.Id,
            AppointmentDate = dto.AppointmentDate,
            StartTime = dto.StartTime,
            EndTime = endTime,
            Status = BookingStatus.Accepted,   // barber-created = auto-accepted
            Notes = dto.Notes,
            TotalAmount = service.Price,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Bookings.AddAsync(booking);
        await _context.SaveChangesAsync();

        await _notifications.NotifyAsync(
            dto.UserId,
            NotificationType.BookingAccepted,
            "Booking confirmed",
            $"Your barber has booked you for {service.Name} on {dto.AppointmentDate:MMM dd} at {dto.StartTime:HH:mm}.",
            "Bookings/MyBookings");

        return Ok(new { bookingId = booking.Id, message = $"Booking confirmed for {user.FullName} on {dto.AppointmentDate:MMM dd} at {dto.StartTime:HH:mm}." });
    }

    // ----------- Earnings -----------
    [HttpGet("earnings")]
    public async Task<IActionResult> Earnings(DateOnly? from = null, DateOnly? to = null)
    {
        if (!_features.EarningsFinancialsEnabled)
            return Ok(new EarningsDto());

        var barberId = _currentUser.UserId!;
        var rangeFrom = from ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-29));
        var rangeTo = to ?? DateOnly.FromDateTime(DateTime.Today);
        if (rangeTo < rangeFrom) (rangeFrom, rangeTo) = (rangeTo, rangeFrom);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var startOfWeek = today.AddDays(-(int)DateTime.Today.DayOfWeek);
        var startOfMonth = new DateOnly(today.Year, today.Month, 1);

        // 1. Service Earnings
        var earningStatuses = new[] { BookingStatus.Completed, BookingStatus.Accepted };
        var allEarningBookings = await _context.Bookings
            .Where(b => b.BarberId == barberId && earningStatuses.Contains(b.Status))
            .Include(b => b.Service)
            .ToListAsync();

        var serviceTotal = allEarningBookings.Sum(b => b.TotalAmount);
        var serviceToday = allEarningBookings.Where(b => b.AppointmentDate == today).Sum(b => b.TotalAmount);
        var serviceWeek = allEarningBookings.Where(b => b.AppointmentDate >= startOfWeek).Sum(b => b.TotalAmount);
        var serviceMonth = allEarningBookings.Where(b => b.AppointmentDate >= startOfMonth).Sum(b => b.TotalAmount);
        var serviceCompleted = allEarningBookings.Count(b => b.Status == BookingStatus.Completed);
        var serviceAccepted = allEarningBookings.Count(b => b.Status == BookingStatus.Accepted);

        var serviceDaily = allEarningBookings
            .Where(b => b.AppointmentDate >= rangeFrom && b.AppointmentDate <= rangeTo)
            .GroupBy(b => b.AppointmentDate)
            .Select(g => new DailyEarningRow { Date = g.Key, BookingsCount = g.Count(), Amount = g.Sum(b => b.TotalAmount) })
            .OrderByDescending(r => r.Date).ToList();

        var byService = allEarningBookings
            .Where(b => b.AppointmentDate >= rangeFrom && b.AppointmentDate <= rangeTo)
            .GroupBy(b => b.Service.Name)
            .Select(g => new ServiceEarningRow { ServiceName = g.Key, Count = g.Count(), Amount = g.Sum(b => b.TotalAmount) })
            .OrderByDescending(r => r.Amount).ToList();

        // 2. Product Selling Financials
        var paidOrderStatuses = new[] { OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Delivered };
        var orderItems = await _context.OrderItems
            .Include(i => i.Product)
            .Include(i => i.Order)
            .Where(i => i.Product.BarberId == barberId && paidOrderStatuses.Contains(i.Order.Status))
            .ToListAsync();

        var barberProducts = await _context.Products
            .Where(p => p.BarberId == barberId && !p.IsDeleted)
            .ToListAsync();

        var prodTotalRev = orderItems.Sum(i => i.UnitPrice * i.Quantity);
        var prodTotalCost = orderItems.Sum(i => (i.UnitCostPrice ?? i.Product.CostPrice ?? 0m) * i.Quantity);
        var prodNetProfit = prodTotalRev - prodTotalCost;
        var prodProfitMargin = prodTotalRev > 0 ? Math.Round((prodNetProfit / prodTotalRev) * 100m, 2) : 0m;
        var prodUnitsSold = orderItems.Sum(i => i.Quantity);
        var prodOrdersCount = orderItems.Select(i => i.OrderId).Distinct().Count();

        var prodTodayRev = orderItems.Where(i => DateOnly.FromDateTime(i.Order.CreatedAt.Date) == today).Sum(i => i.UnitPrice * i.Quantity);
        var prodWeekRev = orderItems.Where(i => DateOnly.FromDateTime(i.Order.CreatedAt.Date) >= startOfWeek).Sum(i => i.UnitPrice * i.Quantity);
        var prodMonthRev = orderItems.Where(i => DateOnly.FromDateTime(i.Order.CreatedAt.Date) >= startOfMonth).Sum(i => i.UnitPrice * i.Quantity);

        // Product rows
        var byProduct = barberProducts.Select(p =>
        {
            var soldItems = orderItems.Where(i => i.ProductId == p.Id).ToList();
            var units = soldItems.Sum(i => i.Quantity);
            var rev = soldItems.Sum(i => i.UnitPrice * i.Quantity);
            var cost = soldItems.Sum(i => (i.UnitCostPrice ?? p.CostPrice ?? 0m) * i.Quantity);
            var profit = rev - cost;
            var margin = rev > 0 ? Math.Round((profit / rev) * 100m, 2) : 0m;

            return new ProductFinancialRow
            {
                ProductId = p.Id,
                ProductName = p.Name,
                UnitsSold = units,
                TotalRevenue = rev,
                TotalCost = cost,
                NetProfit = profit,
                ProfitMarginPercentage = margin,
                CurrentStock = p.StockQuantity,
                SellingPrice = p.EffectivePrice,
                CostPrice = p.CostPrice
            };
        }).OrderByDescending(r => r.TotalRevenue).ToList();

        // Product Daily breakdown
        var prodDaily = orderItems
            .Where(i =>
            {
                var d = DateOnly.FromDateTime(i.Order.CreatedAt.Date);
                return d >= rangeFrom && d <= rangeTo;
            })
            .GroupBy(i => DateOnly.FromDateTime(i.Order.CreatedAt.Date))
            .Select(g =>
            {
                var r = g.Sum(i => i.UnitPrice * i.Quantity);
                var c = g.Sum(i => (i.UnitCostPrice ?? i.Product.CostPrice ?? 0m) * i.Quantity);
                return new DailyProductEarningRow
                {
                    Date = g.Key,
                    UnitsSold = g.Sum(i => i.Quantity),
                    Revenue = r,
                    Cost = c,
                    Profit = r - c
                };
            })
            .OrderByDescending(r => r.Date).ToList();

        var dto = new EarningsDto
        {
            // Service tab
            ServiceTotalEarnings = serviceTotal,
            ServiceTodayEarnings = serviceToday,
            ServiceThisWeekEarnings = serviceWeek,
            ServiceThisMonthEarnings = serviceMonth,
            ServiceTotalCompleted = serviceCompleted,
            ServiceTotalAccepted = serviceAccepted,
            DailyBreakdown = serviceDaily,
            ByService = byService,

            // Backward-compatibility aliases
            TotalEarnings = serviceTotal,
            TodayEarnings = serviceToday,
            ThisWeekEarnings = serviceWeek,
            ThisMonthEarnings = serviceMonth,
            TotalCompleted = serviceCompleted,
            TotalAccepted = serviceAccepted,

            // Product tab
            ProductTotalRevenue = prodTotalRev,
            ProductTodayRevenue = prodTodayRev,
            ProductThisWeekRevenue = prodWeekRev,
            ProductThisMonthRevenue = prodMonthRev,
            ProductTotalCost = prodTotalCost,
            ProductNetProfit = prodNetProfit,
            ProductProfitMarginPercentage = prodProfitMargin,
            ProductTotalUnitsSold = prodUnitsSold,
            ProductTotalOrders = prodOrdersCount,
            ByProduct = byProduct,
            ProductDailyBreakdown = prodDaily,

            // Combined
            CombinedTotalRevenue = serviceTotal + prodTotalRev,
            CombinedTotalProfit = serviceTotal + prodNetProfit
        };

        return Ok(dto);
    }

    // ----------- Leave days + weekly holiday -----------
    [HttpGet("leave-days")]
    public async Task<IActionResult> LeaveDays()
    {
        var barberId = _currentUser.UserId!;
        var leaves = await _context.LeaveDays
            .Where(l => l.BarberId == barberId && l.LeaveDate >= DateOnly.FromDateTime(DateTime.Today.AddDays(-30)))
            .OrderBy(l => l.LeaveDate).ToListAsync();

        var dtos = leaves.Select(l => new LeaveDayDto { Id = l.Id, LeaveDate = l.LeaveDate, Reason = l.Reason }).ToList();
        var barber = await _userManager.FindByIdAsync(barberId);

        return Ok(new { weeklyHoliday = barber?.WeeklyHoliday, leaves = dtos });
    }

    [HttpPost("leave-days")]
    public async Task<IActionResult> AddLeaveDay(LeaveDayDto dto)
    {
        var barberId = _currentUser.UserId!;
        if (dto.LeaveDate < DateOnly.FromDateTime(DateTime.Today))
            return BadRequest(new { error = "Cannot add leave in the past." });

        var existing = await _context.LeaveDays.FirstOrDefaultAsync(l => l.BarberId == barberId && l.LeaveDate == dto.LeaveDate);
        if (existing is not null)
            return BadRequest(new { error = "Leave already set for this date." });

        await _context.LeaveDays.AddAsync(new LeaveDay { BarberId = barberId, LeaveDate = dto.LeaveDate, Reason = dto.Reason });
        await _context.SaveChangesAsync();
        return Ok(new { message = "Leave day added." });
    }

    [HttpDelete("leave-days/{id:guid}")]
    public async Task<IActionResult> RemoveLeaveDay(Guid id)
    {
        var barberId = _currentUser.UserId!;
        var leave = await _context.LeaveDays.FirstOrDefaultAsync(l => l.Id == id && l.BarberId == barberId);
        if (leave is null) return NotFound();

        _context.LeaveDays.Remove(leave);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Leave removed." });
    }

    [HttpPut("weekly-holiday")]
    public async Task<IActionResult> SetWeeklyHoliday(WeeklyHolidayDto dto)
    {
        var barber = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (barber is null) return NotFound();

        barber.WeeklyHoliday = dto.WeeklyHoliday;
        await _userManager.UpdateAsync(barber);
        return Ok(new { message = $"Weekly holiday set to {dto.WeeklyHoliday}." });
    }
}
