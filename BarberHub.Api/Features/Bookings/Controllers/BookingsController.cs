using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Bookings.Dtos;
using BarberHub.Api.Features.Bookings.Services;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Filters;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Features.Bookings.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;
    private readonly ApplicationDbContext _db;

    public BookingsController(
        IBookingService service,
        ICurrentUserService currentUser,
        INotificationService notifications,
        ApplicationDbContext db)
    {
        _service = service;
        _currentUser = currentUser;
        _notifications = notifications;
        _db = db;
    }

    // ================= Customer =================

    [HttpPost]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> Create(CreateBookingDto dto)
    {
        var result = await _service.CreateAsync(_currentUser.UserId!, dto);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("shift-slot")]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> ShiftSlot(ShiftBookingSlotDto dto)
    {
        var result = await _service.ShiftSlotAsync(_currentUser.UserId!, dto);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });
        return Ok(new { message = "Booking successfully moved to the new slot." });
    }

    [HttpGet("mine")]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> MyBookings()
    {
        var bookings = await _service.GetUserBookingsAsync(_currentUser.UserId!);
        return Ok(bookings);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var result = await _service.CancelAsync(_currentUser.UserId!, id);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });
        return Ok(new { message = "Booking cancelled." });
    }

    [HttpGet("{id:guid}/queue-position")]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> QueuePosition(Guid id)
    {
        var result = await _service.GetQueuePositionAsync(_currentUser.UserId!, id);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    // ================= Barber =================

    [HttpGet("manage")]
    [Authorize(Roles = AppRoles.Admin)]
    [RequireApprovedBarber]
    public async Task<IActionResult> Manage()
    {
        var bookings = await _service.GetBarberBookingsAsync(_currentUser.UserId!);
        return Ok(bookings);
    }

    [HttpPut("status")]
    [Authorize(Roles = AppRoles.Admin)]
    [RequireApprovedBarber]
    public async Task<IActionResult> UpdateStatus(UpdateBookingStatusDto dto)
    {
        var result = await _service.UpdateStatusAsync(_currentUser.UserId!, dto);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });
        return Ok(new { message = "Booking updated." });
    }
}
