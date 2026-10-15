using System.IO.Compression;
using System.Text;
using Tailoring.Application.Common;
using Tailoring.Domain.Documents;

namespace Tailoring.Infrastructure.Storage;

public sealed record SniffedDocument(DocumentKind Kind, string ContentType);

/// <summary>
/// Decides what an upload really is from its bytes. Allow-list only: PDF, .docx, .xlsx (no macros), UTF-8 .txt/.csv.
/// Legacy Office files (.doc/.xls) and macro-enabled files are refused. Nothing is extracted or executed.
/// </summary>
public static class DocumentSniffer
{
    private const int MaxZipEntries = 5000;
    private const long MaxZipDeclaredBytes = 200L * 1024 * 1024;
    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static SniffedDocument Inspect(byte[] data, string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var sniffed = Detect(data, ext);
        var allowed = sniffed.Kind switch
        {
            DocumentKind.Pdf => new[] { ".pdf" },
            DocumentKind.Word => new[] { ".docx" },
            DocumentKind.Excel => new[] { ".xlsx" },
            _ => new[] { ".txt", ".csv" },
        };
        if (!allowed.Contains(ext))
            throw AppException.BadRequest("extension_mismatch", $"The file name must end with {string.Join(" or ", allowed)} to match what the file contains.");
        return sniffed;
    }

    private static SniffedDocument Detect(byte[] d, string ext)
    {
        if (d.Length >= 5 && d[0] == '%' && d[1] == 'P' && d[2] == 'D' && d[3] == 'F' && d[4] == '-')
            return new(DocumentKind.Pdf, "application/pdf");

        if (d.Length >= 4 && d[0] == 'P' && d[1] == 'K' && d[2] == 3 && d[3] == 4) return DetectOoxml(d);

        if (d.Length >= 4 && d[0] == 0xD0 && d[1] == 0xCF && d[2] == 0x11 && d[3] == 0xE0)
            throw AppException.BadRequest("unsupported_document", "Old Word/Excel formats (.doc, .xls) are not accepted. Save as .docx or .xlsx.");

        if (ext is ".txt" or ".csv") return DetectText(d, ext);
        throw AppException.BadRequest("unsupported_document", "Only PDF, .docx, .xlsx, .txt and .csv files are accepted.");
    }

    private static SniffedDocument DetectOoxml(byte[] d)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(d, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxZipEntries || zip.Entries.Sum(e => e.Length) > MaxZipDeclaredBytes)
                throw AppException.BadRequest("unsafe_archive", "This file is too large or complex to accept.");

            var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Any(n => n.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                throw AppException.BadRequest("macros_not_allowed", "Files with macros are not accepted.");

            var contentTypes = zip.GetEntry("[Content_Types].xml");
            if (contentTypes is null) throw AppException.BadRequest("unsupported_document", "Only PDF, .docx, .xlsx, .txt and .csv files are accepted.");
            if (contentTypes.Length > 1024 * 1024) throw AppException.BadRequest("unsafe_archive", "This file is too large or complex to accept.");
            using (var reader = new StreamReader(contentTypes.Open()))
                if (reader.ReadToEnd().Contains("macroEnabled", StringComparison.OrdinalIgnoreCase))
                    throw AppException.BadRequest("macros_not_allowed", "Files with macros are not accepted.");

            if (names.Contains("word/document.xml")) return new(DocumentKind.Word, DocxType);
            if (names.Contains("xl/workbook.xml")) return new(DocumentKind.Excel, XlsxType);
            throw AppException.BadRequest("unsupported_document", "Only PDF, .docx, .xlsx, .txt and .csv files are accepted.");
        }
        catch (InvalidDataException)
        {
            throw AppException.BadRequest("unsupported_document", "This file is damaged or not a supported document.");
        }
    }

    private static SniffedDocument DetectText(byte[] d, string ext)
    {
        if (Array.IndexOf(d, (byte)0, 0, Math.Min(d.Length, 8192)) >= 0)
            throw AppException.BadRequest("invalid_text", "This is not a plain text file.");
        try { new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(d); }
        catch (DecoderFallbackException) { throw AppException.BadRequest("invalid_text", "Text files must be UTF-8."); }
        return new(DocumentKind.Text, ext == ".csv" ? "text/csv; charset=utf-8" : "text/plain; charset=utf-8");
    }
}
