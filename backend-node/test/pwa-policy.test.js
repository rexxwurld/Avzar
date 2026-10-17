import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const serviceWorker = await readFile(new URL('../../pwa/sw.js', import.meta.url), 'utf8');
const app = await readFile(new URL('../../pwa/app.js', import.meta.url), 'utf8');
const html = await readFile(new URL('../../pwa/index.html', import.meta.url), 'utf8');

test('service worker bypasses API requests instead of caching private responses', () => {
  assert.match(serviceWorker, /url\.pathname\.startsWith\('\/backend\/'\)/);
  assert.match(serviceWorker, /url\.pathname\.startsWith\('\/api\/'\)/);
  assert.match(serviceWorker, /request\.method !== 'GET'/);
});

test('service worker updates wait for user confirmation', () => {
  assert.doesNotMatch(serviceWorker.split("self.addEventListener('install'")[1].split("self.addEventListener('activate'")[0], /skipWaiting/);
  assert.match(serviceWorker, /event\.data\?\.type === 'SKIP_WAITING'/);
  assert.match(app, /showUpdateNotice/);
  assert.match(html, /A new version of Avzar is ready/);
  assert.match(html, /id="update-button"/);
});

test('measurement comparison uses the existing permission-protected revision API', () => {
  assert.match(app, /parts\[4\] === 'compare'/);
  assert.match(app, /renderMeasurementCompare\(/);
  assert.match(app, /\/api\/measurement-sets\/\$\{encodeURIComponent\(setId\)\}\/compare\?from=\$\{fromRevision\}&to=\$\{toRevision\}/);
  assert.match(app, /Choose two different measurement revisions/);
  assert.match(app, /Compare with current/);
});

test('phase 1 to 5 PWA workflows remain API-backed and do not invent business records', () => {
  for (const pattern of [
    /\/api\/auth\/csrf/,
    /\/api\/auth\/login/,
    /\/api\/auth\/me/,
    /\/api\/customers\?/,
    /\/api\/customers\/\$\{encodeURIComponent\(customerId\)\}\/measurements/,
    /\/api\/orders\?/,
    /\/api\/tasks/,
    /\/api\/workers/,
    /\/api\/staff/,
    /\/api\/tasks\/schedule\/preview/
  ]) assert.match(app, pattern);
  assert.match(app, /does not invent totals/i);
});

test('staff invitation errors render in the visible form and allow retry', () => {
  assert.match(app, /const errorBox = document\.querySelector\('#staff-invite-error'\)/);
  assert.match(app, /errorBox\.innerHTML = `<p class="form-error" role="alert">\$\{escapeHtml\(error\.message \|\| 'Could not create invitation\.'\)\}<\/p>`/);
  assert.match(app, /submitButton\.disabled = false; submitButton\.textContent = 'Create invitation'/);
});

test('business overview is backed by live customer, order and task APIs', () => {
  assert.match(app, /async function renderHomeDashboard/);
  assert.match(app, /\/api\/customers\?page=1&pageSize=1/);
  assert.match(app, /\/api\/orders\?page=1&pageSize=5/);
  assert.match(app, /\/api\/my\/tasks\?page=1&pageSize=10/);
  assert.match(app, /A dash or an error message means that the API did not return a usable count/);
});

test('business settings require business.settings permission and use existing API endpoints', () => {
  assert.match(app, /async function renderBusinessSettings/);
  assert.match(app, /hasPermission\('business\.settings'\)/);
  assert.match(app, /\/api\/business\/schedule\/hours/);
  assert.match(app, /\/api\/business\/schedule\/holidays/);
  assert.match(app, /\/api\/business\/branding/);
  assert.match(app, /closing time must be later than opening time/);
});

test('shop schedule date input is formatted in local calendar time, not UTC date slices', () => {
  assert.match(app, /const startValue = `\$\{start\.getFullYear\(\)\}-\$\{String\(start\.getMonth\(\)\+1\)\.padStart\(2,'0'\)\}-\$\{String\(start\.getDate\(\)\)\.padStart\(2,'0'\)\}`/);
  assert.doesNotMatch(app, /const start = new Date\(\); start\.setHours\(0,0,0,0\); const startValue = start\.toISOString\(\)/);
});

test('account security UI uses existing password, two-factor and session APIs', () => {
  assert.match(app, /async function renderAccountSecurity/);
  for (const endpoint of [
    /\/api\/account\/security/,
    /\/api\/account\/change-password/,
    /\/api\/account\/two-factor\/setup/,
    /\/api\/account\/two-factor\/enable/,
    /\/api\/account\/two-factor\/disable/,
    /\/api\/account\/sessions\/revoke-all/,
    /\/api\/account\/resend-verification/
  ]) assert.match(app, endpoint);
  assert.match(app, /The new password and confirmation do not match/);
  assert.match(app, /Save your recovery codes/);
});

test('customer detail supports communication logs and permission-gated profile merging', () => {
  assert.match(app, /\/api\/customers\/\$\{encodeURIComponent\(customerId\)\}\/communications/);
  assert.match(app, /channel: document\.querySelector\('#communication-channel'\)\.value/);
  assert.match(app, /<option>WhatsApp<\/option>/);
  assert.match(app, /<option>Outbound<\/option><option>Inbound<\/option>/);
  assert.match(app, /hasPermission\('customers\.merge'\)/);
  assert.match(app, /merge-into\/\$\{encodeURIComponent\(target\.id\)\}/);
});

test('two-factor disable distinguishes authenticator codes from one-time recovery codes', () => {
  assert.match(app, /id="disable-2fa-type"/);
  assert.match(app, /method === 'recovery' \? \{ recoveryCode: value \} : \{ code: value \}/);
});

test('fittings screen is wired to existing appointment APIs and validates creation inputs', () => {
  assert.match(app, /async function renderFittings/);
  assert.match(app, /\/api\/fittings\?page=1&pageSize=100/);
  assert.match(app, /\/api\/fittings', \{ method: 'POST'/);
  assert.match(app, /\/api\/fittings\/\$\{encodeURIComponent\(select\.dataset\.fittingStatus\)\}\/status/);
  assert.match(app, /durationMinutes < 5 \|\| durationMinutes > 480/);
});

test('inventory PWA uses permission-gated stock APIs and displays server stock state', () => {
  assert.match(app, /hasPermission\('inventory\.view'\)/);
  assert.match(app, /hasPermission\('inventory\.manage'\)/);
  assert.match(app, /async function renderInventory\(container\)/);
  assert.match(app, /api\('\/api\/inventory'\)/);
  assert.match(app, /\/api\/inventory\/\$\{encodeURIComponent\(button\.dataset\.stockHistory\)\}\/movements/);
  assert.match(app, /\/api\/inventory\/\$\{encodeURIComponent\(button\.dataset\.stockChange\)\}\/\$\{actionName\}/);
  assert.match(app, /Available stock excludes quantities reserved for production tasks/);
  assert.match(app, /Stock figures come from the business API/);
});

test('inventory API enforces view/manage permissions and exposes movement history', async () => {
  const { readFile } = await import('node:fs/promises');
  const controller = await readFile(new URL('../../backend/src/Tailoring.Api/Controllers/InventoryController.cs', import.meta.url), 'utf8');
  assert.match(controller, /\[HttpGet, HasPermission\(Permissions\.InventoryView\)\]/);
  assert.match(controller, /\[HttpPost, HasPermission\(Permissions\.InventoryManage\)\]/);
  assert.match(controller, /\[HttpGet\("\{id:guid\}\/movements"\), HasPermission\(Permissions\.InventoryView\)\]/);
  assert.match(controller, /ReserveTask/);
  assert.match(controller, /ReleaseTask/);
  assert.match(controller, /ConsumeTask/);
});

test('finance PWA is permission-gated and records/verifies payments through authoritative finance APIs', () => {
  assert.match(app, /hasPermission\('invoices\.view'\)/);
  assert.match(app, /hasPermission\('payments\.record'\)/);
  assert.match(app, /hasPermission\('payments\.verify'\)/);
  assert.match(app, /async function renderFinance\(container, invoiceId = null\)/);
  assert.match(app, /\/api\/invoices\?page=1&pageSize=50/);
  assert.match(app, /\/api\/invoices\/\$\{encodeURIComponent\(invoice\.id\)\}\/payments/);
  assert.match(app, /\/api\/payments\/\$\{encodeURIComponent\(button\.dataset\.paymentVerify\)\}\/verify/);
  assert.match(app, /pending claim does not count|pending payment does not count/i);
  assert.match(app, /idempotency-key': crypto\.randomUUID\(\)/);
});

test('expenses PWA separates money-out records, permissions, category lookup, and audited voids', () => {
  assert.match(app, /hasPermission\('expenses\.view'\)/);
  assert.match(app, /hasPermission\('expenses\.manage'\)/);
  assert.match(app, /async function renderExpenses\(container\)/);
  assert.match(app, /\/api\/expenses\?page=1&pageSize=100/);
  assert.match(app, /\/api\/expense-categories/);
  assert.match(app, /\/api\/expenses\/summary/);
  assert.match(app, /\/api\/expenses\/\$\{encodeURIComponent\(button\.dataset\.expenseVoid\)\}\/void/);
  assert.match(app, /Voided entries remain in the audit trail/);
});

test('customer portal token is sent only to the anonymous access endpoint and removed from the URL', () => {
  assert.match(app, /async function renderCustomerPortal\(\)/);
  assert.match(app, /\/backend\/api\/customer-portal\/access/);
  assert.match(app, /credentials: 'omit'/);
  assert.match(app, /history\.replaceState\(null, '', `\$\{location\.pathname\}\$\{location\.search\}`\)/);
  assert.match(app, /if \(location\.pathname\.replace\(\/\\\/\$\/, ''\) === '\/customer-portal'\) renderCustomerPortal\(\)/);
  assert.match(app, /send-customer-portal/);
  assert.match(app, /revoke-customer-portal/);
});

test('finance PWA exposes credit-note and receipt review through existing protected API routes', () => {
  assert.match(app, /async function renderFinance/);
  assert.match(app, /hasPermission\('invoices\.manage'\)/);
  assert.match(app, /\/api\/invoices\/\$\{encodeURIComponent\(invoice\.id\)\}\/credit-notes/);
  assert.match(app, /\/api\/receipts\/\$\{encodeURIComponent\(button\.dataset\.receiptView\)\}/);
  assert.match(app, /credit-note-error/);
  assert.match(app, /Credit notes apply to issued invoices only|server rejects credits above the remaining balance/);
  assert.match(app, /data-receipt-view/);
});

test('production notification queue uses the durable email outbox for assignment, issue and newly eligible tasks', async () => {
  const { readFile } = await import('node:fs/promises');
  const queue = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/ProductionNotificationQueue.cs', import.meta.url), 'utf8');
  const manager = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/MyTaskService.cs', import.meta.url), 'utf8');
  const assignments = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/TaskService.cs', import.meta.url), 'utf8');
  assert.match(queue, /db\.EmailOutbox\.Add/);
  assert.match(queue, /DeduplicationKey = key/);
  assert.match(queue, /Permissions\.TasksManage/);
  assert.match(manager, /QueueIssueForManagersAsync/);
  assert.match(manager, /task\.next-step-ready/);
  assert.match(assignments, /task\.assignment/);
  assert.match(assignments, /task\.unblocked/);
});

test('inventory CSV import uses multipart preview, hash-bound commit, and explicit invalid-row choice', () => {
  assert.match(app, /const isFormData = typeof FormData !== 'undefined' && options\.body instanceof FormData/);
  assert.match(app, /api\('\/api\/inventory\/import\/preview', \{ method: 'POST', body: data \}\)/);
  assert.match(app, /expectedHash', preview\.fileHash/);
  assert.match(app, /skipInvalidRows', String\(previewArea\.querySelector\('#inventory-skip-invalid'\)\.checked\)/);
  assert.match(app, /file\.size > 5 \* 1024 \* 1024/);
});

test('expense categories can be created and updated through permission-gated APIs', () => {
  assert.match(app, /id="expense-categories-manage"/);
  assert.match(app, /api\('\/api\/expense-categories', \{ method: 'POST'/);
  assert.match(app, /\/api\/expense-categories\/\$\{encodeURIComponent\(category\.id\)\}/);
  assert.match(app, /isActive: normalized === 'yes'/);
});

test('location scoping is required for order, task, finance, fitting, expense and search services', async () => {
  const { readFile } = await import('node:fs/promises');
  const files = [
    ['OrderService.cs', /IPermissionService permissions, ILocationScope locations\) : IOrderService/],
    ['TaskService.cs', /IPermissionService permissions, ILocationScope locations\) : ITaskService/],
    ['InvoiceService.cs', /ILocationScope locations\) : IInvoiceService/],
    ['PaymentService.cs', /ILocationScope locations\) : IPaymentService/],
    ['ExpenseService.cs', /ILocationScope locations\) : IExpenseService/],
    ['CustomerService.cs', /ILocationScope locations\) : IFittingService/],
    ['ImportExportServices.cs', /ILocationScope locations\) : ISearchService/],
  ];
  for (const [file, signature] of files) {
    const source = await readFile(new URL(`../../backend/src/Tailoring.Infrastructure/Services/${file}`, import.meta.url), 'utf8');
    assert.match(source, signature, `${file} must require ILocationScope`);
    assert.doesNotMatch(source, /ILocationScope\? locations\s*=\s*null/);
  }
});

