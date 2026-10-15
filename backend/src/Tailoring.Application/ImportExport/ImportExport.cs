using System.Text;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.Email;
using Tailoring.Domain.Common;

namespace Tailoring.Application.ImportExport;

/// <summary>RFC 4180 reader: quoted fields, doubled quotes, commas and line breaks inside quotes, CRLF or LF, optional BOM. Blank lines are skipped.</summary>
public static class CsvReader
{
    public static List<string[]> Parse(string text, int maxRows = 100_000)
    {
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        void EndRow()
        {
            row.Add(field.ToString());
            field.Clear();
            if (row.Any(c => c.Trim().Length > 0))
            {
                rows.Add(row.ToArray());
                if (rows.Count > maxRows) throw AppException.BadRequest("too_many_rows", $"The file has more than {maxRows} rows.");
            }
            row.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else inQuotes = false;
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); break;
                case '\n': EndRow(); break;
                default: field.Append(c); break;
            }
        }
        if (inQuotes) throw AppException.BadRequest("csv_malformed", "The file has a quoted value that never closes.");
        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }
}

public static class CsvWriter
{
    /// <summary>
    /// Spreadsheet formula-injection guard: a cell that starts with = + - @ tab or CR gets a leading apostrophe, so Excel and
    /// Sheets show text instead of running it. The importer removes the apostrophe again, so exports re-import unchanged.
    /// </summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value.Replace("\0", "");
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        if (v.IndexOfAny([',', '"', '\r', '\n']) >= 0) v = "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }

    public static string Row(params string?[] cells) => string.Join(',', cells.Select(Cell));
}

public sealed record ParsedCustomerRow(int RowNumber, UpsertCustomerRequest? Request, string? Error, string? NormalizedEmail, string? NormalizedPhone);
public sealed record CustomerCsvParse(IReadOnlyList<ParsedCustomerRow> Rows, IReadOnlyList<string> UnknownColumns);

/// <summary>Pure mapping and validation of customer CSV files. Same limits as UpsertCustomerRequest.</summary>
public static class CustomerCsv
{
    public static readonly string[] ExportHeader = ["Full name", "Email", "Phone", "Address", "City", "Notes", "Garment preferences", "Status", "Created (UTC)"];

    private enum Field { Name, Email, Phone, Address, City, Notes, Preferences }

