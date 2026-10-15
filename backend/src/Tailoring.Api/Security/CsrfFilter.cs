using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Tailoring.Api.Security;

/// <summary>
/// Replaces AutoValidateAntiforgeryToken. Same rule (unsafe methods need the CSRF token, [IgnoreAntiforgeryToken] opts out) with one
/// addition: a request that carries an Authorization: Bearer header is exempt. Browsers never attach that header on their own, so
/// it cannot be forged cross-site, and the mobile app has no cookie to hold a CSRF token. A bad bearer token never falls back to
/// the cookie (see the "Smart" scheme in Program.cs), so the exemption cannot be used to bypass cookie authentication.
/// </summary>
public sealed class CsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method)) return;
        if (context.Filters.OfType<IgnoreAntiforgeryTokenAttribute>().Any()) return;
        if (request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return;

        try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing or invalid CSRF token. Fetch /api/auth/csrf and send it in the X-XSRF-TOKEN header.",
                Extensions = { ["code"] = "invalid_csrf" },
            }) { StatusCode = StatusCodes.Status400BadRequest };
        }
    }
}
