using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Audit;
using Tailoring.Domain.Access;
using Tailoring.Domain.Audit;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class AuditReadTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> NewBusinessAsync()
    {
        await using var db = fx.Create(null);
        var b = new Business { Name = "Audit-" + Guid.NewGuid().ToString("N")[..8] };
        db.Businesses.Add(b);
        await db.SaveChangesAsync();
        return b.Id;
    }

    private async Task<Guid> NewUserAsync(string name)
    {
        await using var db = fx.Create(null);
        var email = $"{name}-{Guid.NewGuid():N}@test.local";
        var u = new AppUser { UserName = email, Email = email, FullName = name };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task AddAsync(Guid businessId, Guid? actor, string action, string entityType, Guid? entityId = null, DateTime? at = null)
    {
        await using var db = fx.Create(null); // no tenant in scope: BusinessId is explicit
        db.AuditLogs.Add(new AuditLog
        {
            BusinessId = businessId, ActorUserId = actor, Action = action, EntityType = entityType,
            EntityId = entityId, CreatedAtUtc = at ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private AuditReadService Svc(Guid businessId) => new(fx.Create(businessId));

    [Fact]
    public async Task Only_the_callers_business_entries_are_returned()
    {
        var a = await NewBusinessAsync();
        var b = await NewBusinessAsync();
        await AddAsync(a, null, "order.create", "Order");
        await AddAsync(b, null, "order.create", "Order");
        await AddAsync(b, null, "invoice.issue", "Invoice");

        var forA = await Svc(a).ListAsync(new AuditListQuery());
        Assert.Equal(1, forA.TotalCount);
        var forB = await Svc(b).ListAsync(new AuditListQuery());
        Assert.Equal(2, forB.TotalCount);
    }

    [Fact]
    public async Task No_tenant_in_scope_returns_nothing()
    {
        var a = await NewBusinessAsync();
        await AddAsync(a, null, "order.create", "Order");
        var none = await new AuditReadService(fx.Create(null)).ListAsync(new AuditListQuery());
        Assert.Equal(0, none.TotalCount);
    }

    [Fact]
    public async Task Newest_first_with_paging_and_actor_name()
    {
        var biz = await NewBusinessAsync();
        var actor = await NewUserAsync("Ada Owner");
        var t = DateTime.UtcNow.AddHours(-3);
        await AddAsync(biz, actor, "first", "Order", at: t);
        await AddAsync(biz, actor, "second", "Order", at: t.AddHours(1));
        await AddAsync(biz, null, "third", "Order", at: t.AddHours(2));

        var page1 = await Svc(biz).ListAsync(new AuditListQuery { Page = 1, PageSize = 2 });
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(new[] { "third", "second" }, page1.Items.Select(i => i.Action).ToArray());
        Assert.Null(page1.Items[0].ActorName);
        Assert.Equal("Ada Owner", page1.Items[1].ActorName);

        var page2 = await Svc(biz).ListAsync(new AuditListQuery { Page = 2, PageSize = 2 });
        Assert.Single(page2.Items);
        Assert.Equal("first", page2.Items[0].Action);
    }

    [Fact]
    public async Task Filters_narrow_the_result()
    {
        var biz = await NewBusinessAsync();
        var actor = await NewUserAsync("Filter Actor");
        var entity = Guid.NewGuid();
        var t = DateTime.UtcNow.AddDays(-2);
        await AddAsync(biz, actor, "order.create", "Order", entity, t);
        await AddAsync(biz, null, "invoice.issue", "Invoice", null, t.AddDays(1));

        Assert.Equal(1, (await Svc(biz).ListAsync(new AuditListQuery { Action = "order.create" })).TotalCount);
        Assert.Equal(1, (await Svc(biz).ListAsync(new AuditListQuery { EntityType = "Invoice" })).TotalCount);
        Assert.Equal(1, (await Svc(biz).ListAsync(new AuditListQuery { EntityId = entity })).TotalCount);
        Assert.Equal(1, (await Svc(biz).ListAsync(new AuditListQuery { ActorUserId = actor })).TotalCount);
        Assert.Equal(1, (await Svc(biz).ListAsync(new AuditListQuery { FromUtc = t.AddHours(12) })).TotalCount);
        Assert.Equal(1, (await Svc(biz).ListAsync(new AuditListQuery { ToUtc = t.AddHours(12) })).TotalCount);
        Assert.Equal(0, (await Svc(biz).ListAsync(new AuditListQuery { Action = "order.create", EntityType = "Invoice" })).TotalCount);
    }

    [Fact]
    public void Audit_view_is_in_the_permission_catalogue()
        => Assert.Contains(Permissions.AuditView, Permissions.All);
}
