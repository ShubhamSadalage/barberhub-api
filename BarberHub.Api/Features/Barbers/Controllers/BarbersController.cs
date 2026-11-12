using BarberHub.Api.Features.Barbers.Dtos;
using BarberHub.Api.Features.Barbers.Services;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberHub.Api.Features.Barbers.Controllers;

[ApiController]
[Route("api/barbers")]
public class BarbersController : ControllerBase
{
    private readonly IBarberService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<BarbersController> _logger;

    public BarbersController(IBarberService service, ICurrentUserService currentUser, ILogger<BarbersController> logger)
    {
        _service = service;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Home-tab listing with smart location fallback (live location → saved location →
    /// city → featured). Works for both logged-in and anonymous users; pass lat/lng from
    /// the device's current GPS fix when available.
    /// </summary>
    [HttpGet("home")]
    [AllowAnonymous]
    public async Task<IActionResult> Home(double? lat = null, double? lng = null, int limit = 12)
    {
        var data = await _service.GetForHomeAsync(_currentUser.UserId, lat, lng, limit);
        return Ok(data);
    }

    /// <summary>Explicit search/filter (Barbers tab) — by city, or by lat/lng + radius.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Search([FromQuery] BarberSearchDto search)
    {
        var barbers = await _service.SearchAsync(search);
        return Ok(barbers);
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Details(string id)
    {
        var barber = await _service.GetDetailsAsync(id);
        if (barber is null) return NotFound();
        return Ok(barber);
    }

    [HttpGet("{id}/slots")]
    [AllowAnonymous]
    public async Task<IActionResult> AvailableSlots(string id, [FromQuery] DateOnly date, [FromQuery] Guid serviceId)
    {
        var slots = await _service.GetAvailableSlotsAsync(id, date, serviceId);
        return Ok(slots);
    }

    [HttpGet("{id}/next-available-slot")]
    [AllowAnonymous]
    public async Task<IActionResult> NextAvailableSlot(string id, [FromQuery] DateOnly date, [FromQuery] Guid serviceId, [FromQuery] TimeOnly afterTime)
    {
        var slot = await _service.GetNextAvailableSlotAsync(id, date, serviceId, afterTime);
        return Ok(new { nextAvailableSlot = slot });
    }
}
