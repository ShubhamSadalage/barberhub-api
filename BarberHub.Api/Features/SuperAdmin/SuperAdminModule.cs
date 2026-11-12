using System.ComponentModel.DataAnnotations;
using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Features.Orders;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Features.SuperAdmin;

// =================== DTOs ===================

public class PendingBarberDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ShopName { get; set; }
    public string? ShopDescription { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public DateTime? RequestedAt { get; set; }
    public BarberApprovalStatus Status { get; set; }
    public string? RejectionReason { get; set; }
}

public class RejectBarberDto
{
    [Required] public string BarberId { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
}

public class MarketplaceProductDto
{
    public Guid Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(2000)] public string? Description { get; set; }
    [Range(0.01, 999999)] public decimal Price { get; set; }
    [Range(0, 100)] public decimal? DiscountPercentage { get; set; }
    [Range(0, 100000)] public int StockQuantity { get; set; }
    [StringLength(100)] public string Category { get; set; } = "General";
    public string? ExistingImageUrl { get; set; }
    public IFormFile? Image { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DashboardSummaryDto
{
    public int PendingBarbers { get; set; }
    public int ApprovedBarbers { get; set; }
    public int TotalBarbers { get; set; }
    public int TotalUsers { get; set; }
    public int MarketplaceProducts { get; set; }
    public int BarberProducts { get; set; }
    public int TotalBookings { get; set; }
    public int TotalOrders { get; set; }
}

// =================== Controller ===================

[ApiController]
[Route("api/superadmin")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class SuperAdminController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;
    private readonly IWebHostEnvironment _env;
    private readonly IOrderService _orders;
    private readonly IAuditService _audit;

    public SuperAdminController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUser,
        INotificationService notifications,
        IWebHostEnvironment env,
        IOrderService orders,
        IAuditService audit)
    {
        _db = db; _userManager = userManager; _currentUser = currentUser;
        _notifications = notifications; _env = env; _orders = orders; _audit = audit;
    }

    // ----------- Dashboard -----------
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var allBarbers = await _userManager.GetUsersInRoleAsync(AppRoles.Admin);
        var allUsers = await _userManager.GetUsersInRoleAsync(AppRoles.User);

        var dto = new DashboardSummaryDto
        {
            PendingBarbers = allBarbers.Count(b => b.ApprovalStatus == BarberApprovalStatus.Pending),
            ApprovedBarbers = allBarbers.Count(b => b.ApprovalStatus == BarberApprovalStatus.Approved),
            TotalBarbers = allBarbers.Count,
            TotalUsers = allUsers.Count,
            MarketplaceProducts = await _db.Products.CountAsync(p => p.BarberId == null),
            BarberProducts = await _db.Products.CountAsync(p => p.BarberId != null),
            TotalBookings = await _db.Bookings.CountAsync(),
            TotalOrders = await _db.Orders.CountAsync()
        };

        return Ok(dto);
    }

    // ----------- Approvals -----------
    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals()
    {
        var barbers = await _userManager.GetUsersInRoleAsync(AppRoles.Admin);
        var dtos = barbers
            .Where(b => b.ApprovalStatus == BarberApprovalStatus.Pending || b.ApprovalStatus == BarberApprovalStatus.Rejected)
            .OrderBy(b => b.ApprovalStatus)
            .ThenByDescending(b => b.ApprovalRequestedAt)
            .Select(b => new PendingBarberDto
            {
                Id = b.Id, FullName = b.FullName, Email = b.Email ?? "", PhoneNumber = b.PhoneNumber,
                ShopName = b.ShopName, ShopDescription = b.ShopDescription, Address = b.Address, City = b.City,
                RequestedAt = b.ApprovalRequestedAt, Status = b.ApprovalStatus, RejectionReason = b.ApprovalRejectionReason
            }).ToList();

        return Ok(dtos);
    }

