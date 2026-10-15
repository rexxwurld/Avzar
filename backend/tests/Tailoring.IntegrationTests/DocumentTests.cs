using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tailoring.Application.Common;
using Tailoring.Application.Documents;
using Tailoring.Application.Media;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Documents;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Tailoring.Infrastructure.Storage;
using Xunit;

namespace Tailoring.IntegrationTests;

internal static class TestDocs
{
    public static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");

    public static byte[] Zip(IEnumerable<(string Name, string Content)> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open()); w.Write(content);
            }
        return ms.ToArray();
    }

    public const string PlainTypes = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"></Types>";
    public static byte[] Docx() => Zip([("[Content_Types].xml", PlainTypes), ("word/document.xml", "<w:document/>")]);
    public static byte[] Xlsx() => Zip([("[Content_Types].xml", PlainTypes), ("xl/workbook.xml", "<workbook/>")]);
}

internal sealed class FakeScanner(ScanOutcome outcome, string? detail = null) : IVirusScanner
{
    public int Calls;
    public Task<ScanResult> ScanAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    { Interlocked.Increment(ref Calls); return Task.FromResult(new ScanResult(outcome, detail)); }
}

public sealed class DocumentSnifferTests
{
    private static AppException Rejected(byte[] data, string name) => Assert.Throws<AppException>(() => DocumentSniffer.Inspect(data, name));

    [Fact] public void Accepts_a_pdf() => Assert.Equal((DocumentKind.Pdf, "application/pdf"), Tuple(DocumentSniffer.Inspect(TestDocs.Pdf(), "Invoice.PDF")));
    [Fact] public void Accepts_docx() => Assert.Equal(DocumentKind.Word, DocumentSniffer.Inspect(TestDocs.Docx(), "a.docx").Kind);
    [Fact] public void Accepts_xlsx() => Assert.Equal(DocumentKind.Excel, DocumentSniffer.Inspect(TestDocs.Xlsx(), "a.xlsx").Kind);
    [Fact] public void Accepts_utf8_csv_and_txt()
    {
        Assert.Equal("text/csv; charset=utf-8", DocumentSniffer.Inspect(Encoding.UTF8.GetBytes("name,price\nAnkara dress,₦15000\n"), "list.csv").ContentType);
        Assert.Equal("text/plain; charset=utf-8", DocumentSniffer.Inspect(Encoding.UTF8.GetBytes("hello"), "n.txt").ContentType);
    }

    [Fact] public void A_pdf_named_as_something_else_is_refused() => Assert.Equal("extension_mismatch", Rejected(TestDocs.Pdf(), "invoice.docx").Code);
    [Fact] public void A_docx_named_pdf_is_refused() => Assert.Equal("extension_mismatch", Rejected(TestDocs.Docx(), "x.pdf").Code);
    [Fact] public void A_missing_extension_is_refused() => Assert.Equal("extension_mismatch", Rejected(TestDocs.Pdf(), "invoice").Code);

    [Fact]
    public void Macro_files_are_refused()
    {
        var withVba = TestDocs.Zip([("[Content_Types].xml", TestDocs.PlainTypes), ("word/document.xml", "<d/>"), ("word/vbaProject.bin", "x")]);
        Assert.Equal("macros_not_allowed", Rejected(withVba, "a.docx").Code);
        var macroType = TestDocs.Zip([("[Content_Types].xml", "<Types>application/vnd.ms-word.document.macroEnabled.main+xml</Types>"), ("word/document.xml", "<d/>")]);
        Assert.Equal("macros_not_allowed", Rejected(macroType, "a.docx").Code);
    }

    [Fact] public void A_zip_that_is_not_an_office_file_is_refused()
    {
        Assert.Equal("unsupported_document", Rejected(TestDocs.Zip([("readme.txt", "hi")]), "a.docx").Code);
        Assert.Equal("unsupported_document", Rejected(TestDocs.Zip([("[Content_Types].xml", TestDocs.PlainTypes), ("other/file.xml", "<a/>")]), "a.docx").Code);
    }

    [Fact] public void A_zip_with_too_many_entries_is_refused()
    {
        var many = TestDocs.Zip(Enumerable.Range(0, 5001).Select(i => ($"f{i}.xml", "")).Append(("[Content_Types].xml", TestDocs.PlainTypes)).Append(("word/document.xml", "<d/>")));
        Assert.Equal("unsafe_archive", Rejected(many, "a.docx").Code);
    }

