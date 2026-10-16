using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;
using Tailoring.Api.Security;
using Tailoring.Application.Collections;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.Designs;
using Tailoring.Application.Measurements;
using Tailoring.Application.ImportExport;
using Tailoring.Application.Orders;
using Tailoring.Application.Pricing;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/customers")]
public class CustomersController(ICustomerService customers, IMeasurementService measurements) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.CustomersView)]
    public async Task<PagedResult<CustomerSummaryDto>> List([FromQuery] CustomerListQuery query, CancellationToken ct)
        => await customers.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.CustomersView)]
    public async Task<CustomerDto> Get(Guid id, CancellationToken ct) => await customers.GetAsync(id, ct);

    /// <summary>409 with code "possible_duplicate" and a candidates list when the email/phone matches an existing customer.</summary>
    [HttpPost, HasPermission(Permissions.CustomersManage)]
    public async Task<ActionResult<CustomerDto>> Create(UpsertCustomerRequest request, CancellationToken ct)
    {
        var created = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.CustomersManage)]
    public async Task<CustomerDto> Update(Guid id, UpsertCustomerRequest request, CancellationToken ct)
        => await customers.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/status"), HasPermission(Permissions.CustomersManage)]
    public async Task<CustomerDto> SetStatus(Guid id, SetCustomerStatusRequest request, CancellationToken ct)
        => await customers.SetStatusAsync(id, request, ct);

    /// <summary>Merges the source profile INTO the target. The source is kept, marked Merged, and can no longer be edited.</summary>
    [HttpPost("{sourceId:guid}/merge-into/{targetId:guid}"), HasPermission(Permissions.CustomersMerge)]
    public async Task<CustomerDto> Merge(Guid sourceId, Guid targetId, CancellationToken ct)
        => await customers.MergeAsync(sourceId, targetId, ct);

    [HttpGet("{id:guid}/communications"), HasPermission(Permissions.CustomersView)]
    public async Task<IReadOnlyList<CommunicationDto>> Communications(Guid id, CancellationToken ct)
        => await customers.ListCommunicationsAsync(id, ct);

    [HttpPost("{id:guid}/communications"), HasPermission(Permissions.CustomersManage)]
    public async Task<ActionResult<CommunicationDto>> AddCommunication(Guid id, AddCommunicationRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await customers.AddCommunicationAsync(id, request, ct));

    [HttpGet("{id:guid}/measurements"), HasPermission(Permissions.CustomersView)]
    public async Task<IReadOnlyList<MeasurementSetDto>> Measurements(Guid id, CancellationToken ct)
        => await measurements.ListSetsAsync(id, ct);

    [HttpPost("{id:guid}/measurements"), HasPermission(Permissions.MeasurementsRecord)]
    public async Task<ActionResult<MeasurementSetDto>> CreateMeasurements(Guid id, CreateMeasurementSetRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await measurements.CreateSetAsync(id, request, ct));
}

[ApiController, Route("api/measurement-templates")]
public class MeasurementTemplatesController(IMeasurementService measurements) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.CustomersView)]
    public async Task<IReadOnlyList<TemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => await measurements.ListTemplatesAsync(includeInactive, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.CustomersView)]
    public async Task<TemplateDto> Get(Guid id, CancellationToken ct) => await measurements.GetTemplateAsync(id, ct);

    [HttpPost, HasPermission(Permissions.MeasurementTemplates)]
    public async Task<ActionResult<TemplateDto>> Create(UpsertTemplateRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await measurements.CreateTemplateAsync(request, ct));

    [HttpPut("{id:guid}"), HasPermission(Permissions.MeasurementTemplates)]
    public async Task<TemplateDto> Update(Guid id, UpsertTemplateRequest request, CancellationToken ct)
        => await measurements.UpdateTemplateAsync(id, request, ct);
}

[ApiController, Route("api/measurement-sets")]
public class MeasurementSetsController(IMeasurementService measurements) : ControllerBase
{
    [HttpPost("{id:guid}/revisions"), HasPermission(Permissions.MeasurementsRecord)]
    public async Task<MeasurementSetDto> AddRevision(Guid id, AddRevisionRequest request, CancellationToken ct)
        => await measurements.AddRevisionAsync(id, request, ct);

    [HttpGet("{id:guid}/history"), HasPermission(Permissions.CustomersView)]
    public async Task<IReadOnlyList<RevisionDto>> History(Guid id, CancellationToken ct)
        => await measurements.HistoryAsync(id, ct);

    [HttpGet("{id:guid}/compare"), HasPermission(Permissions.CustomersView)]
    public async Task<IReadOnlyList<MeasurementDiffDto>> Compare(Guid id,
        [FromQuery(Name = "from")] int fromRevision, [FromQuery(Name = "to")] int toRevision, CancellationToken ct)
        => await measurements.CompareAsync(id, fromRevision, toRevision, ct);
}

