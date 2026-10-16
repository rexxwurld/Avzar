using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.ImportExport;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Tailoring.Application.Abstractions;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure: no database.</summary>
public sealed class CsvRulesTests
{
    [Fact]
    public void Reader_handles_quotes_commas_line_breaks_bom_and_blank_lines()
    {
        var rows = CsvReader.Parse("\uFEFFa,b,c\r\n\"x, y\",\"he said \"\"hi\"\"\",\"line1\nline2\"\r\n\r\n1,2,3");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal(new[] { "x, y", "he said \"hi\"", "line1\nline2" }, rows[1]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[2]);
    }

    [Fact]
    public void Reader_rejects_an_unclosed_quote_and_too_many_rows()
    {
        Assert.Equal("csv_malformed", Assert.Throws<AppException>(() => CsvReader.Parse("a,b\n\"oops,1")).Code);
        Assert.Equal("too_many_rows", Assert.Throws<AppException>(() => CsvReader.Parse("a\n1\n2\n3", maxRows: 2)).Code);
    }

    [Theory]
    [InlineData("name\nAda\"Obi", "csv_malformed")]
    [InlineData("name\n\"Ada\"tail", "csv_malformed")]
    [InlineData("name\n\"Ada\" tail", "csv_malformed")]
    public void Reader_rejects_quotes_that_break_csv_field_boundaries(string csv, string code)
        => Assert.Equal(code, Assert.Throws<AppException>(() => CsvReader.Parse(csv)).Code);

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+2348031234567", "'+2348031234567")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    public void Writer_defuses_formulas_and_quotes_when_needed(string input, string expected)
        => Assert.Equal(expected, CsvWriter.Cell(input));

    [Fact]
    public void Exported_phone_numbers_survive_a_round_trip()
    {
        var line = CsvWriter.Row("Ada Obi", "ada@example.com", "+2348031234567");
        var parsed = CustomerCsv.Parse("Full name,Email,Phone\r\n" + line);
        Assert.Equal("+2348031234567", parsed.Rows.Single().Request!.Phone);
    }

    [Fact]
    public void Headers_are_matched_by_alias_and_unknown_columns_are_reported()
    {
        var p = CustomerCsv.Parse("Name,E-mail,Mobile,Favourite colour\nAda Obi,ADA@Example.com, 0803 123 4567 ,red");
        var r = p.Rows.Single();
        Assert.Null(r.Error);
        Assert.Equal("ada@example.com", r.NormalizedEmail);
        Assert.Equal("08031234567", r.NormalizedPhone);
        Assert.Equal(new[] { "Favourite colour" }, p.UnknownColumns);
    }

    [Theory]
    [InlineData("Full name,Email\nA,a@example.com", "Full name must be 2-120 characters.")]
    [InlineData("Full name,Email\nAda Obi,not-an-email", "Email is not valid.")]
    [InlineData("Full name,Phone\nAda Obi,abc", "Phone must contain digits and be at most 40 characters.")]
    public void Invalid_rows_get_a_specific_message(string csv, string message)
        => Assert.Equal(message, CustomerCsv.Parse(csv).Rows.Single().Error);

    [Fact]
    public void A_file_without_a_name_column_is_refused()
        => Assert.Equal("csv_missing_name_column", Assert.Throws<AppException>(() => CustomerCsv.Parse("Email\na@example.com")).Code);
}

