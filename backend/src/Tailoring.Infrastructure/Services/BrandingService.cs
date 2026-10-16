using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Pdf;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed partial class BrandingService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : IBrandingService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    /// <summary>WCAG contrast of the accent against the white page. Below this the business name and table headings would be hard to read.</summary>
    private const double MinContrastOnWhite = 3.0;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex HexColor();

    public async Task<BrandingDto> GetAsync(CancellationToken ct = default)
    {
        var b = await db.Businesses.AsNoTracking().FirstAsync(x => x.Id == BusinessId, ct);
        return ToDto(b);
    }

    public async Task<BrandingDto> UpdateAsync(UpdateBrandingRequest request, CancellationToken ct = default)
    {
        var color = Color(request.AccentColor);
        var phone = Text(request.Phone, 40, "phone");
        var email = Text(request.Email, 254, "email");
        if (email is not null && !(email.Contains('@') && !email.Contains(' ') && email.IndexOf('@') > 0 && email.IndexOf('@') < email.Length - 1))
            throw AppException.BadRequest("invalid_email", "Enter a valid email address.");
        var tax = Text(request.TaxNumber, 60, "tax number");
        var footer = Text(request.FooterNote, 300, "footer note");

        if (request.LogoMediaId is { } logo && !await db.MediaAssets.AnyAsync(m => m.Id == logo, ct)) // tenant filter: another business's image is "not found"
            throw AppException.NotFound("media_not_found", "That image was not found. Upload it first, then choose it as the logo.");

        var b = await db.Businesses.FirstAsync(x => x.Id == BusinessId, ct);
        var changed = new List<string>();
        void Set<T>(string name, T before, T after, Action apply) { if (!EqualityComparer<T>.Default.Equals(before, after)) { apply(); changed.Add(name); } }
        Set("accentColor", b.BrandColor, color, () => b.BrandColor = color);
        Set("logo", b.LogoMediaId, request.LogoMediaId, () => b.LogoMediaId = request.LogoMediaId);
        Set("phone", b.ContactPhone, phone, () => b.ContactPhone = phone);
        Set("email", b.ContactEmail, email, () => b.ContactEmail = email);
        Set("taxNumber", b.TaxNumber, tax, () => b.TaxNumber = tax);
        Set("footerNote", b.PdfFooterNote, footer, () => b.PdfFooterNote = footer);

        if (changed.Count > 0)
        {
            b.UpdatedAtUtc = DateTime.UtcNow;
            audit.Record(BusinessId, tenant.UserId, "business.branding_updated", nameof(Business), b.Id, new { changed }); // field names only
            await db.SaveChangesAsync(ct);
        }
        return ToDto(b);
    }

    private static BrandingDto ToDto(Business b) => new(b.BrandColor, b.LogoMediaId, b.ContactPhone, b.ContactEmail, b.TaxNumber, b.PdfFooterNote);

    /// <summary>One line of plain text: control characters are dropped and runs of whitespace collapse, so nothing odd reaches a PDF.</summary>
    private static string? Text(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sb = new StringBuilder();
        foreach (var ch in value.Trim()) sb.Append(char.IsControl(ch) ? ' ' : ch);
        var clean = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        if (clean.Length > max) throw AppException.BadRequest("branding_too_long", $"The {label} can be at most {max} characters.");
        return clean.Length == 0 ? null : clean;
    }

    private static string? Color(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (!HexColor().IsMatch(v)) throw AppException.BadRequest("invalid_brand_color", "Use a colour like #1F3A5F (a # and six hex digits).");
        v = v.ToUpperInvariant();
        if (ContrastOnWhite(v) < MinContrastOnWhite)
            throw AppException.BadRequest("brand_color_too_light", "That colour is too light to read on white paper. Choose a darker one.");
        return v;
    }

    public static double ContrastOnWhite(string hex)
    {
        double Channel(int start)
        {
            var c = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        return 1.05 / (luminance + 0.05);
    }
}
