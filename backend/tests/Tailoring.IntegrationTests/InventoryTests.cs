using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Inventory;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Tailoring.Infrastructure.Persistence;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class InventoryTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record Seed(Guid BusinessId, Guid LocationId, Guid TaskId, Guid MaterialId);

    private async Task<Seed> SeedAsync()
    {
        await using var db = fx.Create(null);
        var plan = await db.Plans.FirstAsync(x => x.Code == "professional");
        var business = new Business { Name = "Inventory-" + Guid.NewGuid().ToString("N")[..8], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(business);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = business.Id, PlanId = plan.Id });
        var location = new BusinessLocation { BusinessId = business.Id, Name = "Main workshop" };
        var customer = new Customer { BusinessId = business.Id, FullName = "Inventory test customer" };
        db.Locations.Add(location);
        db.Customers.Add(customer);

        var order = new Order
        {
            BusinessId = business.Id, LocationId = location.Id, CustomerId = customer.Id,
            OrderNumber = "INV-STOCK-" + Guid.NewGuid().ToString("N")[..8], Status = OrderStatus.InProduction,
            PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), CurrencyCode = "NGN", Total = 1000m, Subtotal = 1000m
        };
        var garment = new OrderGarment { BusinessId = business.Id, OrderId = order.Id, LineNumber = 1, Name = "Test garment", Quantity = 1, UnitPrice = 1000m, LineTotal = 1000m };
        order.Garments.Add(garment);
        db.Orders.Add(order);
        var task = new ProductionTask { BusinessId = business.Id, OrderId = order.Id, OrderGarmentId = garment.Id, Title = "Cut fabric", StageName = "Cutting", Status = ProductionTaskStatus.Pending };
        var material = new MaterialStock { BusinessId = business.Id, LocationId = location.Id, Sku = "FABRIC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Name = "Cotton fabric", Unit = "m", OnHand = 10m, Reserved = 0m, ReorderLevel = 1m };
        db.ProductionTasks.Add(task);
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        return new Seed(business.Id, location.Id, task.Id, material.Id);
    }

    private static InventoryService Service(AppDbContext db, Guid businessId)
        => new(db, new FakeTenant(businessId), new AuditLogger(db), new EntitlementService(db), new UnrestrictedLocationScope());

    [Fact]
    public async Task Task_stock_release_restores_availability_without_consuming_on_hand()
    {
        var seed = await SeedAsync();
        await using var db = fx.Create(seed.BusinessId);
        var service = Service(db, seed.BusinessId);
        var request = new TaskMaterialBatchRequest([new TaskMaterialQuantityRequest(seed.MaterialId, 4m)], "Cutting allocation");

        var reserved = await service.ReserveTaskAsync(seed.TaskId, request, CancellationToken.None);
        Assert.Equal(10m, reserved.Single().OnHand);
        Assert.Equal(4m, reserved.Single().Reserved);
        Assert.Equal(6m, reserved.Single().Available);

        var released = await service.ReleaseTaskAsync(seed.TaskId,
            new TaskMaterialBatchRequest([new TaskMaterialQuantityRequest(seed.MaterialId, 2m)], "Unused fabric returned"), CancellationToken.None);
        Assert.Equal(10m, released.Single().OnHand);
        Assert.Equal(2m, released.Single().Reserved);
        Assert.Equal(8m, released.Single().Available);

        var consumed = await service.ConsumeTaskAsync(seed.TaskId,
            new TaskMaterialBatchRequest([new TaskMaterialQuantityRequest(seed.MaterialId, 2m)], "Fabric used"), CancellationToken.None);
        Assert.Equal(8m, consumed.Single().OnHand);
        Assert.Equal(0m, consumed.Single().Reserved);
        Assert.Equal(8m, consumed.Single().Available);

        var movements = await db.StockMovements.AsNoTracking().Where(x => x.TaskId == seed.TaskId && x.MaterialId == seed.MaterialId).ToListAsync();
        var byKind = movements.ToDictionary(x => x.Kind);
        Assert.Equal(3, byKind.Count);
        Assert.Equal((10m, 4m), (byKind[StockMovementKind.Reserve].OnHandAfter, byKind[StockMovementKind.Reserve].ReservedAfter));
        Assert.Equal((10m, 2m), (byKind[StockMovementKind.Release].OnHandAfter, byKind[StockMovementKind.Release].ReservedAfter));
        Assert.Equal((8m, 0m), (byKind[StockMovementKind.Consume].OnHandAfter, byKind[StockMovementKind.Consume].ReservedAfter));
    }

    [Fact]
    public async Task Task_stock_cannot_release_or_consume_more_than_its_reservation()
    {
        var seed = await SeedAsync();
        await using var db = fx.Create(seed.BusinessId);
        var service = Service(db, seed.BusinessId);
        await service.ReserveTaskAsync(seed.TaskId,
            new TaskMaterialBatchRequest([new TaskMaterialQuantityRequest(seed.MaterialId, 3m)], null), CancellationToken.None);

        var release = await Assert.ThrowsAsync<Tailoring.Application.Common.AppException>(() => service.ReleaseTaskAsync(seed.TaskId,
            new TaskMaterialBatchRequest([new TaskMaterialQuantityRequest(seed.MaterialId, 4m)], null), CancellationToken.None));
        Assert.Equal("reservation_exceeded", release.Code);
        var consume = await Assert.ThrowsAsync<Tailoring.Application.Common.AppException>(() => service.ConsumeTaskAsync(seed.TaskId,
            new TaskMaterialBatchRequest([new TaskMaterialQuantityRequest(seed.MaterialId, 4m)], null), CancellationToken.None));
        Assert.Equal("reservation_exceeded", consume.Code);

        var material = await db.Materials.AsNoTracking().SingleAsync(x => x.Id == seed.MaterialId);
        Assert.Equal(10m, material.OnHand);
        Assert.Equal(3m, material.Reserved);
    }
}