public sealed class CustomerImportTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> BusinessAsync(string? customerLimit = null)
    {
        await using var db = fx.Create(null);
        var plan = await db.Plans.FirstAsync(p => p.Code == "free");
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = plan.Id });
        if (customerLimit is not null)
            db.EntitlementOverrides.Add(new TenantEntitlementOverride { BusinessId = b.Id, Key = EntitlementKeys.CustomersActiveMax, Value = customerLimit, Reason = "test" });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private ICustomerImportService Import(Guid bid, IPermissionService? perms = null)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, Guid.NewGuid());
        var audit = new AuditLogger(db);
        return new CustomerImportService(db, new CustomerService(db, t, audit, new EntitlementService(db)), t, audit);
    }

    private static CsvImportCommitOptions Opts(string hash, string onDup = "skip", int? confirm = null, bool skipInvalid = false) => new(hash, onDup, confirm, skipInvalid);

    private async Task<int> CountAsync(Guid bid) { await using var db = fx.Create(bid); return await db.Customers.CountAsync(); }

    [Fact]
    public async Task Preview_changes_nothing_and_classifies_every_row()
    {
        var bid = await BusinessAsync("100");
        await using (var db = fx.Create(bid))
        {
            db.Customers.Add(new Customer { BusinessId = bid, FullName = "Existing One", Email = "old@example.com", NormalizedEmail = "old@example.com" });
            await db.SaveChangesAsync();
        }
        const string csv = "Full name,Email,Phone\nNew Person,new@example.com,\nExisting Renamed,OLD@example.com,\nDup Phone,a@example.com,0803 111 2222\nDup Phone Again,b@example.com,08031112222\nX,bad,\n";
        var p = await Import(bid).PreviewAsync(csv);

        Assert.Equal((5, 2, 1, 1, 1), (p.TotalRows, p.NewCount, p.ExistingCount, p.DuplicateInFileCount, p.InvalidCount));
        Assert.Equal(1, await CountAsync(bid));
        Assert.Contains(p.Problems, x => x.Outcome == "duplicate" && x.ExistingCustomerId is not null);
    }

    [Fact]
    public async Task Commit_is_create_only_by_default_and_skips_existing_customers()
    {
        var bid = await BusinessAsync("100");
        await Import(bid).CommitAsync("Full name,Email\nAda Obi,ada@example.com\n", Opts((await Import(bid).PreviewAsync("Full name,Email\nAda Obi,ada@example.com\n")).FileHash));

        const string csv = "Full name,Email\nAda Changed,ada@example.com\nBayo Ade,bayo@example.com\n";
        var hash = (await Import(bid).PreviewAsync(csv)).FileHash;
        var r = await Import(bid).CommitAsync(csv, Opts(hash));

        Assert.Equal((1, 0, 1), (r.Created, r.Updated, r.Skipped));
        await using var db = fx.Create(bid);
        Assert.Equal("Ada Obi", (await db.Customers.SingleAsync(c => c.Email == "ada@example.com")).FullName);   // untouched
    }

    [Fact]
    public async Task Overwriting_existing_customers_needs_the_exact_confirmation_count()
    {
        var bid = await BusinessAsync("100");
        const string first = "Full name,Email,City\nAda Obi,ada@example.com,Lagos\n";
        await Import(bid).CommitAsync(first, Opts((await Import(bid).PreviewAsync(first)).FileHash));

        const string csv = "Full name,Email,City\nAda Obi-Eze,ada@example.com,\n";
        var hash = (await Import(bid).PreviewAsync(csv)).FileHash;

        var ex = await Assert.ThrowsAsync<AppException>(() => Import(bid).CommitAsync(csv, Opts(hash, "update")));
        Assert.Equal("confirmation_required", ex.Code);
        Assert.Equal(1, ex.Extensions!["updateCount"]);
        await Assert.ThrowsAsync<AppException>(() => Import(bid).CommitAsync(csv, Opts(hash, "update", confirm: 5)));

        var r = await Import(bid).CommitAsync(csv, Opts(hash, "update", confirm: 1));
        Assert.Equal(1, r.Updated);
        await using var db = fx.Create(bid);
        var c = await db.Customers.SingleAsync();
        Assert.Equal("Ada Obi-Eze", c.FullName);
        Assert.Equal("Lagos", c.City);   // a blank cell never erases existing data
    }

    [Fact]
    public async Task A_file_that_differs_from_the_previewed_one_is_refused()
    {
        var bid = await BusinessAsync("100");
        var hash = (await Import(bid).PreviewAsync("Full name\nAda Obi\n")).FileHash;
        var ex = await Assert.ThrowsAsync<AppException>(() => Import(bid).CommitAsync("Full name\nSomeone Else\n", Opts(hash)));
        Assert.Equal("file_changed", ex.Code);
        Assert.Equal(0, await CountAsync(bid));
    }

    [Fact]
    public async Task Invalid_rows_block_the_import_unless_the_user_chooses_to_skip_them()
    {
        var bid = await BusinessAsync("100");
        const string csv = "Full name,Email\nAda Obi,ada@example.com\nB,nope\n";
        var hash = (await Import(bid).PreviewAsync(csv)).FileHash;

        Assert.Equal("import_has_errors", (await Assert.ThrowsAsync<AppException>(() => Import(bid).CommitAsync(csv, Opts(hash)))).Code);
        Assert.Equal(0, await CountAsync(bid));

        var r = await Import(bid).CommitAsync(csv, Opts(hash, skipInvalid: true));
        Assert.Equal((1, 1), (r.Created, r.Skipped));
    }

    [Fact]
    public async Task The_plan_limit_stops_the_import_cleanly_and_reports_what_was_not_imported()
    {
        var bid = await BusinessAsync("2");
        const string csv = "Full name\nAnn One\nBen Two\nCal Three\nDee Four\n";
        var hash = (await Import(bid).PreviewAsync(csv)).FileHash;
        var r = await Import(bid).CommitAsync(csv, Opts(hash));

        Assert.Equal(2, r.Created);
        Assert.True(r.StoppedEarly);
        Assert.Equal(1, r.Failed);
        Assert.Equal(2, await CountAsync(bid));
        Assert.Contains(r.Problems, p => p.Outcome == "not_imported");
    }

    [Fact]
    public async Task Import_and_export_only_touch_the_callers_own_business_and_export_defuses_formulas()
    {
        var a = await BusinessAsync("100");
        var b = await BusinessAsync("100");
        await using (var db = fx.Create(b)) { db.Customers.Add(new Customer { BusinessId = b, FullName = "Other Tenant" }); await db.SaveChangesAsync(); }
        await using (var db = fx.Create(a)) { db.Customers.Add(new Customer { BusinessId = a, FullName = "=cmd|calc", Phone = "+2348031234567", NormalizedPhone = "2348031234567" }); await db.SaveChangesAsync(); }

        var csv = await Import(a).ExportAsync();
        Assert.Contains("'=cmd|calc", csv);
        Assert.Contains("'+2348031234567", csv);
        Assert.DoesNotContain("Other Tenant", csv);

        // an email that exists only in the other business is NOT a duplicate here
        await using (var db = fx.Create(b)) { db.Customers.Add(new Customer { BusinessId = b, FullName = "Dup Email", Email = "same@example.com", NormalizedEmail = "same@example.com" }); await db.SaveChangesAsync(); }
        var p = await Import(a).PreviewAsync("Full name,Email\nMine,same@example.com\n");
        Assert.Equal(1, p.NewCount);
    }
}

