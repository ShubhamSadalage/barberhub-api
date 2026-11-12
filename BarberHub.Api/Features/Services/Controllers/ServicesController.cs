using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Services.Dtos;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Filters;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Features.Services.Controllers;

[ApiController]
[Route("api/services")]
[Authorize(Roles = AppRoles.Admin)]
[RequireApprovedBarber]
public class ServicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ServicesController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    // ----------- My services -----------
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var barberId = _currentUser.UserId!;
        var services = await _db.Services
            .Where(s => s.BarberId == barberId)
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .Select(s => new ServiceDto
            {
                Id = s.Id, Name = s.Name, Description = s.Description, Category = s.Category,
                Price = s.Price, DurationMinutes = s.DurationMinutes, IsActive = s.IsActive,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();
        return Ok(services);
    }

    // ----------- Templates (quick-add for new barbers) -----------
    [HttpGet("templates")]
    public async Task<IActionResult> Templates()
    {
        var barberId = _currentUser.UserId!;
        var existing = await _db.Services
            .Where(s => s.BarberId == barberId)
            .Select(s => s.Name.ToLower())
            .ToListAsync();

        var items = ServiceTemplates.All.Select(t => new ServiceTemplateDto
        {
            Name = t.Name,
            Category = t.Category,
            Description = t.Description,
            DefaultPrice = t.Price,
            DefaultDuration = t.Duration,
            SuggestedSelected = !existing.Contains(t.Name.ToLower())
        }).ToList();

        return Ok(items);
    }

    [HttpPost("templates/activate")]
    public async Task<IActionResult> ActivateTemplates(TemplateActivateRequestDto dto)
    {
        var barberId = _currentUser.UserId!;
        if (dto.Items.Count == 0)
            return BadRequest(new { error = "Please select at least one service." });

        var existing = await _db.Services
            .Where(s => s.BarberId == barberId)
            .Select(s => s.Name.ToLower())
            .ToListAsync();

        int added = 0;
        foreach (var item in dto.Items)
        {
            if (existing.Contains(item.Name.ToLower())) continue;
            await _db.Services.AddAsync(new Service
            {
                Name = item.Name,
                Category = item.Category,
                Description = item.Description,
                Price = item.Price,
                DurationMinutes = item.DurationMinutes,
                BarberId = barberId,
                IsActive = true
            });
            added++;
        }
        await _db.SaveChangesAsync();

        return Ok(new { message = $"{added} service{(added == 1 ? "" : "s")} added to your menu.", added });
    }

    // ----------- Create -----------
    [HttpPost]
    public async Task<IActionResult> Create(ServiceUpsertRequestDto dto)
    {
        var barberId = _currentUser.UserId!;
        var dupe = await _db.Services
            .AnyAsync(s => s.BarberId == barberId && s.Name.ToLower() == dto.Name.ToLower());
        if (dupe)
            return BadRequest(new { error = "You already have a service with this name." });

        var service = new Service
        {
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            Price = dto.Price,
            DurationMinutes = dto.DurationMinutes,
            IsActive = dto.IsActive,
            BarberId = barberId
        };
        await _db.Services.AddAsync(service);
        await _db.SaveChangesAsync();

        return Ok(new ServiceDto
        {
            Id = service.Id, Name = service.Name, Description = service.Description, Category = service.Category,
            Price = service.Price, DurationMinutes = service.DurationMinutes, IsActive = service.IsActive,
            CreatedAt = service.CreatedAt
        });
    }

    // ----------- Edit -----------
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, ServiceUpsertRequestDto dto)
    {
        var s = await _db.Services.FirstOrDefaultAsync(x => x.Id == id && x.BarberId == _currentUser.UserId);
        if (s is null) return NotFound();

        var dupe = await _db.Services.AnyAsync(x =>
            x.BarberId == _currentUser.UserId && x.Id != id && x.Name.ToLower() == dto.Name.ToLower());
        if (dupe)
            return BadRequest(new { error = "You already have a service with this name." });

        s.Name = dto.Name;
        s.Description = dto.Description;
        s.Category = dto.Category;
        s.Price = dto.Price;
        s.DurationMinutes = dto.DurationMinutes;
        s.IsActive = dto.IsActive;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Service updated." });
    }

    // ----------- Toggle active/inactive -----------
    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var s = await _db.Services.FirstOrDefaultAsync(x => x.Id == id && x.BarberId == _currentUser.UserId);
        if (s is null) return NotFound();

        s.IsActive = !s.IsActive;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            isActive = s.IsActive,
            message = s.IsActive ? $"'{s.Name}' is now visible to customers." : $"'{s.Name}' is now hidden."
        });
    }

    // ----------- Delete -----------
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var s = await _db.Services.FirstOrDefaultAsync(x => x.Id == id && x.BarberId == _currentUser.UserId);
        if (s is null) return NotFound();

        var hasBookings = await _db.Bookings.AnyAsync(b => b.ServiceId == id);
        if (hasBookings)
        {
            s.IsActive = false;
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { message = $"'{s.Name}' has bookings and was hidden instead of deleted.", hidden = true });
        }

        _db.Services.Remove(s);
        await _db.SaveChangesAsync();
        return Ok(new { message = $"'{s.Name}' deleted.", hidden = false });
    }
}
