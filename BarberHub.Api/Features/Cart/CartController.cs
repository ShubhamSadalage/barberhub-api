using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BarberHub.Api.Features.Cart;

public class AddCartItemRequestDto
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class UpdateCartItemRequestDto
{
    public int Quantity { get; set; }
}

[ApiController]
[Route("api/cart")]
[Authorize(Roles = AppRoles.User)]
public class CartController : ControllerBase
{
    private readonly ICartService _service;
    private readonly ICurrentUserService _currentUser;

    public CartController(ICartService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var cart = await _service.GetAsync(_currentUser.UserId!);
        return Ok(cart);
    }

    [HttpPost("items")]
    public async Task<IActionResult> Add(AddCartItemRequestDto dto)
    {
        var result = await _service.AddAsync(_currentUser.UserId!, dto.ProductId, dto.Quantity);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        var cart = await _service.GetAsync(_currentUser.UserId!);
        return Ok(cart);
    }

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> UpdateQuantity(Guid id, UpdateCartItemRequestDto dto)
    {
        var result = await _service.UpdateQuantityAsync(_currentUser.UserId!, id, dto.Quantity);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        var cart = await _service.GetAsync(_currentUser.UserId!);
        return Ok(cart);
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var result = await _service.RemoveAsync(_currentUser.UserId!, id);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        var cart = await _service.GetAsync(_currentUser.UserId!);
        return Ok(cart);
    }
}
