using Microsoft.Extensions.Options;
using Tailoring.Application.Common;
using Tailoring.Application.Pdf;
using Tailoring.Domain.Media;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Pdf;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>A business can set how its documents look. Settings are validated, tenant-scoped and never block a document from rendering.</summary>
public sealed class PdfBrandingTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> NewBusinessAsync()
    {
        await using var db = fx.Create(null);
        var business = new Business { Name = "Brand-" + Guid.NewGuid().ToString("N")[..8], CurrencyCode = "USD", TimeZoneId = "UTC" };
        db.Businesses.Add(business);
        await db.SaveChangesAsync();
        return business.Id;
    }

    private BrandingService Service(Guid businessId)
    {
        var db = fx.Create(businessId);
        return new BrandingService(db, new FakeUserTenant(businessId, Guid.NewGuid()), new AuditLogger(db));
    }

    private async Task<Guid> NewImageAsync(Guid businessId)
    {
        await using var db = fx.Create(businessId);
        var asset = new MediaAsset
        {
            BusinessId = businessId, Purpose = MediaPurpose.SiteImage, OriginalFileName = "logo.png", ContentType = "image/webp",
            Width = 120, Height = 40, SizeBytes = 1000, Sha256 = new string('a', 64)
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    private static async Task<AppException> Fails(Func<Task> action) => await Assert.ThrowsAsync<AppException>(action);

    [Fact]
    public async Task An_unbranded_business_gets_empty_settings()
    {
        var id = await NewBusinessAsync();
        var dto = await Service(id).GetAsync();
        Assert.Equal(new BrandingDto(null, null, null, null, null, null), dto);
    }

    [Fact]
    public async Task Branding_round_trips_and_is_tidied()
    {
        var id = await NewBusinessAsync();
        var logo = await NewImageAsync(id);

        var saved = await Service(id).UpdateAsync(new UpdateBrandingRequest
        {
            AccentColor = " #1f3a5f ", LogoMediaId = logo, Phone = "  +234 801\n 234   5678 ", Email = "hello@ade.test",
            TaxNumber = "TIN 123-456", FooterNote = "Thank you for your custom."
        });

        Assert.Equal("#1F3A5F", saved.AccentColor);
        Assert.Equal("+234 801 234 5678", saved.Phone);

        var again = await Service(id).GetAsync();
        Assert.Equal(saved, again);
        Assert.Equal(logo, again.LogoMediaId);
        Assert.Equal("TIN 123-456", again.TaxNumber);
    }

    [Fact]
    public async Task Sending_blanks_clears_the_branding()
    {
        var id = await NewBusinessAsync();
        await Service(id).UpdateAsync(new UpdateBrandingRequest { AccentColor = "#112233", Phone = "123", FooterNote = "hi" });

        var cleared = await Service(id).UpdateAsync(new UpdateBrandingRequest { AccentColor = "  ", Phone = "", FooterNote = null });
        Assert.Equal(new BrandingDto(null, null, null, null, null, null), cleared);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("1F3A5F")]
    public async Task A_colour_that_is_not_hex_is_rejected(string colour)
    {
        var id = await NewBusinessAsync();
        var ex = await Fails(() => Service(id).UpdateAsync(new UpdateBrandingRequest { AccentColor = colour }));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("invalid_brand_color", ex.Code);
    }

    [Theory]
    [InlineData("#FFFF00")]
    [InlineData("#FFFFFF")]
    [InlineData("#DDDDDD")]
    public async Task A_colour_too_light_to_read_on_white_is_rejected(string colour)
    {
        var id = await NewBusinessAsync();
        var ex = await Fails(() => Service(id).UpdateAsync(new UpdateBrandingRequest { AccentColor = colour }));
        Assert.Equal("brand_color_too_light", ex.Code);
    }

    [Fact]
    public async Task Nothing_is_saved_when_any_field_is_invalid()
    {
        var id = await NewBusinessAsync();
        await Fails(() => Service(id).UpdateAsync(new UpdateBrandingRequest { Phone = "123", Email = "not an email" }));
        Assert.Null((await Service(id).GetAsync()).Phone);

        var tooLong = await Fails(() => Service(id).UpdateAsync(new UpdateBrandingRequest { FooterNote = new string('x', 301) }));
        Assert.Equal("branding_too_long", tooLong.Code);
    }

    [Fact]
    public async Task Another_businesss_image_cannot_be_used_as_the_logo()
    {
        var mine = await NewBusinessAsync();
        var theirs = await NewBusinessAsync();
        var theirImage = await NewImageAsync(theirs);

        var ex = await Fails(() => Service(mine).UpdateAsync(new UpdateBrandingRequest { LogoMediaId = theirImage }));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("media_not_found", ex.Code);

        var missing = await Fails(() => Service(mine).UpdateAsync(new UpdateBrandingRequest { LogoMediaId = Guid.NewGuid() }));
        Assert.Equal("media_not_found", missing.Code);
    }

    [Fact]
    public async Task One_businesss_branding_is_not_visible_to_another()
    {
        var a = await NewBusinessAsync();
        var b = await NewBusinessAsync();
        await Service(a).UpdateAsync(new UpdateBrandingRequest { AccentColor = "#112233", FooterNote = "A only" });

        Assert.Null((await Service(b).GetAsync()).AccentColor);
        Assert.Null((await Service(b).GetAsync()).FooterNote);
    }

    [Fact]
    public void Contrast_is_computed_against_white()
    {
        Assert.True(BrandingService.ContrastOnWhite("#000000") > 20);
        Assert.True(BrandingService.ContrastOnWhite("#1F3A5F") > 9);   // the default navy
        Assert.True(BrandingService.ContrastOnWhite("#FFFFFF") < 1.1);
    }
}

/// <summary>Drawing needs QuestPDF and its native libraries.</summary>
public sealed class PdfBrandingRenderTests
{
    // A valid 1x1 PNG.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static QuestPdfRenderer Renderer() => new(Options.Create(new PdfOptions { QuestPdfLicense = "community" }));

    private static PdfDoc Sample(PdfStyle? style) => new("INVOICE", "INV-1", "Ade Tailors", "12 Market Road", [new("Issue date", "2026-10-01")], "Bill to", [new("Customer", "Ada Obi")],
        new PdfTable(["#", "Description", "Total"], [["1", "Agbada", "1,075.00"]], new HashSet<int> { 0, 2 }), [new("Total", "NGN 1,075.00")],
        [new("Notes", ["Thank you"])], null, "footer") { Style = style };

    [Fact]
    public void A_branded_document_with_a_logo_renders_as_a_real_pdf()
    {
        var style = new PdfStyle("#7A1F3D", TinyPng, "+234 801 234 5678", "hello@ade.test", "TIN 123-456", "Thank you for your custom.");
        var bytes = Renderer().Render(Sample(style));
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void A_style_without_a_logo_or_contact_details_still_renders()
    {
        var bytes = Renderer().Render(Sample(new PdfStyle("#1F3A5F", null, null, null, null, null)));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void A_document_with_no_style_keeps_the_default_look()
    {
        var bytes = Renderer().Render(Sample(null));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }
}
