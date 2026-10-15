using System.Globalization;
using System.Text.Json;
using Tailoring.Application.Finance;
using Tailoring.Application.Measurements;
using Tailoring.Application.Orders;
using Tailoring.Application.Production;

namespace Tailoring.Application.Pdf;

public sealed record PdfKv(string Label, string Value);
public sealed record PdfTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlySet<int> RightAlignedColumns);
public sealed record PdfSection(string Heading, IReadOnlyList<string> Lines);

/// <summary>Layout-free description of a printable document. Built from real records only; the renderer just draws it.</summary>
public sealed record PdfDoc(
    string Title, string? Number, string BusinessName, string? BusinessAddress,
    IReadOnlyList<PdfKv> Meta, string? PartiesHeading, IReadOnlyList<PdfKv> Parties, PdfTable? Table,
    IReadOnlyList<PdfKv> Totals, IReadOnlyList<PdfSection> Sections, string? Stamp, string? Footer);

public sealed record PdfBranding(string BusinessName, string? Address);
public sealed record PdfFile(byte[] Content, string FileName);

public interface IPdfRenderer { byte[] Render(PdfDoc document); }

/// <summary>Each method authorises through the existing tenant-scoped service it reads from: another business's id is "not found".</summary>
public interface IPdfService
{
    Task<PdfFile> InvoiceAsync(Guid invoiceId, CancellationToken ct = default);
    Task<PdfFile> ReceiptAsync(Guid receiptId, CancellationToken ct = default);
    Task<PdfFile> CreditNoteAsync(Guid invoiceId, Guid creditNoteId, CancellationToken ct = default);
    Task<PdfFile> QuotationAsync(Guid orderId, CancellationToken ct = default);
    Task<PdfFile> MeasurementSheetAsync(Guid customerId, Guid setId, CancellationToken ct = default);
    /// <summary>asWorker reads through the worker's own task view, so only tasks assigned to the caller are reachable.</summary>
    Task<PdfFile> JobSheetAsync(Guid taskId, bool asWorker, CancellationToken ct = default);
}