public sealed class SearchTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private SearchService Search(Guid bid, IPermissionService perms)
        => new(fx.Create(bid), new FakeUserTenant(bid, Guid.NewGuid()), perms);

    private async Task<Guid> SeedAsync(string name)
    {
        await using var db = fx.Create(null);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Customers.Add(new Customer { BusinessId = b.Id, FullName = name, Phone = "0803 555 1234", NormalizedPhone = "08035551234", Email = "find.me@example.com", NormalizedEmail = "find.me@example.com" });
        await db.SaveChangesAsync();
        return b.Id;
    }

    [Fact]
    public async Task Finds_customers_by_name_email_or_phone_and_treats_wildcards_literally()
    {
        var bid = await SeedAsync("Folasade Bankole");
        var s = Search(bid, new AllowAllPermissions());
        Assert.Single((await s.SearchAsync("folasade")).Customers);
        Assert.Single((await s.SearchAsync("find.me@")).Customers);
        Assert.Single((await s.SearchAsync("555 1234")).Customers);
        Assert.Empty((await s.SearchAsync("%%")).Customers);
        Assert.Empty((await s.SearchAsync("fo_asade")).Customers);
    }

    [Fact]
    public async Task Kinds_the_role_cannot_view_come_back_empty()
    {
        var bid = await SeedAsync("Hidden Person");
        var r = await Search(bid, new FixedPermissions(Permissions.DesignsView)).SearchAsync("hidden");
        Assert.Empty(r.Customers);
    }

    [Fact]
    public async Task Never_returns_another_businesss_records()
    {
        var a = await SeedAsync("Shared Name Alpha");
        var b = await SeedAsync("Shared Name Beta");
        var r = await Search(a, new AllowAllPermissions()).SearchAsync("shared name");
        Assert.Equal("Shared Name Alpha", Assert.Single(r.Customers).Title);
        Assert.NotEqual(Guid.Empty, b);
    }

    [Fact]
    public async Task Short_or_missing_queries_are_refused()
    {
        var bid = await SeedAsync("Whoever");
        Assert.Equal("query_too_short", (await Assert.ThrowsAsync<AppException>(() => Search(bid, new AllowAllPermissions()).SearchAsync("a"))).Code);
    }
}
