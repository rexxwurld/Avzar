using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Tailoring.Application.Common;

namespace Tailoring.Api.Infrastructure;

public sealed class AppExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not AppException app) return false; // anything else -> default 500, details logged not leaked

        ctx.Response.StatusCode = app.StatusCode;
        var details = new ProblemDetails { Status = app.StatusCode, Title = app.Message };
        details.Extensions["code"] = app.Code;
        if (app.Extensions is not null)
            foreach (var (key, value) in app.Extensions) details.Extensions[key] = value;

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, Exception = ex, ProblemDetails = details });
    }
}
