using System.ComponentModel.DataAnnotations;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BarberHub.Api.Features.Location;

public class UpdateLocationDto
{
    [Required, Range(-90, 90)]
    public double Latitude { get; set; }

    [Required, Range(-180, 180)]
    public double Longitude { get; set; }

    [StringLength(200)]
    public string? Label { get; set; }   // optional reverse-geocoded text from the device
}

[ApiController]
[Route("api/location")]
[Authorize]
public class LocationController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;

    public LocationController(UserManager<ApplicationUser> userManager, ICurrentUserService currentUser)
    {
        _userManager = userManager;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Called by the RN app right after the user grants location permission (and periodically
    /// thereafter). Stores coordinates on the profile so the home-feed fallback ladder
    /// (Module 2) can use the last-known location even without a fresh GPS fix.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Update(UpdateLocationDto dto)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return Unauthorized();

        user.Latitude = dto.Latitude;
        user.Longitude = dto.Longitude;
        user.LocationLabel = dto.Label;
        user.LocationUpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return Ok(new { ok = true });
    }

    /// <summary>Returns the most recent stored location for this user, if any.</summary>
    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        if (user is null) return Unauthorized();

        return Ok(new
        {
            lat = user.Latitude,
            lng = user.Longitude,
            label = user.LocationLabel,
            updatedAt = user.LocationUpdatedAt
        });
    }
}
