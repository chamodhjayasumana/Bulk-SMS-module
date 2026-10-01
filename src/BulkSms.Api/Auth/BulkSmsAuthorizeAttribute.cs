using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BulkSms.Api.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class BulkSmsAuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string AccessType = "BulkSMS";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var result = await context.HttpContext.AuthenticateAsync();
        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
        {
            context.Result = new JsonResult(new { message = "Unauthorized" })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        context.HttpContext.User = result.Principal;
        var access = result.Principal.FindFirst("accessType")?.Value;
        if (!string.Equals(access, AccessType, StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new JsonResult(new { message = "No Access Permission to the Logged User" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