[ApiController, Route("api/orders")]
public class OrdersController(IOrderService orders) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.OrdersView)]
    public async Task<PagedResult<OrderSummaryDto>> List([FromQuery] OrderListQuery query, CancellationToken ct)
        => await orders.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.OrdersView)]
    public async Task<OrderDto> Get(Guid id, CancellationToken ct) => await orders.GetAsync(id, ct);

    /// <summary>Calculator snapshots, costs and margins included. Needs pricing.view.</summary>
    [HttpGet("{id:guid}/pricing"), HasPermission(Permissions.PricingView)]
    public async Task<OrderPricingDto> GetPricing(Guid id, CancellationToken ct) => await orders.GetPricingAsync(id, ct);

    /// <summary>Send an Idempotency-Key header (any unique string per submission). A retry returns the original order with 200.</summary>
    [HttpPost, HasPermission(Permissions.OrdersManage)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var (order, created) = await orders.CreateAsync(request, idempotencyKey, ct);
        return created ? CreatedAtAction(nameof(Get), new { id = order.Id }, order) : Ok(order);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.OrdersManage)]
    public async Task<OrderDto> Update(Guid id, UpdateOrderRequest request, CancellationToken ct)
        => await orders.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/quote"), HasPermission(Permissions.OrdersManage)]
    public async Task<OrderDto> Quote(Guid id, CancellationToken ct) => await orders.QuoteAsync(id, ct);

    [HttpPost("{id:guid}/confirm"), HasPermission(Permissions.OrdersConfirm)]
    public async Task<OrderDto> Confirm(Guid id, CancellationToken ct) => await orders.ConfirmAsync(id, ct);

    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.OrdersManage)]
    public async Task<OrderDto> Cancel(Guid id, CancelOrderRequest request, CancellationToken ct)
        => await orders.CancelAsync(id, request, ct);
}

[ApiController, Route("api/fittings")]
public class FittingsController(IFittingService fittings) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.OrdersView)]
    public async Task<PagedResult<FittingDto>> List([FromQuery] FittingListQuery query, CancellationToken ct)
        => await fittings.ListAsync(query, ct);

    [HttpPost, HasPermission(Permissions.FittingsManage)]
    public async Task<ActionResult<FittingDto>> Create(CreateFittingRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await fittings.CreateAsync(request, ct));

    [HttpPost("{id:guid}/status"), HasPermission(Permissions.FittingsManage)]
    public async Task<FittingDto> SetStatus(Guid id, SetFittingStatusRequest request, CancellationToken ct)
        => await fittings.SetStatusAsync(id, request, ct);
}

[ApiController, Route("api/designs")]
public class DesignsController(IDesignService designs) : ControllerBase
{
    [HttpGet("export"), HasPermission(Permissions.DesignsView)]
    public async Task<IActionResult> Export([FromQuery] DesignListQuery query, CancellationToken ct)
    {
        var rows = new List<DesignSummaryDto>();
        var page = 1;
        var total = 0;
        do
        {
            var result = await designs.ListAsync(query with { Page = page, PageSize = 100 }, ct);
            rows.AddRange(result.Items);
            total = result.TotalCount;
            page++;
            if (result.Items.Count == 0 || rows.Count >= total || rows.Count >= 100_000) break;
        } while (true);

        var csv = new StringBuilder().Append(CsvWriter.Row("Design ID", "Name", "Slug", "Kind", "Category", "Base price", "Currency", "Featured", "Visibility", "Status", "Tags", "Created UTC")).Append("\r\n");
        foreach (var row in rows)
            csv.Append(CsvWriter.Row(row.Id.ToString(), row.Name, row.Slug, row.Kind, row.CategoryName,
                row.BasePrice?.ToString(CultureInfo.InvariantCulture), row.CurrencyCode, row.IsFeatured ? "yes" : "no",
                row.Visibility, row.Status, string.Join("; ", row.Tags), row.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append("\r\n");
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"designs-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet, HasPermission(Permissions.DesignsView)]
    public async Task<PagedResult<DesignSummaryDto>> List([FromQuery] DesignListQuery query, CancellationToken ct)
        => await designs.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.DesignsView)]
    public async Task<DesignDto> Get(Guid id, CancellationToken ct) => await designs.GetAsync(id, ct);

    /// <summary>403 plan_limit_reached when the plan's saved-design limit is hit.</summary>
    [HttpPost, HasPermission(Permissions.DesignsManage)]
    public async Task<ActionResult<DesignDto>> Create(UpsertDesignRequest request, CancellationToken ct)
    {
        var created = await designs.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.DesignsManage)]
    public async Task<DesignDto> Update(Guid id, UpsertDesignRequest request, CancellationToken ct)
        => await designs.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/status"), HasPermission(Permissions.DesignsManage)]
    public async Task<DesignDto> SetStatus(Guid id, SetDesignStatusRequest request, CancellationToken ct)
        => await designs.SetStatusAsync(id, request, ct);

