using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PrivateGym.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SessionAuthorizeAttribute : ActionFilterAttribute
{
    private readonly string[] _allowedRoles;

    public SessionAuthorizeAttribute(params string[] allowedRoles)
    {
        _allowedRoles = allowedRoles;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var session = context.HttpContext.Session;
        var userId = session.GetInt32("UserId");

        if (!userId.HasValue)
        {
            context.Result = new RedirectToActionResult("Index", "Login", null);
            return;
        }

        if (_allowedRoles.Length > 0)
        {
            var roleName = session.GetString("RoleName") ?? "";
            if (!_allowedRoles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)))
            {
                context.Result = new RedirectToActionResult("Index", "Login", null);
                return;
            }
        }

        base.OnActionExecuting(context);
    }
}
