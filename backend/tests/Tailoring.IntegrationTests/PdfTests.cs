using System.Text.Json;
using Microsoft.Extensions.Options;
using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Application.Measurements;
using Tailoring.Application.Orders;
using Tailoring.Application.Pdf;
using Tailoring.Application.Production;
using Tailoring.Infrastructure.Pdf;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Document content is built by pure code from records, so it is tested without a database or a PDF library.</summary>
public sealed class PdfDocumentTests
{
    private static readonly PdfBranding Brand = new("Ade Tailors", "12 Market Road, Lagos");
    private static readonly DateTime T = new(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);

    private static PaymentDto Pay(string status, decimal amount, string? receipt = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), "INV-1", Guid.NewGuid(), amount, "NGN", "Transfer", null, new DateOnly(2026, 10, 2), status, null, null,
            null, null, null, null, receipt, 1, T);

    private static InvoiceDto Invoice(string status = "Issued", string? number = "INV-2026-0001", decimal paid = 0, decimal pending = 0, decimal credited = 0, string? voidReason = null, IReadOnlyList<PaymentDto>? payments = null)
        => new(Guid.NewGuid(), number, "Standard", status, Guid.NewGuid(), "Ada Obi", null, "ORD-2026-0007", "NGN", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15),
            "Thank you", "Pay within 14 days", 1000m, 75m, 1075m, paid, pending, credited, 1075m - paid - credited, "Unpaid", false, voidReason, 1, T, T,
            [new InvoiceLineDto(Guid.NewGuid(), 1, "Agbada, 2 piece", 2, 500m, 0m, 7.5m, 1000m, 75m, 1075m)], payments ?? [], []);

    [Fact]
    public void Money_and_filenames_are_culture_independent_and_safe()
    {
        Assert.Equal("NGN 1,234.50", PdfFormat.Money(1234.5m, "NGN"));
        Assert.Equal("Invoice-INV-2026-0001.pdf", PdfFormat.SafeFileName("Invoice", "INV-2026-0001", "draft"));
        Assert.Equal("Invoice-draft.pdf", PdfFormat.SafeFileName("Invoice", null, "draft"));
        Assert.DoesNotContain("/", PdfFormat.SafeFileName("Invoice", "a/b\\c", "x"));
    }

    [Fact]
    public void An_issued_invoice_shows_totals_and_balance_due_and_no_stamp()
    {
        var d = PdfDocuments.Invoice(Invoice(), Brand);
        Assert.Equal("INV-2026-0001", d.Number);
        Assert.Null(d.Stamp);
        Assert.Equal("Ade Tailors", d.BusinessName);
        Assert.Contains(d.Totals, t => t is { Label: "Balance due", Value: "NGN 1,075.00" });
        Assert.Equal("1,075.00", d.Table!.Rows.Single()[6]);
    }

    [Fact]
    public void Drafts_and_voids_are_stamped_and_a_draft_shows_no_balance_due()
    {
        var draft = PdfDocuments.Invoice(Invoice("Draft", number: null), Brand);
        Assert.Equal("DRAFT", draft.Number);
        Assert.StartsWith("DRAFT", draft.Stamp);
        Assert.DoesNotContain(draft.Totals, t => t.Label == "Balance due");

        var voided = PdfDocuments.Invoice(Invoice("Void", voidReason: "Wrong customer"), Brand);
        Assert.Equal("VOID - Wrong customer", voided.Stamp);
    }

    [Fact]
    public void Only_verified_payments_are_listed_and_pending_money_is_called_out_but_not_counted()
    {
        var d = PdfDocuments.Invoice(Invoice(paid: 300m, pending: 200m,
            payments: [Pay("Verified", 300m, "RCT-1"), Pay("Pending", 200m), Pay("Rejected", 50m)]), Brand);

        var received = d.Sections.Single(s => s.Heading == "Payments received");
        Assert.Single(received.Lines);
        Assert.Contains("RCT-1", received.Lines[0]);
        Assert.Contains(d.Sections, s => s.Heading == "Note" && s.Lines[0].Contains("awaiting verification"));
        Assert.Contains(d.Totals, t => t is { Label: "Paid", Value: "NGN 300.00" });
    }

    [Fact]
    public void A_voided_receipt_is_marked_void()
    {
        var r = new ReceiptDto(Guid.NewGuid(), "RCT-2026-0001", Guid.NewGuid(), Guid.NewGuid(), "INV-1", Guid.NewGuid(), "Ada Obi", "NGN", 300m, "Transfer", "REF9", 775m, T, IsVoid: true);
        var d = PdfDocuments.Receipt(r, Brand);
        Assert.StartsWith("VOID", d.Stamp);
        Assert.Contains(d.Meta, m => m is { Label: "Reference", Value: "REF9" });
        Assert.Contains(d.Totals, t => t is { Label: "Balance after this payment", Value: "NGN 775.00" });
    }

    [Fact]
    public void A_credit_note_names_the_invoice_it_reduces()
    {
        var inv = Invoice();
        var d = PdfDocuments.CreditNote(inv, new CreditNoteDto(Guid.NewGuid(), "CN-2026-0001", 100m, "Fabric flaw", T), Brand);
        Assert.Equal("CN-2026-0001", d.Number);
        Assert.Contains(d.Meta, m => m is { Label: "Against invoice", Value: "INV-2026-0001" });
        Assert.Equal("Fabric flaw", d.Sections.Single().Lines.Single());
    }

    [Fact]
    public void A_quotation_says_it_is_not_an_invoice_and_that_prices_exclude_tax()
    {
        var g = new OrderGarmentDto(Guid.NewGuid(), 1, "Senator suit", null, 1, 200m, 200m, "Wool", "Navy", null, null, null, null, null);
        var o = new OrderDto(Guid.NewGuid(), "ORD-2026-0007", "Quoted", Guid.NewGuid(), "Ada Obi", Guid.NewGuid(), new DateOnly(2026, 11, 1), "Normal", "Collection",
            null, null, null, "NGN", 200m, 20m, 180m, 1, T, T, null, null, null, [g]);
        var d = PdfDocuments.Quotation(o, Brand);

        Assert.Null(d.Stamp);
        Assert.Contains("not an invoice", d.Sections[0].Lines[0]);
        Assert.Contains("exclude tax", d.Sections[0].Lines[0]);
        Assert.Contains(d.Totals, t => t is { Label: "Discount", Value: "-NGN 20.00" });
        Assert.Contains("Fabric: Wool", d.Table!.Rows[0][1]);
        Assert.StartsWith("DRAFT", PdfDocuments.Quotation(o with { Status = "Draft" }, Brand).Stamp);
    }

    [Fact]
    public void Measurement_sheets_list_every_value_with_its_unit_and_revision()
    {
        var set = new MeasurementSetDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Shirt", "Everyday", 3,
            [new MeasurementValueDto("chest", "Chest", "cm", 100m), new MeasurementValueDto("neck", "Neck", "cm", 40.5m)], T);
        var d = PdfDocuments.MeasurementSheet(set, "Ada Obi", Brand);
        Assert.Equal(2, d.Table!.Rows.Count);
        Assert.Equal(new[] { "Neck", "40.5", "cm" }, d.Table.Rows[1]);
        Assert.Contains(d.Meta, m => m is { Label: "Revision", Value: "3" });
    }

    [Theory]
    [InlineData("""{"values":[{"key":"chest","label":"Chest","unit":"cm","value":100},{"label":"Bad","value":"x"}]}""", "Chest: 100 cm")]
    [InlineData("""{"values":{"chest":100,"note":"text"}}""", "chest: 100")]
    public void Frozen_snapshots_are_read_in_either_shape_and_junk_is_ignored(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, Assert.Single(PdfDocuments.SnapshotLines(doc.RootElement.Clone())));
    }

    [Fact]
    public void Unreadable_snapshots_produce_no_lines()
    {
        using var doc = JsonDocument.Parse("""[1,2,3]""");
        Assert.Empty(PdfDocuments.SnapshotLines(doc.RootElement.Clone()));
        Assert.Empty(PdfDocuments.SnapshotLines(null));
    }

    private static TaskDetailDto TaskDetail(string? customerName)
        => new(new TaskSummaryDto(Guid.NewGuid(), Guid.NewGuid(), "ORD-2026-0007", Guid.NewGuid(), "Senator suit", "Cutting", 2, "Cut fabric", 1, "Ready", "High", T, false,
                null, "Bayo", null, 90, false, 0, true, false, null, null, 1),
            "Mind the nap", ["Check grain", "Check width"], ["Check grain"], [new MaterialRequirementDto("Navy wool", 3.5m, "m")], null, null, null, null, null, [],
            new GarmentSpecDto("Senator suit", null, 1, "Wool", "Navy", "Slim", null, null), customerName, []);

    [Fact]
    public void A_worker_job_sheet_has_no_customer_block_and_a_manager_one_does()
    {
        var worker = PdfDocuments.JobSheet(TaskDetail(null), Brand);
        Assert.Empty(worker.Parties);
        Assert.Null(worker.PartiesHeading);
        Assert.Null(worker.BusinessAddress);

        var manager = PdfDocuments.JobSheet(TaskDetail("Ada Obi"), Brand);
        Assert.Equal("Ada Obi", manager.Parties.Single().Value);

        var checks = manager.Sections.Single(s => s.Heading == "Checks").Lines;
        Assert.Equal(new[] { "[x] Check grain", "[ ] Check width" }, checks);
    }
}

