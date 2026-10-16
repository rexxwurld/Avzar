using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Tailoring.Api.Hubs;
using Tailoring.Application.Abstractions;
using Tailoring.Api.Security;
using Tailoring.Application.Common;
using Tailoring.Application.Orders;
using Tailoring.Application.Production;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/workflows")]
public class WorkflowsController(IWorkflowService workflows) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TasksView)]
    public async Task<IReadOnlyList<WorkflowDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => await workflows.ListAsync(includeInactive, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TasksView)]
    public async Task<WorkflowDto> Get(Guid id, CancellationToken ct) => await workflows.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.WorkflowsManage)]
    public async Task<ActionResult<WorkflowDto>> Create(UpsertWorkflowRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await workflows.CreateAsync(request, ct));

    [HttpPut("{id:guid}"), HasPermission(Permissions.WorkflowsManage)]
    public async Task<WorkflowDto> Update(Guid id, UpsertWorkflowRequest request, CancellationToken ct)
        => await workflows.UpdateAsync(id, request, ct);
}

/// <summary>Manager view of production tasks.</summary>
[ApiController, Route("api/tasks")]
public class TasksController(ITaskService tasks, IShopScheduleService schedules) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TasksView)]
    public async Task<PagedResult<TaskSummaryDto>> List([FromQuery] TaskListQuery query, CancellationToken ct)
        => await tasks.ListAsync(query, ct);

    /// <summary>Calculates a shop-wide plan without writing planned times to production tasks.</summary>
    [HttpPost("schedule/preview"), HasPermission(Permissions.TasksView)]
    public async Task<ShopSchedulePreviewDto> PreviewSchedule(ShopSchedulePreviewRequest request, CancellationToken ct)
        => await schedules.PreviewAsync(request, ct);

    /// <summary>Applies a complete, freshly revalidated schedule. Partial plans are rejected to avoid leaving stale planned times behind.</summary>
    [HttpPost("schedule/apply"), HasPermission(Permissions.TasksManage)]
    public async Task<ShopScheduleApplyDto> ApplySchedule(ShopScheduleApplyRequest request, CancellationToken ct)
        => await schedules.ApplyAsync(request, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TasksView)]
    public async Task<TaskDetailDto> Get(Guid id, CancellationToken ct) => await tasks.GetAsync(id, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.TasksManage)]
    public async Task<TaskDetailDto> Update(Guid id, UpdateTaskRequest request, CancellationToken ct)
        => await tasks.UpdateAsync(id, request, ct);

    /// <summary>409 "assignment_not_allowed" when the worker lacks the skill or is not at the order's workshop; override needs tasks.override plus a reason.</summary>
    [HttpPost("{id:guid}/assign"), HasPermission(Permissions.TasksAssign)]
    public async Task<TaskDetailDto> Assign(Guid id, AssignTaskRequest request, CancellationToken ct)
        => await tasks.AssignAsync(id, request, ct);

    [HttpPost("{id:guid}/unblock"), HasPermission(Permissions.TasksManage)]
    public async Task<TaskDetailDto> Unblock(Guid id, NoteRequest request, CancellationToken ct)
        => await tasks.UnblockAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.TasksManage)]
    public async Task<TaskDetailDto> Cancel(Guid id, CancelTaskRequest request, CancellationToken ct)
        => await tasks.CancelAsync(id, request, ct);

    /// <summary>Explicit, audited bypass of workflow rules (dependencies / checklist). Needs a written reason.</summary>
    [HttpPost("{id:guid}/override"), HasPermission(Permissions.TasksOverride)]
    public async Task<TaskDetailDto> Override(Guid id, OverrideTaskRequest request, CancellationToken ct)
        => await tasks.OverrideAsync(id, request, ct);
}

[ApiController]
public class OrderProductionController(ITaskService tasks, IOrderService orders) : ControllerBase
{
    [HttpPost("api/orders/{orderId:guid}/tasks"), HasPermission(Permissions.TasksManage)]
    public async Task<ActionResult<TaskDetailDto>> AddTask(Guid orderId, CreateTaskRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await tasks.CreateAsync(orderId, request, ct));

    /// <summary>Normally automatic on confirm. Use this to plan garments that had no workflow, or to choose workflows per garment.</summary>
    [HttpPost("api/orders/{orderId:guid}/generate-tasks"), HasPermission(Permissions.TasksManage)]
    public async Task<GenerateTasksResult> Generate(Guid orderId, GenerateTasksRequest request, CancellationToken ct)
        => await tasks.GenerateForOrderAsync(orderId, request, ct);

    [HttpPost("api/orders/{orderId:guid}/deliver"), HasPermission(Permissions.OrdersManage)]
    public async Task<OrderDto> Deliver(Guid orderId, CancellationToken ct) => await orders.DeliverAsync(orderId, ct);

    [HttpPost("api/order-garments/{garmentId:guid}/request-alterations"), HasPermission(Permissions.TasksManage)]
    public async Task<IReadOnlyList<TaskDetailDto>> RequestAlterations(Guid garmentId, RequestAlterationsRequest request, CancellationToken ct)
        => await tasks.RequestAlterationsAsync(garmentId, request, ct);
}