    [HttpPut("{id:guid}/images"), HasPermission(Permissions.DesignsManage)]
    public async Task<DesignDto> SetImages(Guid id, SetDesignImagesRequest request, CancellationToken ct)
        => await designs.SetImagesAsync(id, request, ct);

    [HttpPost("{id:guid}/visibility"), HasPermission(Permissions.DesignsManage)]
    public async Task<DesignDto> SetVisibility(Guid id, SetDesignVisibilityRequest request, CancellationToken ct)
        => await designs.SetVisibilityAsync(id, request, ct);
}

[ApiController, Route("api/design-categories")]
public class DesignCategoriesController(IDesignService designs) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.DesignsView)]
    public async Task<IReadOnlyList<DesignCategoryDto>> List(CancellationToken ct) => await designs.ListCategoriesAsync(ct);

    [HttpPost, HasPermission(Permissions.DesignsManage)]
    public async Task<ActionResult<DesignCategoryDto>> Create(UpsertCategoryRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await designs.CreateCategoryAsync(request, ct));

    [HttpPut("{id:guid}"), HasPermission(Permissions.DesignsManage)]
    public async Task<DesignCategoryDto> Update(Guid id, UpsertCategoryRequest request, CancellationToken ct)
        => await designs.UpdateCategoryAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.DesignsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await designs.DeleteCategoryAsync(id, ct);
        return NoContent();
    }
}

[ApiController, Route("api/collections")]
public class CollectionsController(ICollectionService collections) : ControllerBase
{
    [HttpGet("export"), HasPermission(Permissions.DesignsView)]
    public async Task<IActionResult> Export([FromQuery] CollectionListQuery query, CancellationToken ct)
    {
        var rows = new List<CollectionSummaryDto>();
        var page = 1;
        var total = 0;
        do
        {
            var result = await collections.ListAsync(query with { Page = page, PageSize = 100 }, ct);
            rows.AddRange(result.Items);
            total = result.TotalCount;
            page++;
            if (result.Items.Count == 0 || rows.Count >= total || rows.Count >= 100_000) break;
        } while (true);

        var csv = new StringBuilder().Append(CsvWriter.Row("Collection ID", "Name", "Slug", "Visibility", "Sort order", "Design count", "Public design count")).Append("\r\n");
        foreach (var row in rows)
            csv.Append(CsvWriter.Row(row.Id.ToString(), row.Name, row.Slug, row.Visibility,
                row.SortOrder.ToString(CultureInfo.InvariantCulture), row.DesignCount.ToString(CultureInfo.InvariantCulture),
                row.PublicDesignCount.ToString(CultureInfo.InvariantCulture))).Append("\r\n");
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"collections-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet, HasPermission(Permissions.DesignsView)]
    public async Task<PagedResult<CollectionSummaryDto>> List([FromQuery] CollectionListQuery query, CancellationToken ct)
        => await collections.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.DesignsView)]
    public async Task<CollectionDto> Get(Guid id, CancellationToken ct) => await collections.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.DesignsManage)]
    public async Task<ActionResult<CollectionDto>> Create(UpsertCollectionRequest request, CancellationToken ct)
    {
        var created = await collections.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.DesignsManage)]
    public async Task<CollectionDto> Update(Guid id, UpsertCollectionRequest request, CancellationToken ct)
        => await collections.UpdateAsync(id, request, ct);

    [HttpPut("{id:guid}/items"), HasPermission(Permissions.DesignsManage)]
    public async Task<CollectionDto> SetItems(Guid id, SetCollectionItemsRequest request, CancellationToken ct)
        => await collections.SetItemsAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.DesignsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await collections.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController, Route("api/pricing")]
public class PricingController(IPricingService pricing) : ControllerBase
{
    [HttpGet("profile"), HasPermission(Permissions.PricingView)]
    public async Task<PricingProfileDto> GetProfile(CancellationToken ct) => await pricing.GetProfileAsync(ct);

    [HttpPut("profile"), HasPermission(Permissions.PricingManage)]
    public async Task<PricingProfileDto> SaveProfile(UpsertPricingProfileRequest request, CancellationToken ct)
        => await pricing.UpsertProfileAsync(request, ct);

    /// <summary>Stateless what-if: nothing is saved.</summary>
    [HttpPost("calculate"), HasPermission(Permissions.PricingView)]
    public async Task<PricingCalculationDto> Calculate(PricingInputDto input, CancellationToken ct) => await pricing.CalculateAsync(input, ct);
}