/// <summary>Drawing needs QuestPDF and its native libraries. Real-license use is the deployer's decision.</summary>
public sealed class PdfRendererTests
{
    private static QuestPdfRenderer Renderer(string license) => new(Options.Create(new PdfOptions { QuestPdfLicense = license }));

    private static PdfDoc Sample() => new("INVOICE", "INV-1", "Ade Tailors", "12 Market Road", [new("Issue date", "2026-10-01")], "Bill to", [new("Customer", "Ada Obi")],
        new PdfTable(["#", "Description", "Total"], [["1", "Agbada", "1,075.00"]], new HashSet<int> { 0, 2 }), [new("Total", "NGN 1,075.00")],
        [new("Notes", ["Thank you"])], "DRAFT", "footer");

    [Fact]
    public void Without_a_license_choice_rendering_is_refused_with_a_clear_code()
    {
        foreach (var value in new[] { "", "   ", "free", "yes" })
        {
            var ex = Assert.Throws<AppException>(() => Renderer(value).Render(Sample()));
            Assert.Equal(503, ex.StatusCode);
            Assert.Equal("pdf_license_not_configured", ex.Code);
        }
    }

    [Fact]
    public void A_rendered_document_is_a_real_pdf()
    {
        var bytes = Renderer("community").Render(Sample());
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }
}
