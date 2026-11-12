using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Features.Products.Dtos;
using BarberHub.Api.Features.Products.Services;
using BarberHub.Api.Shared.Filters;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberHub.Api.Features.Products.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IWebHostEnvironment _env;

    public ProductsController(IProductService service, ICurrentUserService currentUser, IWebHostEnvironment env)
    {
        _service = service;
        _currentUser = currentUser;
        _env = env;
    }

    // ================= Public (customer-facing marketplace) =================

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> All()
    {
        var products = await _service.GetAllActiveAsync();
        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Details(Guid id)
    {
        var product = await _service.GetByIdAsync(id);
        if (product is null) return NotFound();
        return Ok(product);
    }

    // ================= Barber management =================

    [HttpGet("mine")]
    [Authorize(Roles = AppRoles.Admin)]
    [RequireApprovedBarber]
    public async Task<IActionResult> Mine()
    {
        var products = await _service.GetByBarberAsync(_currentUser.UserId!);
        return Ok(products);
    }

    [HttpGet("mine/{id:guid}")]
    [Authorize(Roles = AppRoles.Admin)]
    [RequireApprovedBarber]
    public async Task<IActionResult> ForEdit(Guid id)
    {
        var dto = await _service.GetForEditAsync(_currentUser.UserId!, id);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    [RequireApprovedBarber]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] CreateProductDto dto)
    {
        var imagePath = await SaveImageAsync(dto.Image);
        var result = await _service.CreateAsync(_currentUser.UserId!, dto, imagePath);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        return Ok(new { productId = result.Value, message = "Product created." });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.Admin)]
    [RequireApprovedBarber]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Edit(Guid id, [FromForm] EditProductDto dto)
    {
        dto.Id = id;
        var imagePath = await SaveImageAsync(dto.Image);
        var result = await _service.UpdateAsync(_currentUser.UserId!, dto, imagePath);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        return Ok(new { message = "Product updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.Admin)]
    [RequireApprovedBarber]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _service.DeleteAsync(_currentUser.UserId!, id);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });
        return Ok(new { message = "Product deleted." });
    }

    private async Task<string?> SaveImageAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;

        var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExt.Contains(ext)) return null;

        var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "products");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
        return $"/uploads/products/{fileName}";
    }
}