test('overdue task reminders are queued daily through the durable outbox with tenant and active-membership checks', async () => {
  const { readFile } = await import('node:fs/promises');
  const worker = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/OverdueTaskReminderWorker.cs', import.meta.url), 'utf8');
  const registration = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/DependencyInjection.cs', import.meta.url), 'utf8');
  assert.match(worker, /Notifications:OverdueTaskRemindersEnabled/);
  assert.match(worker, /IgnoreQueryFilters\(\)/);
  assert.match(worker, /membership\.BusinessId == task\.BusinessId/);
  assert.match(worker, /membership\.Status == MembershipStatus\.Active/);
  assert.match(worker, /DeduplicationKey = key/);
  assert.match(worker, /TemplateKey = "task\.overdue"/);
  assert.match(worker, /UniqueViolation/);
  assert.match(registration, /services\.AddHostedService<Services\.OverdueTaskReminderWorker>\(\)/);
});

test('location authorization runs before location existence checks on order and fitting writes', async () => {
  const { readFile } = await import('node:fs/promises');
  const orderService = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/OrderService.cs', import.meta.url), 'utf8');
  const customerService = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/CustomerService.cs', import.meta.url), 'utf8');
  const createOrder = orderService.slice(orderService.indexOf('CreateAsync(CreateOrderRequest'), orderService.indexOf('// ------------------------------------------------------------------ update'));
  assert.ok(createOrder.indexOf('RequireLocationAsync(req.LocationId') < createOrder.indexOf('EnsureLocationAsync(req.LocationId'), 'order create must authorize before checking location existence');
  const fittingStart = customerService.indexOf('public async Task<FittingDto> CreateAsync');
  const fittingEnd = customerService.indexOf('public async Task<FittingDto> SetStatusAsync', fittingStart);
  const createFitting = customerService.slice(fittingStart, fittingEnd);
  assert.ok(createFitting.indexOf('locations.EnsureAllowedAsync(req.LocationId') < createFitting.indexOf('db.Locations.AnyAsync'), 'fitting create must authorize before checking location existence');
});

