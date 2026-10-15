using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tailoring.Application.Common;
using Tailoring.Application.Pdf;

namespace Tailoring.Infrastructure.Pdf;

public sealed class PdfOptions
{
    /// <summary>community | professional | enterprise. Deliberately has no default: whoever deploys must decide which QuestPDF license applies to them.</summary>
    public string QuestPdfLicense { get; set; } = "";
}

/// <summary>Draws a <see cref="PdfDoc"/>. No data access, no business rules.</summary>
public sealed class QuestPdfRenderer(IOptions<PdfOptions> options) : IPdfRenderer
{
    private const string Ink = "#1F1F1F", Muted = "#5F6368", Rule = "#D9D9D9", Accent = "#1F3A5F", Warn = "#B3261E";

    public byte[] Render(PdfDoc d)
    {
        EnsureLicense();
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(d.BusinessName).FontSize(16).Bold().FontColor(Accent);
                        if (!string.IsNullOrWhiteSpace(d.BusinessAddress)) c.Item().Text(d.BusinessAddress).FontSize(9).FontColor(Muted);
                    });
                    row.ConstantItem(190).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text(d.Title).FontSize(18).Bold();
                        if (d.Number is not null) c.Item().AlignRight().Text(d.Number).FontSize(11).FontColor(Muted);
                    });
                });
                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Rule);
            });

            page.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(12);
                if (d.Stamp is not null) col.Item().Text(d.Stamp).Bold().FontSize(12).FontColor(Warn);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        if (d.PartiesHeading is not null) c.Item().Text(d.PartiesHeading).FontSize(9).FontColor(Muted);
                        foreach (var p in d.Parties) c.Item().Text(p.Value).Bold();
                    });
                    row.RelativeItem().Column(c =>
                    {
                        foreach (var m in d.Meta)
                            c.Item().Row(r => { r.RelativeItem().Text(m.Label).FontColor(Muted); r.RelativeItem().AlignRight().Text(m.Value); });
                    });
                });

                if (d.Table is { } table) col.Item().Element(e => DrawTable(e, table));

                if (d.Totals.Count > 0)
                    col.Item().AlignRight().Width(260).Column(c =>
                    {
                        foreach (var (t, i) in d.Totals.Select((t, i) => (t, i)))
                        {
                            var last = i == d.Totals.Count - 1;
                            c.Item().PaddingVertical(2).Row(r =>
                            {
                                var left = r.RelativeItem().Text(t.Label);
                                var right = r.AutoItem().Text(t.Value);
                                if (last) { left.Bold(); right.Bold(); }
                            });
                        }
                    });

                foreach (var s in d.Sections)
                    col.Item().Column(c =>
                    {
                        c.Item().Text(s.Heading).Bold().FontColor(Accent);
                        foreach (var line in s.Lines) c.Item().Text(line);
                    });
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(d.Footer ?? "").FontSize(8).FontColor(Muted);
                row.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                    t.Span("Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    private static void DrawTable(IContainer container, PdfTable table)
    {
        var count = table.Headers.Count;
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                for (var i = 0; i < count; i++)
                {
                    // narrow columns for numbers and the row number, wide for descriptions
                    if (i == 0 && table.RightAlignedColumns.Contains(0)) c.ConstantColumn(24);
                    else if (table.RightAlignedColumns.Contains(i)) c.RelativeColumn(1.3f);
                    else c.RelativeColumn(4);
                }
            });

            t.Header(h =>
            {
                for (var i = 0; i < count; i++)
                {
                    var cell = h.Cell().BorderBottom(1).BorderColor(Accent).PaddingVertical(4);
                    var text = table.RightAlignedColumns.Contains(i) ? cell.AlignRight() : cell;
                    text.Text(table.Headers[i]).Bold().FontSize(9);
                }
            });

            foreach (var row in table.Rows)
                for (var i = 0; i < count; i++)
                {
                    var cell = t.Cell().BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(4);
                    var aligned = table.RightAlignedColumns.Contains(i) ? cell.AlignRight() : cell;
                    aligned.Text(i < row.Count ? row[i] : "");
                }
        });
    }

    /// <summary>QuestPDF keeps its license in a static, so it is (re)applied from configuration on every render; an unset value always refuses.</summary>
    private void EnsureLicense()
    {
        QuestPDF.Settings.License = options.Value.QuestPdfLicense.Trim().ToLowerInvariant() switch
        {
            "community" => LicenseType.Community,
            "professional" => LicenseType.Professional,
            "enterprise" => LicenseType.Enterprise,
            _ => throw new AppException(503, "pdf_license_not_configured",
                "PDF generation is switched off until a QuestPDF license is chosen (Pdf:QuestPdfLicense = community, professional or enterprise).")
        };
    }
}
