using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Pdf;

/// <summary>How a business's documents look. Every field is optional; an unset business gets the neutral default.</summary>
public sealed record BrandingDto(string? AccentColor, Guid? LogoMediaId, string? Phone, string? Email, string? TaxNumber, string? FooterNote)
{
    public const string DefaultAccent = "#1F3A5F";
}

/// <summary>Replaces the whole branding. A null or blank field clears it.</summary>
public sealed record UpdateBrandingRequest
{
    [StringLength(7)] public string? AccentColor { get; init; }
    public Guid? LogoMediaId { get; init; }
    [StringLength(40)] public string? Phone { get; init; }
    [StringLength(254)] public string? Email { get; init; }
    [StringLength(60)] public string? TaxNumber { get; init; }
    [StringLength(300)] public string? FooterNote { get; init; }
}

/// <summary>What the renderer draws, already resolved: a validated colour and a logo that is already a small PNG.</summary>
public sealed record PdfStyle(string AccentColor, byte[]? Logo, string? Phone, string? Email, string? TaxNumber, string? FooterNote);

public interface IBrandingService
{
    Task<BrandingDto> GetAsync(CancellationToken ct = default);
    Task<BrandingDto> UpdateAsync(UpdateBrandingRequest request, CancellationToken ct = default);
}
