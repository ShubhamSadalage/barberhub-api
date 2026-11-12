using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberHub.Api.Shared.Filters;

/// <summary>
/// Apply to barber-only endpoints. If the logged-in user is a barber (Admin role)
/// but their ApprovalStatus is not Approved, returns 403 with a machine-readable
/// status the app can use to show a "pending approval" / "rejected" screen instead
/// of the normal barber dashboard. SuperAdmins always pass through.
/// </summary>
public class RequireApprovedBarberAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.HttpContext.User);

        if (user is null) { await next(); return; }

        if (await userManager.IsInRoleAsync(user, AppRoles.SuperAdmin)) { await next(); return; }

        if (!await userManager.IsInRoleAsync(user, AppRoles.Admin)) { await next(); return; }

        if (user.ApprovalStatus == BarberApprovalStatus.Approved) { await next(); return; }

        context.Result = new ObjectResult(new
        {
            error = "Your barber account is not yet approved.",
            approvalStatus = user.ApprovalStatus.ToString(),
            rejectionReason = user.ApprovalRejectionReason
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}