test('worker and leave-management services scope staff records to permitted locations', async () => {
  const { readFile } = await import('node:fs/promises');
  const workers = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/TaskService.cs', import.meta.url), 'utf8');
  const schedules = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/WorkerScheduleService.cs', import.meta.url), 'utf8');
  const customerService = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/CustomerService.cs', import.meta.url), 'utf8');
  const workerService = workers.slice(workers.indexOf('public sealed class WorkerService'), workers.length);
  assert.match(workerService, /ILocationScope locations/);
  assert.match(workerService, /MembershipLocations\.Any/);
  assert.match(workerService, /taskQuery = taskQuery\.Where\(t => db\.Orders\.Any/);
  assert.match(workerService, /EnsureMembershipVisibleAsync/);
  assert.match(workerService, /WorkerService\(AppDbContext db, ITenantContext tenant, IAuditLogger audit, ILocationScope locations\)/);
  const taskService = workers.slice(0, workers.indexOf('public sealed class WorkerService'));
  assert.match(taskService, /allowedLocations is not null && !await db\.MembershipLocations\.AnyAsync/);
  assert.ok(taskService.indexOf('allowedLocations is not null && !await db.MembershipLocations.AnyAsync') < taskService.indexOf('if (t.AssignedMembershipId == memberId)'), 'assignment idempotence must not bypass worker location visibility');
  assert.match(customerService, /CustomerService\(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements, ILocationScope locations\)/);
  assert.match(customerService, /customer_merge_requires_all_locations/);
  assert.match(schedules, /private async Task EnsureMembershipAsync/);
  assert.match(schedules, /query = query\.Where\(m => db\.MembershipLocations\.Any/);
  assert.match(schedules, /ListPendingLeaveRequestsAsync[\s\S]*?query = query\.Where\(x => db\.MembershipLocations\.Any/);
  assert.match(schedules, /ReviewLeaveRequestAsync[\s\S]*?query = query\.Where\(x => db\.MembershipLocations\.Any/);
});

test('task detail supports permission-gated task-linked stock reservation and consumption', () => {
  assert.match(app, /id="task-materials-manage"/);
  assert.match(app, /hasPermission\('inventory\.manage'\) && hasPermission\('inventory\.view'\)/);
  assert.match(app, /\/api\/inventory\/tasks\/\$\{encodeURIComponent\(taskId\)\}\/\$\{kind\}/);
  assert.match(app, /materials: selected, note:/);
  assert.match(app, /Reserve selected/);
  assert.match(app, /Release selected/);
  assert.match(app, /Consume selected/);
  assert.match(app, /kind === 'release' \? '#task-material-release'/);
});

test('draft invoices can be edited with optimistic version checks and server-side total recalculation', () => {
  assert.match(app, /id="invoice-edit-draft"/);
  assert.match(app, /body = \{ version: Number\(invoice\.version\)/);
  assert.match(app, /api\(`\/api\/invoices\/\$\{encodeURIComponent\(invoice\.id\)\}`, \{ method: 'PUT', body \}\)/);
  assert.match(app, /Order-billed invoice lines are locked/);
  assert.match(app, /body\.lines = rows\.map/);
});

test('finance PWA exposes draft deletion and audited invoice void actions through the authoritative API', () => {
  assert.match(app, /id="invoice-delete-draft"/);
  assert.match(app, /method: 'DELETE'/);
  assert.match(app, /id="invoice-void"/);
  assert.match(app, /\/api\/invoices\/\$\{encodeURIComponent\(invoice\.id\)\}\/void/);
  assert.match(app, /Invoice voided; the audit trail was retained/);
});

test('finance PWA can create free-form and order-linked draft invoices with idempotency keys', () => {
  assert.match(app, /id="invoice-create"/);
  assert.match(app, /api\('\/api\/invoices', \{ method: 'POST', headers: \{ 'idempotency-key': crypto\.randomUUID\(\) \}, body \}\)/);
  assert.match(app, /id="invoice-from-order"/);
  assert.match(app, /\/api\/invoices\/from-order\/\$\{encodeURIComponent\(orderId\)\}/);
  assert.match(app, /mode === 'Deposit' && \(\(amountRaw === ''\) === \(percentRaw === ''\)\)/);
});

test('task stock release restores reserved availability without reducing on-hand stock', async () => {
  const { readFile } = await import('node:fs/promises');
  const inventory = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/InventoryService.cs', import.meta.url), 'utf8');
  const releaseStart = inventory.indexOf('else if (action == StockMovementKind.Release)');
  const consumeStart = inventory.indexOf('else if (action == StockMovementKind.Consume)', releaseStart);
  const releaseBlock = inventory.slice(releaseStart, consumeStart);
  assert.match(releaseBlock, /material\.Reserved -= line\.Quantity/);
  assert.doesNotMatch(releaseBlock, /material\.OnHand\s*-/);
  assert.match(inventory, /StockMovementKind\.Release => "inventory\.task_materials_released"/);
});

test('inventory location choices are filtered by the same location scope used for stock records', async () => {
  const { readFile } = await import('node:fs/promises');
  const inventory = await readFile(new URL('../../backend/src/Tailoring.Infrastructure/Services/InventoryService.cs', import.meta.url), 'utf8');
  const inventoryController = await readFile(new URL('../../backend/src/Tailoring.Api/Controllers/InventoryController.cs', import.meta.url), 'utf8');
  assert.match(inventoryController, /HttpGet\("locations"\), HasPermission\(Permissions\.InventoryView\)/);
  assert.match(inventory, /ListLocationsAsync/);
  assert.match(inventory, /if \(allowed is not null\) query = query\.Where\(x => allowed\.Contains\(x\.Id\)\)/);
  assert.match(app, /api\('\/api\/inventory\/locations'\)/);
  assert.match(app, /locationId: form\.querySelector\('#material-location'\)\.value \|\| null/);
});
