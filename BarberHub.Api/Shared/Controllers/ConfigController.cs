using BarberHub.Api.Shared.Config;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BarberHub.Api.Shared.Controllers;

[ApiController]
[Route("api/config")]
[AllowAnonymous]
public class ConfigController : ControllerBase
{
    private readonly FeatureFlags _features;

    public ConfigController(IOptions<FeatureFlags> features)
    {
        _features = features.Value;
    }

    /// <summary>
    /// Returns active client feature flags configured in appsettings.json.
    /// Mobile/Web apps use this to dynamically show/hide tabs, checkout options, and reports.
    /// </summary>
    [HttpGet("features")]
    public IActionResult GetFeatures()
    {
        return Ok(new
        {
            onlinePaymentEnabled = _features.OnlinePaymentEnabled,
            productsEnabled = _features.ProductsEnabled,
            earningsFinancialsEnabled = _features.EarningsFinancialsEnabled,
            googleLoginEnabled = _features.GoogleLoginEnabled,
            emailOtpLoginEnabled = _features.EmailOtpLoginEnabled,
            smsOtpLoginEnabled = _features.SmsOtpLoginEnabled,
            mapsEnabled = _features.MapsEnabled,
            pushEnabled = _features.PushEnabled,
            superAdminTwoFactorEnabled = _features.SuperAdminTwoFactorEnabled
        });
    }

    /// <summary>
    /// Tests SMTP connection and sends a test email to verify credentials.
    /// </summary>
    [HttpPost("test-email")]
    public async Task<IActionResult> TestEmail([FromQuery] string toEmail, [FromServices] BarberHub.Api.Shared.Services.IEmailService emailService)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
            return BadRequest(new { error = "Please provide a valid destination email (e.g. ?toEmail=your-email@gmail.com)." });

        var (success, message) = await emailService.TestConnectionAsync(toEmail.Trim());
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, error = message });
    }
}