    [HttpPost("approvals/{barberId}/approve")]
    public async Task<IActionResult> Approve(string barberId)
    {
        var barber = await _userManager.FindByIdAsync(barberId);
        if (barber is null) return NotFound(new { error = "Barber not found." });

        barber.ApprovalStatus = BarberApprovalStatus.Approved;
        barber.ApprovedAt = DateTime.UtcNow;
        barber.ApprovedByUserId = _currentUser.UserId;
        barber.ApprovalRejectionReason = null;
        await _userManager.UpdateAsync(barber);

        await _notifications.NotifyAsync(
            barber.Id, NotificationType.BarberApproved, "Your barber account is approved!",
            "Welcome to Barber Hub. You can now manage your shop, products, and bookings.", "Admin/Dashboard");

        await _audit.LogAsync(User, "BarberApprove", "ApplicationUser", barber.Id,
            $"Email={barber.Email}; Shop={barber.ShopName}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { message = $"Barber '{barber.FullName}' approved." });
    }

    [HttpPost("approvals/reject")]
    public async Task<IActionResult> Reject(RejectBarberDto dto)
    {
        var barber = await _userManager.FindByIdAsync(dto.BarberId);
        if (barber is null) return NotFound(new { error = "Barber not found." });

        barber.ApprovalStatus = BarberApprovalStatus.Rejected;
        barber.ApprovalRejectionReason = dto.Reason;
        await _userManager.UpdateAsync(barber);

        await _notifications.NotifyAsync(
            barber.Id, NotificationType.BarberRejected, "Your barber registration was not approved",
            $"Reason: {dto.Reason}", "Auth/PendingApproval");

        await _audit.LogAsync(User, "BarberReject", "ApplicationUser", barber.Id,
            $"Email={barber.Email}; Reason={dto.Reason}", HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new { message = $"Barber '{barber.FullName}' rejected." });
    }

    // ----------- Marketplace Products CRUD -----------
    [HttpGet("products")]
    public async Task<IActionResult> Products()
    {
        var products = await _db.Products.Where(p => p.BarberId == null).OrderByDescending(p => p.CreatedAt).ToListAsync();
        return Ok(products);
    }

    [HttpPost("products")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> CreateProduct([FromForm] MarketplaceProductDto dto)
    {
        var imageUrl = await SaveImageAsync(dto.Image);

        var p = new Product
        {
            Name = dto.Name, Description = dto.Description, Price = dto.Price,
            DiscountPercentage = dto.DiscountPercentage, StockQuantity = dto.StockQuantity,
            Category = dto.Category, IsActive = dto.IsActive, BarberId = null,
            ImageUrl = imageUrl, CreatedAt = DateTime.UtcNow
        };
        await _db.Products.AddAsync(p);
        await _db.SaveChangesAsync();

        return Ok(new { productId = p.Id, message = "Marketplace product created." });
    }

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> EditProductForm(Guid id)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && x.BarberId == null);
        if (p is null) return NotFound();

        return Ok(new MarketplaceProductDto
        {
            Id = p.Id, Name = p.Name, Description = p.Description, Price = p.Price,
            DiscountPercentage = p.DiscountPercentage, StockQuantity = p.StockQuantity,
            Category = p.Category, ExistingImageUrl = p.ImageUrl, IsActive = p.IsActive
        });
    }

    [HttpPut("products/{id:guid}")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> EditProduct(Guid id, [FromForm] MarketplaceProductDto dto)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && x.BarberId == null);
        if (p is null) return NotFound();

        p.Name = dto.Name;
        p.Description = dto.Description;
        p.Price = dto.Price;
        p.DiscountPercentage = dto.DiscountPercentage;
        p.StockQuantity = dto.StockQuantity;
        p.Category = dto.Category;
        p.IsActive = dto.IsActive;
        p.UpdatedAt = DateTime.UtcNow;

        if (dto.Image is { Length: > 0 })
            p.ImageUrl = await SaveImageAsync(dto.Image);

