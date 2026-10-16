using Microsoft.AspNetCore.Mvc;
using Tailoring.Api.Security;
using Tailoring.Application.Pdf;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

/// <summary>How the business's PDFs look (accent colour, logo, contact line, footer note). The logo is an image already uploaded through /api/media.</summary>
[ApiController, Route("api/business/branding"), HasPermission(Permissions.BusinessSettings)]
public class BrandingController(IBrandingService branding) : ControllerBase
{
    [HttpGet]
    public async Task<BrandingDto> Get(CancellationToken ct) => await branding.GetAsync(ct);

    [HttpPut]
    public async Task<BrandingDto> Update(UpdateBrandingRequest request, CancellationToken ct) => await branding.UpdateAsync(request, ct);
}