public static class PdfFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string Money(decimal amount, string currency) => $"{currency} {amount.ToString("N2", Inv)}";
    public static string Number2(decimal v) => v.ToString("N2", Inv);
    public static string Quantity(decimal v) => v.ToString("0.###", Inv);
    public static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", Inv) ?? "-";
    public static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd", Inv) ?? "-";
    public static string DateTimeUtc(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm 'UTC'", Inv) ?? "-";

    /// <summary>File names are built from record numbers; keep them to safe characters.</summary>
    public static string SafeFileName(string prefix, string? number, string fallback)
    {
        var cleaned = new string((number ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        return $"{prefix}-{(cleaned.Length == 0 ? fallback : cleaned)}.pdf";
    }
}

public static class PdfDocuments
{
    private static readonly IReadOnlyList<PdfKv> NoKv = [];
    private static readonly IReadOnlyList<PdfSection> NoSections = [];

    public static PdfDoc Invoice(InvoiceDto i, PdfBranding b)
    {
        var cur = i.CurrencyCode;
        var meta = new List<PdfKv> { new("Issue date", PdfFormat.Date(i.IssueDate)), new("Due date", PdfFormat.Date(i.DueDate)) };
        if (i.OrderNumber is not null) meta.Add(new("Order", i.OrderNumber));
        if (i.Kind != "Standard") meta.Add(new("Type", i.Kind));
        if (i.PaymentStatus is not null) meta.Add(new("Payment status", i.PaymentStatus));

        var table = new PdfTable(["#", "Description", "Qty", "Unit price", "Discount", "Tax %", "Total"],
            i.Lines.OrderBy(l => l.LineNumber).Select(l => (IReadOnlyList<string>)
            [
                l.LineNumber.ToString(CultureInfo.InvariantCulture), l.Description, PdfFormat.Quantity(l.Quantity),
                PdfFormat.Number2(l.UnitPrice), PdfFormat.Number2(l.Discount), PdfFormat.Quantity(l.TaxPercent), PdfFormat.Number2(l.LineTotal)
            ]).ToList(), new HashSet<int> { 0, 2, 3, 4, 5, 6 });

        var totals = new List<PdfKv> { new("Subtotal", PdfFormat.Money(i.Subtotal, cur)), new("Tax", PdfFormat.Money(i.TaxTotal, cur)), new("Total", PdfFormat.Money(i.Total, cur)) };
        if (i.AmountPaid > 0) totals.Add(new("Paid", PdfFormat.Money(i.AmountPaid, cur)));
        if (i.AmountCredited > 0) totals.Add(new("Credited", PdfFormat.Money(i.AmountCredited, cur)));
        if (i.Status == "Issued") totals.Add(new("Balance due", PdfFormat.Money(i.Balance, cur)));

        var sections = new List<PdfSection>();
        // Only verified payments are shown: a claimed or pending payment is not money received.
        var received = i.Payments.Where(p => p.Status == "Verified").OrderBy(p => p.ReceivedOn)
            .Select(p => $"{PdfFormat.Date(p.ReceivedOn)}  {p.Method}  {PdfFormat.Money(p.Amount, cur)}" + (p.ReceiptNumber is null ? "" : $"  (receipt {p.ReceiptNumber})")).ToList();
        if (received.Count > 0) sections.Add(new("Payments received", received));
        if (i.AmountPending > 0) sections.Add(new("Note", [$"{PdfFormat.Money(i.AmountPending, cur)} is awaiting verification and is not counted above."]));
        if (!string.IsNullOrWhiteSpace(i.Notes)) sections.Add(new("Notes", Lines(i.Notes)));
        if (!string.IsNullOrWhiteSpace(i.Terms)) sections.Add(new("Terms", Lines(i.Terms)));

        var stamp = i.Status switch
        {
            "Draft" => "DRAFT - not a valid invoice",
            "Void" => "VOID" + (string.IsNullOrWhiteSpace(i.VoidReason) ? "" : $" - {i.VoidReason}"),
            _ => null
        };
        return new PdfDoc("INVOICE", i.Number ?? "DRAFT", b.BusinessName, b.Address, meta, "Bill to", [new("Customer", i.CustomerName)], table, totals, sections, stamp, null);
    }

    public static PdfDoc Receipt(ReceiptDto r, PdfBranding b)
    {
        var meta = new List<PdfKv> { new("Date", PdfFormat.DateTimeUtc(r.IssuedAtUtc)), new("Invoice", r.InvoiceNumber ?? "-"), new("Method", r.Method) };
        if (!string.IsNullOrWhiteSpace(r.Reference)) meta.Add(new("Reference", r.Reference!));
        var totals = new List<PdfKv> { new("Amount received", PdfFormat.Money(r.Amount, r.CurrencyCode)), new("Balance after this payment", PdfFormat.Money(r.BalanceAfter, r.CurrencyCode)) };
        return new PdfDoc("RECEIPT", r.Number, b.BusinessName, b.Address, meta, "Received from", [new("Customer", r.CustomerName)], null, totals, NoSections,
            r.IsVoid ? "VOID - this receipt was cancelled" : null, "Thank you for your payment.");
    }

    public static PdfDoc CreditNote(InvoiceDto i, CreditNoteDto c, PdfBranding b)
    {
        var meta = new List<PdfKv> { new("Date", PdfFormat.Date(c.CreatedAtUtc)), new("Against invoice", i.Number ?? "-") };
        var totals = new List<PdfKv> { new("Credit amount", PdfFormat.Money(c.Amount, i.CurrencyCode)) };
        return new PdfDoc("CREDIT NOTE", c.Number, b.BusinessName, b.Address, meta, "Credited to", [new("Customer", i.CustomerName)], null, totals,
            [new("Reason", Lines(c.Reason))], null, null);
    }

    public static PdfDoc Quotation(OrderDto o, PdfBranding b)
    {
        var cur = o.CurrencyCode;
        var meta = new List<PdfKv> { new("Date", PdfFormat.Date(o.QuotedAtUtc ?? o.CreatedAtUtc)), new("Promised date", PdfFormat.Date(o.PromisedDate)), new("Delivery", o.DeliveryMethod) };
        var rows = o.Garments.OrderBy(g => g.LineNumber).Select(g => (IReadOnlyList<string>)
        [
            g.LineNumber.ToString(CultureInfo.InvariantCulture),
            string.Join("\n", new[] { g.Name, Detail("Fabric", g.Fabric), Detail("Colour", g.Colour), Detail("Style", g.StyleInstructions), Detail("Alteration", g.AlterationNotes) }.Where(s => s.Length > 0)),
            g.Quantity.ToString(CultureInfo.InvariantCulture), PdfFormat.Number2(g.UnitPrice), PdfFormat.Number2(g.LineTotal)
        ]).ToList();
        var totals = new List<PdfKv> { new("Subtotal", PdfFormat.Money(o.Subtotal, cur)) };
        if (o.Discount > 0) totals.Add(new("Discount", "-" + PdfFormat.Money(o.Discount, cur)));
        totals.Add(new("Quoted total", PdfFormat.Money(o.Total, cur)));

        var sections = new List<PdfSection> { new("Please note", ["This is a quotation, not an invoice. Prices exclude tax; any tax is added when the invoice is issued."]) };
        if (!string.IsNullOrWhiteSpace(o.Notes)) sections.Add(new("Notes", Lines(o.Notes)));
        var stamp = o.Status switch { "Draft" => "DRAFT - not yet quoted", "Cancelled" => "CANCELLED", _ => null };
        return new PdfDoc("QUOTATION", o.OrderNumber, b.BusinessName, b.Address, meta, "Prepared for", [new("Customer", o.CustomerName)],
            new PdfTable(["#", "Item", "Qty", "Unit price", "Total"], rows, new HashSet<int> { 0, 2, 3, 4 }), totals, sections, stamp, null);
    }

    public static PdfDoc MeasurementSheet(MeasurementSetDto s, string customerName, PdfBranding b)
    {
        var meta = new List<PdfKv> { new("Profile", s.Label), new("Template", s.TemplateName), new("Revision", s.CurrentRevision.ToString(CultureInfo.InvariantCulture)), new("Updated", PdfFormat.DateTimeUtc(s.UpdatedAtUtc)) };
        var table = new PdfTable(["Measurement", "Value", "Unit"],
            s.Values.Select(v => (IReadOnlyList<string>)[v.Label, PdfFormat.Quantity(v.Value), v.Unit]).ToList(), new HashSet<int> { 1 });
        return new PdfDoc("MEASUREMENT SHEET", null, b.BusinessName, b.Address, meta, "Customer", [new("Name", customerName)], table, NoKv, NoSections, null,
            "Measurements are as recorded at the revision shown. Later changes create a new revision.");
    }

    public static PdfDoc JobSheet(TaskDetailDto d, PdfBranding b)
    {
        var t = d.Task;
        var meta = new List<PdfKv>
        {
            new("Stage", t.StageName), new("Garment", $"{t.GarmentName} x {t.Quantity}"), new("Priority", t.Priority),
            new("Deadline", PdfFormat.DateTimeUtc(t.DeadlineUtc)), new("Assigned to", t.AssigneeName ?? "Unassigned")
        };
        if (t.EstimatedMinutes is { } m) meta.Add(new("Estimate", $"{m} min"));

        var sections = new List<PdfSection>();
        if (!string.IsNullOrWhiteSpace(d.Instructions)) sections.Add(new("Instructions", Lines(d.Instructions)));
        var spec = new[] { Detail("Fabric", d.Garment.Fabric), Detail("Colour", d.Garment.Colour), Detail("Style", d.Garment.StyleInstructions), Detail("Alteration", d.Garment.AlterationNotes) }.Where(s => s.Length > 0).ToList();
        if (spec.Count > 0) sections.Add(new("Garment", spec));
        var measurements = SnapshotLines(d.Garment.MeasurementSnapshot);
        if (measurements.Count > 0) sections.Add(new("Measurements (frozen with the order)", measurements));
        if (d.RequiredMaterials.Count > 0) sections.Add(new("Materials", d.RequiredMaterials.Select(x => $"{PdfFormat.Quantity(x.Quantity)} {x.Unit}  {x.Name}").ToList()));
        if (d.RequiredChecks.Count > 0)
            sections.Add(new("Checks", d.RequiredChecks.Select(c => (d.CompletedChecks.Contains(c) ? "[x] " : "[ ] ") + c).ToList()));
        if (t.BlockedReason is not null) sections.Add(new("Blocked", [t.BlockedReason]));

        // Workers never receive customer personal data: CustomerName is null for them, so the party block is simply absent.
        IReadOnlyList<PdfKv> parties = d.CustomerName is null ? NoKv : [new("Customer", d.CustomerName)];
        return new PdfDoc("JOB SHEET", t.OrderNumber, b.BusinessName, null, meta, d.CustomerName is null ? null : "Customer", parties, null, NoKv, sections, null,
            "Printed from the live task. Check the app for the latest status.");
    }

    /// <summary>Reads the frozen snapshot either as {values:[{label,unit,value}]} or as {values:{key:number}}; anything else yields no lines.</summary>
    public static IReadOnlyList<string> SnapshotLines(JsonElement? snapshot)
    {
        var lines = new List<string>();
        if (snapshot is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("values", out var values)) return lines;
        if (values.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in values.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                var label = Str(v, "label") ?? Str(v, "key");
                var value = v.TryGetProperty("value", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDecimal() : (decimal?)null;
                if (label is not null && value is not null) lines.Add($"{label}: {PdfFormat.Quantity(value.Value)} {Str(v, "unit")}".TrimEnd());
            }
        }
        else if (values.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in values.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number))
                lines.Add($"{p.Name}: {PdfFormat.Quantity(p.Value.GetDecimal())}");
        }
        return lines;

        static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
    }

    private static string Detail(string label, string? value) => string.IsNullOrWhiteSpace(value) ? "" : $"{label}: {value.Trim()}";
    private static IReadOnlyList<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
}