        await _db.SaveChangesAsync();
        return Ok(new { message = "Marketplace product updated." });
    }

    [HttpDelete("products/{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && x.BarberId == null);
        if (p is null) return NotFound();

        _db.Products.Remove(p);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Marketplace product removed." });
    }

    // ----------- Marketplace Orders -----------
    [HttpGet("orders")]
    public async Task<IActionResult> Orders()
    {
        var orders = await _orders.GetMarketplaceOrdersAsync();
        return Ok(orders);
    }

    // ----------- Manage customers -----------
    [HttpGet("users")]
    public async Task<IActionResult> ManageUsers()
    {
        var users = await _userManager.GetUsersInRoleAsync(AppRoles.User);
        var list = users.OrderByDescending(u => u.CreatedAt)
            .Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber, u.City, u.IsActive, u.IsDeleted, u.CreatedAt })
            .ToList();
        return Ok(list);
    }

    [HttpPost("users/{id}/delete")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var u = await _userManager.FindByIdAsync(id);
        if (u is null) return NotFound(new { error = "User not found." });
        if (u.Id == _currentUser.UserId) return BadRequest(new { error = "You cannot delete yourself." });

        u.IsDeleted = true; u.IsActive = false; u.DeletedAt = DateTime.UtcNow; u.DeletedByUserId = _currentUser.UserId;
        await _userManager.UpdateAsync(u);

        await _audit.LogAsync(User, "UserSoftDelete", "ApplicationUser", u.Id, $"Email={u.Email}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { message = $"User '{u.FullName}' deleted." });
    }

    [HttpPost("users/{id}/restore")]
    public async Task<IActionResult> RestoreUser(string id)
    {
        var u = await _userManager.FindByIdAsync(id);
        if (u is null) return NotFound();

        u.IsDeleted = false; u.IsActive = true; u.DeletedAt = null; u.DeletedByUserId = null;
        await _userManager.UpdateAsync(u);
        await _audit.LogAsync(User, "UserRestore", "ApplicationUser", u.Id, $"Email={u.Email}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { message = $"User '{u.FullName}' restored." });
    }

    // ----------- Manage barbers -----------
    [HttpGet("barbers")]
    public async Task<IActionResult> ManageBarbers()
    {
        var barbers = await _userManager.GetUsersInRoleAsync(AppRoles.Admin);
        var list = barbers.OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id, b.FullName, b.Email, b.PhoneNumber, b.City, b.ShopName, b.IsActive,
                b.IsDeleted, b.ApprovalStatus, b.CreatedAt
            }).ToList();
        return Ok(list);
    }

    [HttpPost("barbers/{id}/delete")]
    public async Task<IActionResult> DeleteBarber(string id)
    {
        var b = await _userManager.FindByIdAsync(id);
        if (b is null) return NotFound(new { error = "Barber not found." });

        b.IsDeleted = true; b.IsActive = false; b.DeletedAt = DateTime.UtcNow; b.DeletedByUserId = _currentUser.UserId;
        await _userManager.UpdateAsync(b);

        await _audit.LogAsync(User, "BarberSoftDelete", "ApplicationUser", b.Id, $"Email={b.Email}; Shop={b.ShopName}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { message = $"Barber '{b.ShopName ?? b.FullName}' deleted." });
    }

    [HttpPost("barbers/{id}/restore")]
    public async Task<IActionResult> RestoreBarber(string id)
    {
        var b = await _userManager.FindByIdAsync(id);
        if (b is null) return NotFound();

        b.IsDeleted = false; b.IsActive = true; b.DeletedAt = null; b.DeletedByUserId = null;
        await _userManager.UpdateAsync(b);
        await _audit.LogAsync(User, "BarberRestore", "ApplicationUser", b.Id, $"Email={b.Email}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { message = $"Barber '{b.ShopName ?? b.FullName}' restored." });
    }

    // ----------- Audit log -----------
    [HttpGet("audit-log")]
    public async Task<IActionResult> AuditLog(int page = 1)
    {
        const int pageSize = 50;
        var total = await _db.AuditLogs.CountAsync();
        var entries = await _db.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { page, pageSize, total, hasMore = page * pageSize < total, entries });
    }

    // ----------- Helpers -----------
    private async Task<string?> SaveImageAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;
        var ext = Path.GetExtension(file.FileName);
        var fileName = $"marketplace-{Guid.NewGuid()}{ext}";
        var dir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "products");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await using var stream = System.IO.File.Create(path);
        await file.CopyToAsync(stream);
        return $"/uploads/products/{fileName}";
    }
}
