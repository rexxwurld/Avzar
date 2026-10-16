using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tailoring.Application.Email;

namespace Tailoring.Api.Controllers;

/// <summary>
/// Delivery reports from the email provider. There is no user, cookie or CSRF token here: the provider's signature (checked against
/// Email:Resend:WebhookSecret) is the only authentication. Without a configured secret the endpoint does not exist (404).
/// </summary>
[ApiController, Route("api/webhooks/email"), AllowAnonymous, IgnoreAntiforgeryToken]
public class EmailWebhooksController(IEmailWebhookService webhooks) : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024;

    [HttpPost("resend"), RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Resend(CancellationToken ct)
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, false, 4096, leaveOpen: true))
            body = await reader.ReadToEndAsync(ct);
        if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes) return StatusCode(StatusCodes.Status413PayloadTooLarge);

        var outcome = await webhooks.HandleResendAsync(body, Request.Headers["svix-id"].ToString(), Request.Headers["svix-timestamp"].ToString(), Request.Headers["svix-signature"].ToString(), ct);
        return outcome switch
        {
            WebhookOutcome.Disabled => NotFound(),
            WebhookOutcome.InvalidSignature => Unauthorized(),
            _ => Ok(), // applied, duplicate, unknown message or ignored event: all acknowledged so the provider stops retrying
        };
    }
}