/// <summary>What a signed-in worker can see and do. Always scoped to tasks assigned to the caller.</summary>
[ApiController, Route("api/my/tasks")]
public class MyTasksController(IMyTaskService mine, IHubContext<BusinessEventsHub> hub, ITenantContext tenant) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TasksWork)]
    public async Task<PagedResult<TaskSummaryDto>> List([FromQuery] MyTaskListQuery query, CancellationToken ct)
        => await mine.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TasksWork)]
    public async Task<TaskDetailDto> Get(Guid id, CancellationToken ct) => await mine.GetAsync(id, ct);

    [HttpPost("{id:guid}/start"), HasPermission(Permissions.TasksWork)]
    public async Task<TaskDetailDto> Start(Guid id, CancellationToken ct) => await Publish(await mine.StartAsync(id, ct), ct);

    [HttpPost("{id:guid}/pause"), HasPermission(Permissions.TasksWork)]
    public async Task<TaskDetailDto> Pause(Guid id, NoteRequest request, CancellationToken ct) => await Publish(await mine.PauseAsync(id, request, ct), ct);

    [HttpPost("{id:guid}/resume"), HasPermission(Permissions.TasksWork)]
    public async Task<TaskDetailDto> Resume(Guid id, CancellationToken ct) => await Publish(await mine.ResumeAsync(id, ct), ct);

    [HttpPost("{id:guid}/complete"), HasPermission(Permissions.TasksWork)]
    public async Task<TaskDetailDto> Complete(Guid id, CompleteTaskRequest request, CancellationToken ct)
        => await Publish(await mine.CompleteAsync(id, request, ct), ct);

    /// <summary>Missing materials, defect, clarification or other. Blocks the task and alerts managers (alerts arrive in Phase 5).</summary>
    [HttpPost("{id:guid}/issues"), HasPermission(Permissions.TasksWork)]
    public async Task<TaskDetailDto> ReportIssue(Guid id, ReportIssueRequest request, CancellationToken ct)
        => await Publish(await mine.ReportIssueAsync(id, request, ct), ct);

    private async Task<TaskDetailDto> Publish(TaskDetailDto detail, CancellationToken ct)
    {
        if (tenant.BusinessId is { } businessId)
            await hub.Clients.Group("business:" + businessId.ToString("N")).SendAsync("task.updated", new { taskId = detail.Task.Id, detail.Task.Status, detail.Task.AssigneeMembershipId, detail.Task.DeadlineUtc }, ct);
        return detail;
    }
}

[ApiController, Route("api/workers")]
public class WorkersController(IWorkerService workers) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TasksView)]
    public async Task<IReadOnlyList<WorkerDto>> List(CancellationToken ct) => await workers.ListAsync(ct);

    [HttpPut("{membershipId:guid}/skills"), HasPermission(Permissions.WorkersManage)]
    public async Task<WorkerDto> SetSkills(Guid membershipId, SetSkillsRequest request, CancellationToken ct)
        => await workers.SetSkillsAsync(membershipId, request, ct);
}


/// <summary>Recurring availability, approved leave and personal calendar for one active worker in the current business.</summary>
[ApiController, Route("api/workers/{membershipId:guid}/schedule")]
public sealed class WorkerScheduleController(IWorkerScheduleService schedules) : ControllerBase
{
    [HttpGet("availability"), HasPermission(Permissions.TasksView)]
    public Task<WorkerAvailabilityDto> GetAvailability(Guid membershipId, CancellationToken ct)
        => schedules.GetAvailabilityAsync(membershipId, ct);

    [HttpPut("availability"), HasPermission(Permissions.WorkersManage)]
    public Task<WorkerAvailabilityDto> SetAvailability(Guid membershipId, SetWorkerAvailabilityRequest request, CancellationToken ct)
        => schedules.SetAvailabilityAsync(membershipId, request.Days, ct);

    [HttpGet("leave"), HasPermission(Permissions.TasksView)]
    public Task<IReadOnlyList<WorkerLeaveDto>> ListLeave(Guid membershipId, [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc, CancellationToken ct)
        => schedules.ListLeaveAsync(membershipId, fromUtc, toUtc, ct);

    [HttpPost("leave"), HasPermission(Permissions.WorkersManage)]
    public async Task<ActionResult<WorkerLeaveDto>> AddLeave(Guid membershipId, WorkerLeaveCreateRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await schedules.AddLeaveAsync(membershipId, request, ct));

    [HttpDelete("leave/{leaveId:guid}"), HasPermission(Permissions.WorkersManage)]
    public async Task<IActionResult> CancelLeave(Guid membershipId, Guid leaveId, CancellationToken ct)
    {
        await schedules.CancelLeaveAsync(membershipId, leaveId, ct);
        return NoContent();
    }

    [HttpGet("calendar"), HasPermission(Permissions.TasksView)]
    public Task<IReadOnlyList<WorkerCalendarItemDto>> Calendar(Guid membershipId, [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc, CancellationToken ct)
        => schedules.GetCalendarAsync(membershipId, fromUtc, toUtc, ct);
}