    [Fact] public void Old_binary_office_formats_are_refused() => Assert.Equal("unsupported_document", Rejected([0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3], "a.doc").Code);
    [Fact] public void Executables_and_unknown_bytes_are_refused()
    {
        Assert.Equal("unsupported_document", Rejected(Encoding.ASCII.GetBytes("MZ\x90\0\x03"), "tool.exe").Code);
        Assert.Equal("unsupported_document", Rejected(Encoding.ASCII.GetBytes("hello"), "notes.pdf").Code);
    }
    [Fact] public void Corrupt_zip_data_is_refused() => Assert.Equal("unsupported_document", Rejected([(byte)'P', (byte)'K', 3, 4, 9, 9, 9, 9], "a.docx").Code);
    [Fact] public void Binary_data_named_csv_is_refused() => Assert.Equal("invalid_text", Rejected([1, 2, 0, 3], "a.csv").Code);
    [Fact] public void Non_utf8_text_is_refused() => Assert.Equal("invalid_text", Rejected([0xFF, 0xFE, 0xFA], "a.txt").Code);

    private static (DocumentKind, string) Tuple(SniffedDocument d) => (d.Kind, d.ContentType);
}

public sealed class ClamAvScannerTests
{
    [Theory]
    [InlineData("stream: OK\0", ScanOutcome.Clean, null)]
    [InlineData("stream: Eicar-Test-Signature FOUND\0", ScanOutcome.Infected, "Eicar-Test-Signature")]
    [InlineData("INSTREAM size limit exceeded. ERROR\0", ScanOutcome.Unavailable, "scanner_error")]
    [InlineData("", ScanOutcome.Unavailable, "scanner_error")]
    public void Parses_clamd_replies(string raw, ScanOutcome expected, string? detail)
    {
        var r = ClamAvScanner.ParseResponse(raw);
        Assert.Equal((expected, detail), (r.Outcome, r.Detail));
    }

    /// <summary>A tiny clamd stand-in that speaks INSTREAM and records what it received.</summary>
    private sealed class FakeClamd : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public long Received;
        public string? Command;
        public Task Serving { get; private set; } = Task.CompletedTask;

        public FakeClamd(string reply) { _listener.Start(); Serving = ServeAsync(reply); }

        private async Task ServeAsync(string reply)
        {
            using var client = await _listener.AcceptTcpClientAsync();
            var s = client.GetStream();
            var cmd = new byte[10]; await s.ReadExactlyAsync(cmd);
            Command = Encoding.ASCII.GetString(cmd);
            var len = new byte[4];
            while (true)
            {
                await s.ReadExactlyAsync(len);
                var n = BinaryPrimitives.ReadUInt32BigEndian(len);
                if (n == 0) break;
                var chunk = new byte[n]; await s.ReadExactlyAsync(chunk);
                Received += n;
            }
            await s.WriteAsync(Encoding.ASCII.GetBytes(reply));
        }
        public void Dispose() => _listener.Stop();
    }

    private static ClamAvScanner Scanner(int port, int timeout = 5)
        => new(Options.Create(new AntivirusOptions { Provider = "clamav", Host = "127.0.0.1", Port = port, TimeoutSeconds = timeout }), NullLogger<ClamAvScanner>.Instance);

    [Fact]
    public async Task Streams_every_byte_in_chunks_and_reports_clean()
    {
        using var server = new FakeClamd("stream: OK\0");
        var data = new byte[150_000]; new Random(1).NextBytes(data);
        var r = await Scanner(server.Port).ScanAsync(data);
        await server.Serving;
        Assert.Equal(ScanOutcome.Clean, r.Outcome);
        Assert.Equal(data.Length, server.Received);
        Assert.Equal("zINSTREAM\0", server.Command);
    }

    [Fact]
    public async Task Reports_infection_with_the_signature_name()
    {
        using var server = new FakeClamd("stream: Win.Test.EICAR_HDB-1 FOUND\0");
        var r = await Scanner(server.Port).ScanAsync(new byte[] { 1, 2, 3 });
        Assert.Equal((ScanOutcome.Infected, "Win.Test.EICAR_HDB-1"), (r.Outcome, r.Detail));
    }

    [Fact]
    public async Task An_unreachable_scanner_is_unavailable_never_clean()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); // a port nobody listens on
        var r = await Scanner(port).ScanAsync(new byte[] { 1 });
        Assert.Equal(ScanOutcome.Unavailable, r.Outcome);
    }

    [Fact]
    public void Config_rules()
    {
        new AntivirusOptions().Validate();
        Assert.Throws<InvalidOperationException>(new AntivirusOptions { Provider = "norton" }.Validate);
        Assert.Throws<InvalidOperationException>(new AntivirusOptions { Provider = "none", Required = true }.Validate);
        Assert.Throws<InvalidOperationException>(new AntivirusOptions { Provider = "clamav", Port = 0 }.Validate);
    }
}