    private static readonly Dictionary<string, Field> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["full name"] = Field.Name, ["fullname"] = Field.Name, ["name"] = Field.Name, ["customer"] = Field.Name, ["customer name"] = Field.Name,
        ["email"] = Field.Email, ["e-mail"] = Field.Email, ["email address"] = Field.Email,
        ["phone"] = Field.Phone, ["mobile"] = Field.Phone, ["telephone"] = Field.Phone, ["tel"] = Field.Phone, ["phone number"] = Field.Phone,
        ["address"] = Field.Address, ["address line"] = Field.Address,
        ["city"] = Field.City, ["town"] = Field.City,
        ["notes"] = Field.Notes, ["note"] = Field.Notes,
        ["garment preferences"] = Field.Preferences, ["preferences"] = Field.Preferences,
    };

    public static CustomerCsvParse Parse(string text)
    {
        var table = CsvReader.Parse(text);
        if (table.Count == 0) throw AppException.BadRequest("csv_empty", "The file is empty.");

        var map = new Dictionary<int, Field>();
        var unknown = new List<string>();
        for (var i = 0; i < table[0].Length; i++)
        {
            var header = table[0][i].Trim();
            if (header.Length == 0) continue;
            if (Aliases.TryGetValue(header, out var f) && !map.ContainsValue(f)) map[i] = f;
            else unknown.Add(header);
        }
        if (!map.ContainsValue(Field.Name))
            throw AppException.BadRequest("csv_missing_name_column", "The first row must have a \"Full name\" column.");

        var rows = new List<ParsedCustomerRow>();
        for (var r = 1; r < table.Count; r++)
        {
            string? Get(Field f)
            {
                var idx = map.FirstOrDefault(kv => kv.Value == f, new KeyValuePair<int, Field>(-1, f)).Key;
                if (idx < 0 || idx >= table[r].Length) return null;
                var v = Unguard(table[r][idx].Trim());
                return v.Length == 0 ? null : v;
            }
            rows.Add(Validate(r + 1, Get(Field.Name), Get(Field.Email), Get(Field.Phone), Get(Field.Address), Get(Field.City), Get(Field.Notes), Get(Field.Preferences)));
        }
        return new CustomerCsvParse(rows, unknown);
    }

    /// <summary>Undoes the export's formula guard.</summary>
    public static string Unguard(string s) => s.Length > 1 && s[0] == '\'' && "=+-@".Contains(s[1]) ? s[1..] : s;

    private static ParsedCustomerRow Validate(int row, string? name, string? email, string? phone, string? address, string? city, string? notes, string? prefs)
    {
        string? Fail(string m) => m;
        var error =
            name is null || name.Length is < 2 or > 120 ? Fail("Full name must be 2-120 characters.") :
            email is not null && (email.Length > 256 || !EmailAddressRules.TryNormalize(email, out _)) ? Fail("Email is not valid.") :
            phone is not null && (phone.Length > 40 || Normalize.Phone(phone) is null) ? Fail("Phone must contain digits and be at most 40 characters.") :
            address is { Length: > 300 } ? Fail("Address is longer than 300 characters.") :
            city is { Length: > 100 } ? Fail("City is longer than 100 characters.") :
            notes is { Length: > 4000 } ? Fail("Notes are longer than 4000 characters.") :
            prefs is { Length: > 2000 } ? Fail("Garment preferences are longer than 2000 characters.") : null;

        if (error is not null) return new ParsedCustomerRow(row, null, error, null, null);
        var req = new UpsertCustomerRequest { FullName = name!, Email = email, Phone = phone, AddressLine = address, City = city, Notes = notes, GarmentPreferences = prefs };
        return new ParsedCustomerRow(row, req, null, Normalize.Email(email), Normalize.Phone(phone));
    }
}

public sealed record CsvRowResultDto(int Row, string Outcome, string? Message, string? FullName, Guid? ExistingCustomerId);

/// <summary>Outcome values: create, duplicate (matches an existing customer), duplicate_in_file, invalid.</summary>
public sealed record CsvImportPreviewDto(
    string FileHash, int TotalRows, int NewCount, int ExistingCount, int DuplicateInFileCount, int InvalidCount,
    IReadOnlyList<string> UnknownColumns, IReadOnlyList<string> SampleNew, IReadOnlyList<CsvRowResultDto> Problems);

public sealed record CsvImportCommitOptions(string ExpectedHash, string OnDuplicate, int? ConfirmUpdateCount, bool SkipInvalidRows);

public sealed record CsvImportResultDto(int Created, int Updated, int Skipped, int Failed, bool StoppedEarly, IReadOnlyList<CsvRowResultDto> Problems);

public interface ICustomerImportService
{
    Task<string> ExportAsync(CancellationToken ct = default);
    /// <summary>Changes nothing.</summary>
    Task<CsvImportPreviewDto> PreviewAsync(string csvText, CancellationToken ct = default);
    /// <summary>
    /// Default is create-only: matches are skipped. OnDuplicate=update overwrites the matched customer's fields with the file's non-empty
    /// cells and requires ConfirmUpdateCount to equal the number of customers that would be changed, as shown by the preview.
    /// </summary>
    Task<CsvImportResultDto> CommitAsync(string csvText, CsvImportCommitOptions options, CancellationToken ct = default);
}

public sealed record SearchHitDto(string Kind, Guid Id, string Title, string? Subtitle);
public sealed record SearchResultDto(
    IReadOnlyList<SearchHitDto> Customers, IReadOnlyList<SearchHitDto> Orders, IReadOnlyList<SearchHitDto> Invoices,
    IReadOnlyList<SearchHitDto> Designs, IReadOnlyList<SearchHitDto> Inquiries);

public interface ISearchService
{
    /// <summary>Searches only what the caller may view. Kinds the caller cannot see come back empty.</summary>
    Task<SearchResultDto> SearchAsync(string query, int perKindLimit = 5, CancellationToken ct = default);
}
