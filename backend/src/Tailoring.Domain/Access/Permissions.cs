namespace Tailoring.Domain.Access;

/// <summary>
/// Catalogue of permissions. Later phases append to this list (tasks.*, schedule.*, ...).
/// Permissions are strings so businesses can compose custom roles from them.
/// </summary>
public static class Permissions
{
    // Phase 1
    public const string StaffView = "staff.view";
    public const string StaffManage = "staff.manage";
    public const string RolesManage = "roles.manage";
    public const string LocationsManage = "locations.manage";
    /// <summary>Sees orders and fittings at every location. Without it, staff only see the locations they are assigned to (fail closed).</summary>
    public const string LocationsAll = "locations.all";
    public const string AuditView = "audit.view";
    public const string BusinessSettings = "business.settings";

    // Phase 2
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string CustomersMerge = "customers.merge";
    public const string MeasurementTemplates = "measurements.templates";
    public const string MeasurementsRecord = "measurements.record";
    public const string OrdersView = "orders.view";
    public const string OrdersManage = "orders.manage";
    public const string OrdersConfirm = "orders.confirm";
    public const string FittingsManage = "fittings.manage";

    // Phase 3
    public const string TasksView = "tasks.view";
    public const string TasksManage = "tasks.manage";
    public const string TasksAssign = "tasks.assign";
    public const string TasksOverride = "tasks.override";
    public const string TasksWork = "tasks.work";
    public const string WorkflowsManage = "workflows.manage";
    public const string WorkersManage = "workers.manage";

    // Step 2
    public const string DesignsView = "designs.view";
    public const string DesignsManage = "designs.manage";
    public const string InvoicesView = "invoices.view";
    public const string InvoicesManage = "invoices.manage";   // draft, issue, void, credit notes
    public const string PaymentsView = "payments.view";
    public const string PaymentsRecord = "payments.record";   // claim a payment (stays Pending)
    public const string PaymentsVerify = "payments.verify";   // confirm, reject, reverse. The only way money counts.
    public const string ExpensesView = "expenses.view";
    public const string ExpensesManage = "expenses.manage";
    public const string StorefrontView = "storefront.view";
    public const string StorefrontEdit = "storefront.edit";       // draft, theme, branding, templates
    public const string StorefrontPublish = "storefront.publish"; // publish, unpublish, roll back, domains
    public const string InquiriesView = "inquiries.view";
    public const string InquiriesManage = "inquiries.manage";
    public const string PricingView = "pricing.view";     // costs and margins are sensitive
    public const string PricingManage = "pricing.manage";
    public const string MediaView = "media.view";
    public const string MediaManage = "media.manage";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage"; // upload, delete

    public static readonly IReadOnlyList<string> All =
    [
        StaffView, StaffManage, RolesManage, LocationsManage, LocationsAll, AuditView, BusinessSettings,
        CustomersView, CustomersManage, CustomersMerge, MeasurementTemplates, MeasurementsRecord,
        OrdersView, OrdersManage, OrdersConfirm, FittingsManage,
        TasksView, TasksManage, TasksAssign, TasksOverride, TasksWork, WorkflowsManage, WorkersManage,
        DesignsView, DesignsManage, MediaView, MediaManage, DocumentsView, DocumentsManage, PricingView, PricingManage,
        StorefrontView, StorefrontEdit, StorefrontPublish, InquiriesView, InquiriesManage,
        InvoicesView, InvoicesManage, PaymentsView, PaymentsRecord, PaymentsVerify, ExpensesView, ExpensesManage
    ];

    public static bool IsValid(string permission) => All.Contains(permission);
}
