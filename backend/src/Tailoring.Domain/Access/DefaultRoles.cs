namespace Tailoring.Domain.Access;

public static class DefaultRoles
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Tailor = "Tailor";
    public const string Cutter = "Cutter";
    public const string Finisher = "Finisher";
    public const string QualityControl = "Quality Control";
    public const string Accountant = "Accountant";

    /// <summary>
    /// Roles created for every new business. Manager gets everything except business settings and role editing.
    /// Production roles get only tasks.work: they act on tasks assigned to them and never see customer personal data.
    /// </summary>
    public static List<BusinessRole> Create(Guid businessId)
    {
        BusinessRole Make(string name, IEnumerable<string> perms)
        {
            var role = new BusinessRole { BusinessId = businessId, Name = name, IsSystem = true };
            role.Permissions = perms.Select(p => new BusinessRolePermission
            {
                RoleId = role.Id, BusinessId = businessId, Permission = p
            }).ToList();
            return role;
        }

        return
        [
            Make(Owner, Permissions.All),
            Make(Manager, Permissions.All.Where(p => p is not (Permissions.BusinessSettings or Permissions.RolesManage))),
            // Money only: sees orders and customers to bill them, but cannot change them.
            Make(Accountant, [Permissions.OrdersView, Permissions.CustomersView, Permissions.InvoicesView, Permissions.InvoicesManage,
                Permissions.PaymentsView, Permissions.PaymentsRecord, Permissions.PaymentsVerify, Permissions.ExpensesView, Permissions.ExpensesManage, Permissions.ReportsView, Permissions.LocationsAll]),
            Make(Tailor, [Permissions.TasksWork]),
            Make(Cutter, [Permissions.TasksWork]),
            Make(Finisher, [Permissions.TasksWork]),
            Make(QualityControl, [Permissions.TasksWork]),
        ];
    }
}
