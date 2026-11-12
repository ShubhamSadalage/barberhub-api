using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Features.Payments;
using BarberHub.Api.Shared.Config;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;

namespace BarberHub.Api.Features.Orders;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _service;
    private readonly IStripeService _stripe;
    private readonly ICurrentUserService _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notifications;
    private readonly FeatureFlags _features;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderService service,
        IStripeService stripe,
        ICurrentUserService currentUser,
        UserManager<ApplicationUser> userManager,
        INotificationService notifications,
        IOptions<FeatureFlags> features,
        ILogger<OrdersController> logger)
    {
        _service = service;
        _stripe = stripe;
        _currentUser = currentUser;
        _userManager = userManager;
        _notifications = notifications;
        _features = features.Value;
        _logger = logger;
    }

    [HttpPost("checkout")]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> Checkout(CheckoutDto dto)
    {
        if (!_features.ProductsEnabled)
            return BadRequest(new { error = "Product shopping is currently disabled." });

        if (dto.PaymentMethod == Domain.Entities.PaymentMethod.Online && !_features.OnlinePaymentEnabled)
            return BadRequest(new { error = "Online payments are currently disabled. Please choose Cash on Delivery." });

        var result = await _service.CheckoutAsync(_currentUser.UserId!, _currentUser.Email!, dto);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        await _service.NotifyOrderOwnersAsync(result.Value.orderId);

        return Ok(new
        {
            orderId = result.Value.orderId,
            clientSecret = result.Value.clientSecret,
            isCod = result.Value.isCod,
            message = result.Value.isCod ? "Order placed! Pay cash on delivery." : "Order created — complete payment to confirm."
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> Details(Guid id)
    {
        var order = await _service.GetByIdAsync(id);
        if (order is null) return NotFound();
        return Ok(order);
    }

    [HttpGet("mine")]
    [Authorize(Roles = AppRoles.User)]
    public async Task<IActionResult> MyOrders()
    {
        var orders = await _service.GetUserOrdersAsync(_currentUser.UserId!);
        return Ok(orders);
    }

    [HttpGet("manage")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Manage()
    {
        var orders = await _service.GetBarberOrdersAsync(_currentUser.UserId!);
        return Ok(orders);
    }

    /// <summary>Seller (barber or SuperAdmin) marks a Cash-on-Delivery order as paid after receiving cash.</summary>
    [HttpPost("{id:guid}/mark-paid")]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.SuperAdmin)]
    public async Task<IActionResult> MarkAsPaid(Guid id)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId!);
        var isSuperAdmin = user is not null && await _userManager.IsInRoleAsync(user, AppRoles.SuperAdmin);

        var result = await _service.MarkAsPaidByOwnerAsync(id, _currentUser.UserId!, isSuperAdmin);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });

        var order = await _service.GetByIdAsync(id);
        var buyerId = await _service.GetUserIdForOrderAsync(id);
        if (order is not null && !string.IsNullOrEmpty(buyerId))
        {
            await _notifications.NotifyAsync(
                buyerId,
                NotificationType.OrderPlaced,
                "Payment received",
                $"Your COD order {order.OrderNumber} has been marked as paid by the seller.",
                "Orders/MyOrders");
        }

        return Ok(new { message = "Order marked as paid." });
    }

    /// <summary>Stripe webhook — must remain anonymous; Stripe signs the payload instead.</summary>
    [HttpPost("stripe/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> StripeWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        try
        {
            var stripeEvent = _stripe.ConstructWebhookEvent(json, Request.Headers["Stripe-Signature"]!);

            if (stripeEvent.Type == "payment_intent.succeeded")
            {
                var intent = stripeEvent.Data.Object as PaymentIntent;
                if (intent is not null)
                    await _service.MarkPaidAsync(intent.Id);
            }

            return Ok();
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe webhook failed");
            return BadRequest();
        }
    }
}