public sealed class DocumentTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private readonly InMemoryObjectStorage _storage = new();

    private async Task<Guid> NewBusinessAsync(string plan = "professional")
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == plan);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private async Task<Guid> NewCustomerAsync(Guid bid)
    {
        var db = fx.Create(bid);
        var c = new Customer { BusinessId = bid, FullName = "Ada " + Guid.NewGuid().ToString("N")[..4] };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    private DocumentService Docs(Guid bid, IVirusScanner? scanner = null, StorageOptions? storage = null, DocumentOptions? docs = null, AntivirusOptions? av = null)
    {
        var db = fx.Create(bid);
        return new DocumentService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db), _storage,
            scanner ?? new FakeScanner(ScanOutcome.Clean), Options.Create(storage ?? new StorageOptions()),
            Options.Create(docs ?? new DocumentOptions()), Options.Create(av ?? new AntivirusOptions()));
    }

    private Task<DocumentDto> Upload(Guid bid, byte[]? bytes = null, string name = "measurements.pdf", Guid? customerId = null, Guid? orderId = null, DocumentService? svc = null)
        => (svc ?? Docs(bid)).UploadAsync(new MemoryStream(bytes ?? TestDocs.Pdf()), name, "note", customerId, orderId);

    [Fact]
    public async Task Upload_stores_the_file_and_a_row_with_a_server_chosen_type()
    {
        var bid = await NewBusinessAsync();
        var dto = await Upload(bid, name: "../../Invoice.PDF");
        Assert.Equal(("Invoice.PDF", "Pdf", "application/pdf", "Clean"), (dto.FileName, dto.Kind, dto.ContentType, dto.ScanStatus));
        Assert.True(_storage.Objects.ContainsKey(DocumentKeys.KeyFor(bid, dto.Id)));
        Assert.Single((await Docs(bid).ListAsync(new DocumentListQuery())).Items, d => d.Id == dto.Id);
        await using var content = (await Docs(bid).OpenAsync(dto.Id)).Content;
        using var ms = new MemoryStream(); await content.CopyToAsync(ms);
        Assert.Equal(TestDocs.Pdf(), ms.ToArray());
    }

    [Fact]
    public async Task A_document_can_be_linked_to_a_customer_and_listed_by_it()
    {
        var bid = await NewBusinessAsync();
        var c1 = await NewCustomerAsync(bid); var c2 = await NewCustomerAsync(bid);
        await Upload(bid, customerId: c1); await Upload(bid, customerId: c1); await Upload(bid, customerId: c2);
        Assert.Equal(2, (await Docs(bid).ListAsync(new DocumentListQuery { CustomerId = c1 })).Total);
    }

    [Fact]
    public async Task Another_business_customer_or_order_behaves_like_a_missing_one_and_stores_nothing()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var foreignCustomer = await NewCustomerAsync(b);
        var before = _storage.Objects.Count;
        var ex1 = await Assert.ThrowsAsync<AppException>(() => Upload(a, customerId: foreignCustomer));
        var ex2 = await Assert.ThrowsAsync<AppException>(() => Upload(a, customerId: Guid.NewGuid()));
        Assert.Equal((404, "customer_not_found"), (ex1.StatusCode, ex1.Code));
        Assert.Equal((ex1.StatusCode, ex1.Code, ex1.Message), (ex2.StatusCode, ex2.Code, ex2.Message));
        var ex3 = await Assert.ThrowsAsync<AppException>(() => Upload(a, orderId: Guid.NewGuid()));
        Assert.Equal("order_not_found", ex3.Code);
        Assert.Equal(before, _storage.Objects.Count);
    }

    [Fact]
    public async Task An_infected_file_is_refused_and_nothing_is_stored()
    {
        var bid = await NewBusinessAsync();
        var before = _storage.Objects.Count;
        var ex = await Assert.ThrowsAsync<AppException>(() => Upload(bid, svc: Docs(bid, new FakeScanner(ScanOutcome.Infected, "Eicar"))));
        Assert.Equal((422, "file_infected"), (ex.StatusCode, ex.Code));
        Assert.Equal(before, _storage.Objects.Count);
        Assert.Equal(0, (await Docs(bid).ListAsync(new DocumentListQuery())).Total);
    }

    [Fact]
    public async Task A_down_scanner_blocks_uploads_only_when_scanning_is_required()
    {
        var bid = await NewBusinessAsync();
        var down = new FakeScanner(ScanOutcome.Unavailable, "scanner_unreachable");

        var ex = await Assert.ThrowsAsync<AppException>(() => Upload(bid, svc: Docs(bid, down, av: new AntivirusOptions { Provider = "clamav", Required = true })));
        Assert.Equal((503, "scan_unavailable"), (ex.StatusCode, ex.Code));
        Assert.Equal(0, (await Docs(bid).ListAsync(new DocumentListQuery())).Total);

        var ok = await Upload(bid, svc: Docs(bid, down, av: new AntivirusOptions { Provider = "clamav", Required = false }));
        Assert.Equal("NotScanned", ok.ScanStatus);
        var disabled = await Upload(bid, svc: Docs(bid, new FakeScanner(ScanOutcome.Disabled)));
        Assert.Equal("NotScanned", disabled.ScanStatus);
    }

    [Fact]
    public async Task Bad_files_are_refused_before_scanning_or_storing()
    {
        var bid = await NewBusinessAsync();
        var scanner = new FakeScanner(ScanOutcome.Clean);
        var svc = Docs(bid, scanner);
        var ex = await Assert.ThrowsAsync<AppException>(() => svc.UploadAsync(new MemoryStream(Encoding.ASCII.GetBytes("MZ-not-a-doc")), "setup.pdf", null, null, null));
        Assert.Equal("unsupported_document", ex.Code);
        Assert.Equal(0, scanner.Calls);
        var big = await Assert.ThrowsAsync<AppException>(() => Docs(bid, docs: new DocumentOptions { MaxUploadBytes = 10 }).UploadAsync(new MemoryStream(TestDocs.Pdf()), "a.pdf", null, null, null));
        Assert.Equal((413, "file_too_large"), (big.StatusCode, big.Code));
        var empty = await Assert.ThrowsAsync<AppException>(() => svc.UploadAsync(new MemoryStream(), "a.pdf", null, null, null));
        Assert.Equal("empty_file", empty.Code);
    }

    [Fact]
    public async Task Documents_count_against_the_storage_limit_and_deleting_frees_space()
    {
        var bid = await NewBusinessAsync("free");
        var first = await Upload(bid);
        var tight = new StorageOptions { QuotaBytesPerGb = first.SizeBytes + first.SizeBytes / 2 };
        var ex = await Assert.ThrowsAsync<AppException>(() => Upload(bid, svc: Docs(bid, storage: tight)));
        Assert.Equal("plan_limit_reached", ex.Code);

        var o = Options.Create(tight); var db = fx.Create(bid);
        var media = new MediaService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db), new ImageSharpProcessor(o), _storage, o);
        Assert.Equal(first.SizeBytes, (await media.GetUsageAsync()).UsedBytes); // the shared usage figure includes documents

        await Docs(bid).DeleteAsync(first.Id);
        await Upload(bid, svc: Docs(bid, storage: tight));
    }

    [Fact]
    public async Task Another_business_cannot_see_open_or_delete_a_document()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var dto = await Upload(a);
        Assert.Equal("document_not_found", (await Assert.ThrowsAsync<AppException>(() => Docs(b).GetAsync(dto.Id))).Code);
        Assert.Equal("document_not_found", (await Assert.ThrowsAsync<AppException>(() => Docs(b).OpenAsync(dto.Id))).Code);
        Assert.Equal("document_not_found", (await Assert.ThrowsAsync<AppException>(() => Docs(b).DeleteAsync(dto.Id))).Code);
        Assert.Equal(0, (await Docs(b).ListAsync(new DocumentListQuery())).Total);
        Assert.True(_storage.Objects.ContainsKey(DocumentKeys.KeyFor(a, dto.Id)));
    }

    [Fact]
    public async Task Delete_removes_the_row_and_the_file()
    {
        var bid = await NewBusinessAsync();
        var dto = await Upload(bid);
        await Docs(bid).DeleteAsync(dto.Id);
        Assert.False(_storage.Objects.ContainsKey(DocumentKeys.KeyFor(bid, dto.Id)));
        Assert.Equal("document_not_found", (await Assert.ThrowsAsync<AppException>(() => Docs(bid).GetAsync(dto.Id))).Code);
    }

    [Fact]
    public async Task The_orphan_cleaner_deletes_orphaned_document_files_but_keeps_live_ones()
    {
        var bid = await NewBusinessAsync();
        var live = await Upload(bid);
        var liveKey = DocumentKeys.KeyFor(bid, live.Id);
        _storage.Modified[liveKey] = DateTime.UtcNow.AddDays(-30);
        var orphan = DocumentKeys.KeyFor(bid, Guid.NewGuid());
        _storage.Objects[orphan] = new byte[] { 1 }; _storage.Modified[orphan] = DateTime.UtcNow.AddDays(-3);

        var r = await new OrphanFileCleaner(fx.Create(null), _storage, NullLogger<OrphanFileCleaner>.Instance).RunAsync(TimeSpan.FromHours(24), 100);

        Assert.True(r.Deleted >= 1);
        Assert.False(_storage.Objects.ContainsKey(orphan));
        Assert.True(_storage.Objects.ContainsKey(liveKey));
    }
}
