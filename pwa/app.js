const app = document.querySelector('#app');
const toast = document.querySelector('#toast');
const connectionStatus = document.querySelector('#connection-status');
const installButton = document.querySelector('#install-button');
let installPrompt = null;
let currentUser = null;
let loginState = { email: '', password: '', choices: [], businessId: '', twoFactorRequired: false, useRecovery: false, code: '' };
let customerDraft = null;
let customerDuplicateCandidates = null;

class ApiError extends Error {
  constructor(status, body) {
    super(body?.detail || body?.title || body?.message || 'The request could not be completed.');
    this.status = status;
    this.code = body?.code;
    this.body = body;
  }
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
}

function showToast(message) {
  toast.textContent = message;
  toast.hidden = false;
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => { toast.hidden = true; }, 4200);
}

function setConnectionStatus() {
  const online = navigator.onLine;
  connectionStatus.innerHTML = `<span class="status-line"><span class="status-dot ${online ? '' : 'offline'}"></span>${online ? 'Online' : 'Offline — changes may not sync'}</span>`;
}

async function readBody(response) {
  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('json')) {
    try { return await response.json(); } catch { return {}; }
  }
  const text = await response.text();
  return text ? { message: text.slice(0, 240) } : {};
}

async function api(path, options = {}) {
  const method = options.method || 'GET';
  const headers = { accept: 'application/json', ...(options.headers || {}) };
  const isFormData = typeof FormData !== 'undefined' && options.body instanceof FormData;
  if (options.body !== undefined && !isFormData) headers['content-type'] = 'application/json';
  if (method !== 'GET' && method !== 'HEAD') {
    const csrfResponse = await fetch('/backend/api/auth/csrf', { credentials: 'same-origin', cache: 'no-store' });
    if (!csrfResponse.ok) throw new ApiError(csrfResponse.status, await readBody(csrfResponse));
    const csrf = await csrfResponse.json();
    if (!csrf.token) throw new ApiError(500, { message: 'Could not start a secure session. Reload and try again.' });
    headers['x-xsrf-token'] = csrf.token;
  }
  let response;
  try {
    response = await fetch(`/backend${path}`, {
      method,
      headers,
      credentials: 'same-origin',
      cache: 'no-store',
      body: options.body === undefined ? undefined : (isFormData ? options.body : JSON.stringify(options.body))
    });
  } catch {
    throw new ApiError(0, { message: 'Could not reach Avzar. Check your internet connection and try again.' });
  }
  if (!response.ok) throw new ApiError(response.status, await readBody(response));
  if (response.status === 204) return undefined;
  return readBody(response);
}

function renderLogin(message = '') {
  currentUser = null;
  const businessOptions = loginState.choices.map((choice) => `<option value="${escapeHtml(choice.businessId)}" ${choice.businessId === loginState.businessId ? 'selected' : ''}>${escapeHtml(choice.businessName)}</option>`).join('');
  app.innerHTML = `
    <section class="auth-wrap card" aria-labelledby="login-title">
      <p class="eyebrow">Your work, in one place</p>
      <h1 id="login-title">Welcome back.</h1>
      <p class="lead">Sign in to manage your tailoring business, customers and daily work.</p>
      ${message ? `<p class="form-error" role="alert">${escapeHtml(message)}</p>` : ''}
      <form id="login-form" novalidate>
        <div class="field"><label for="email">Email address</label><input id="email" name="email" type="email" autocomplete="username" maxlength="256" required inputmode="email" value="${escapeHtml(loginState.email)}" ${loginState.twoFactorRequired ? 'readonly' : ''}></div>
        <div class="field"><label for="password">Password</label><input id="password" name="password" type="password" autocomplete="current-password" maxlength="128" required value="${escapeHtml(loginState.password)}" ${loginState.twoFactorRequired ? 'readonly' : ''}></div>
        ${loginState.choices.length ? `<div class="field"><label for="business-id">Choose business</label><select id="business-id" name="businessId" required>${businessOptions}</select></div>` : ''}
        ${loginState.twoFactorRequired ? `<div class="field"><label for="two-factor-code">${loginState.useRecovery ? 'Recovery code' : 'Authenticator code'}</label><input id="two-factor-code" name="twoFactorCode" autocomplete="one-time-code" inputmode="${loginState.useRecovery ? 'text' : 'numeric'}" maxlength="80" required value="${escapeHtml(loginState.code)}"></div><button id="recovery-toggle" class="button button-outline" type="button">${loginState.useRecovery ? 'Use authenticator code instead' : 'Use a recovery code instead'}</button>` : ''}
        <button class="button button-primary" type="submit">Sign in</button>
      </form>
      <p class="form-note">Avzar checks your account with the existing API. Passwords and session tokens are not stored in localStorage.</p>
    </section>`;
  document.querySelector('#login-form').addEventListener('submit', submitLogin);
  const businessSelect = document.querySelector('#business-id');
  if (businessSelect) businessSelect.addEventListener('change', () => { loginState.businessId = businessSelect.value; });
  const codeInput = document.querySelector('#two-factor-code');
  if (codeInput) codeInput.addEventListener('input', () => { loginState.code = codeInput.value; });
  const recoveryToggle = document.querySelector('#recovery-toggle');
  if (recoveryToggle) recoveryToggle.addEventListener('click', () => {
    loginState.useRecovery = !loginState.useRecovery;
    loginState.code = '';
    renderLogin();
  });
}

async function submitLogin(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const button = form.querySelector('button[type="submit"]');
  loginState.email = form.elements.email.value.trim();
  loginState.password = form.elements.password.value;
  if (form.elements.businessId) loginState.businessId = form.elements.businessId.value;
  if (form.elements.twoFactorCode) loginState.code = form.elements.twoFactorCode.value.trim();
  if (!loginState.email || !form.elements.email.validity.valid || !loginState.password) {
    renderLogin('Enter a valid email address and password.');
    return;
  }
  if (loginState.twoFactorRequired && !loginState.code) {
    renderLogin('Enter your authenticator or recovery code.');
    return;
  }
  button.disabled = true;
  button.textContent = 'Signing in…';
  const body = { email: loginState.email, password: loginState.password };
  if (loginState.businessId) body.businessId = loginState.businessId;
  if (loginState.twoFactorRequired && loginState.code) {
    body[loginState.useRecovery ? 'recoveryCode' : 'twoFactorCode'] = loginState.code;
  }
  try {
    currentUser = await api('/api/auth/login', { method: 'POST', body });
    loginState = { email: '', password: '', choices: [], businessId: '', twoFactorRequired: false, useRecovery: false, code: '' };
    await renderDashboard();
  } catch (error) {
    if (error.code === 'choose_business') {
      loginState.choices = Array.isArray(error.body?.choices) ? error.body.choices.filter((choice) => choice?.businessId) : [];
      loginState.businessId = loginState.choices[0]?.businessId || '';
      renderLogin('This email belongs to more than one business. Choose the business you want to open.');
      return;
    }
    if (error.code === 'two_factor_required') {
      loginState.twoFactorRequired = true;
      renderLogin('Enter the authenticator code or a recovery code for this account.');
      return;
    }
    renderLogin(error.message || 'Could not sign in.');
  }
}

function hasPermission(permission) {
  return Boolean(currentUser?.isOwner || currentUser?.permissions?.includes(permission));
}

function activeViewFromHash() {
  const raw = location.hash.replace(/^#\/?/, '').split('?')[0];
  return raw || 'home';
}

function dateLabel(value) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date);
}

function navMarkup(view) {
  const root = view.split('/')[0] || 'home';
  return `<nav class="app-nav" aria-label="Business sections">
    <a class="nav-item" href="#home" ${root === 'home' ? 'aria-current="page"' : ''}>Overview</a>
    <a class="nav-item" href="#customers" ${root === 'customers' ? 'aria-current="page"' : ''}>Customers</a>
    <a class="nav-item" href="#orders" ${root === 'orders' ? 'aria-current="page"' : ''}>Orders</a>
    ${hasPermission('orders.view') ? `<a class="nav-item" href="#fittings" ${root === 'fittings' ? 'aria-current="page"' : ''}>Fittings</a>` : ''}
    <a class="nav-item" href="#tasks" ${root === 'tasks' ? 'aria-current="page"' : ''}>Daily work</a>
    ${hasPermission('staff.view') ? `<a class="nav-item" href="#staff" ${root === 'staff' ? 'aria-current="page"' : ''}>Staff</a>` : ''}
    ${hasPermission('inventory.view') ? `<a class="nav-item" href="#inventory" ${root === 'inventory' ? 'aria-current="page"' : ''}>Inventory</a>` : ''}
    ${(hasPermission('invoices.view') || hasPermission('payments.view')) ? `<a class="nav-item" href="#finance" ${root === 'finance' ? 'aria-current="page"' : ''}>Finance</a>` : ''}
    ${hasPermission('expenses.view') ? `<a class="nav-item" href="#expenses" ${root === 'expenses' ? 'aria-current="page"' : ''}>Expenses</a>` : ''}
    <a class="nav-item" href="#settings" ${root === 'settings' ? 'aria-current="page"' : ''}>Settings</a>
  </nav>`;
}

async function renderDashboard() {
  if (!currentUser) {
    renderLogin();
    return;
  }
  const view = activeViewFromHash();
  app.innerHTML = `
    <section class="welcome">
      <p class="eyebrow">Business workspace</p>
      <h1>Hello, ${escapeHtml(currentUser.fullName || 'there')}.</h1>
      <p>You're signed in to <strong>${escapeHtml(currentUser.businessName || 'your business')}</strong>.</p>
      <div class="account-line"><span class="pill">${escapeHtml(currentUser.currencyCode || 'Currency not set')}</span><span class="pill">${currentUser.isOwner ? 'Owner' : 'Team member'}</span></div>
    </section>
    ${navMarkup(view)}
    <section id="view-content" class="panel" aria-live="polite"><div class="loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading your workspace…</p></div></section>
    <div class="account-line"><button id="sign-out" class="button button-outline" type="button">Sign out</button></div>`;
  document.querySelector('#sign-out').addEventListener('click', signOut);
  await renderRoute(view);
}

async function renderRoute(view = activeViewFromHash()) {
  const container = document.querySelector('#view-content');
  if (!container) return;
  if (view === 'home') return renderHomeDashboard(container);
  if (view === 'customers') return renderCustomerList(container);
  if (view === 'customers/new') return renderCustomerForm(container);
  if (view === 'orders') return renderOrderList(container);
  if (view === 'orders/new') return renderOrderForm(container);
  if (view === 'fittings') return renderFittings(container);
  if (view === 'tasks') return renderTaskBoard(container);
  if (view === 'tasks/schedule') return renderShopSchedule(container);
  if (view === 'tasks/workers') return renderWorkerSchedules(container);
  if (view === 'staff') return renderStaffList(container);
  if (view === 'inventory') return renderInventory(container);
  if (view === 'finance') return renderFinance(container);
  if (view === 'expenses') return renderExpenses(container);
  if (view.startsWith('finance/')) return renderFinance(container, view.split('/')[1]);
  if (view === 'staff/invite') return renderStaffInvite(container);
  const parts = view.split('/');
  if (parts[0] === 'customers' && parts[1] && parts[2] === 'measurements' && parts[3] === 'new') {
    return renderMeasurementForm(container, parts[1]);
  }
  if (parts[0] === 'customers' && parts[1] && parts[2] === 'measurements' && parts[3] && parts[4] === 'revise') {
    return renderMeasurementForm(container, parts[1], parts[3]);
  }
  if (parts[0] === 'customers' && parts[1] && parts[2] === 'measurements' && parts[3] && parts[4] === 'history') {
    return renderMeasurementHistory(container, parts[1], parts[3]);
  }
  if (parts[0] === 'customers' && parts[1] && parts[2] === 'measurements' && parts[3] && parts[4] === 'compare') {
    const query = new URLSearchParams(location.hash.split('?')[1] || '');
    return renderMeasurementCompare(container, parts[1], parts[3], query.get('from'), query.get('to'));
  }
  if (parts[0] === 'customers' && parts[1] && parts[1] !== 'new') {
    if (parts[2] === 'edit') return renderCustomerForm(container, parts[1]);
    return renderCustomerDetail(container, parts[1]);
  }
  if (parts[0] === 'orders' && parts[1] === 'new') return renderOrderForm(container);
  if (parts[0] === 'orders' && parts[1] && parts[2] === 'edit') return renderOrderForm(container, parts[1]);
  if (parts[0] === 'orders' && parts[1]) return renderOrderDetail(container, parts[1]);
  if (parts[0] === 'tasks' && parts[1] === 'schedule') return renderShopSchedule(container);
  if (parts[0] === 'tasks' && parts[1] === 'workers') return renderWorkerSchedules(container);
  if (parts[0] === 'tasks' && parts[1]) return renderTaskDetail(container, parts[1]);
  if (parts[0] === 'staff' && parts[1] === 'invite') return renderStaffInvite(container);
  if (parts[0] === 'settings' && parts[1] === 'security') return renderAccountSecurity(container);
  if (parts[0] === 'settings') return renderBusinessSettings(container);
  container.innerHTML = `<section class="card"><h2>Page not found</h2><p><a href="#home">Return to overview</a></p></section>`;
}

async function renderHomeDashboard(container) {
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading your business overview…</p></section>';
  const canViewCustomers = hasPermission('customers.view');
  const canViewOrders = hasPermission('orders.view');
  const canViewTasks = hasPermission('tasks.view') || hasPermission('tasks.work');
  const requests = await Promise.allSettled([
    canViewCustomers ? api('/api/customers?page=1&pageSize=1') : Promise.resolve(null),
    canViewOrders ? api('/api/orders?page=1&pageSize=5') : Promise.resolve(null),
    canViewTasks ? api(hasPermission('tasks.view') ? '/api/tasks?page=1&pageSize=10' : '/api/my/tasks?page=1&pageSize=10') : Promise.resolve(null)
  ]);
  const [customerResult, orderResult, taskResult] = requests;
  const customers = customerResult.status === 'fulfilled' ? customerResult.value : null;
  const ordersPayload = orderResult.status === 'fulfilled' ? orderResult.value : null;
  const tasksPayload = taskResult.status === 'fulfilled' ? taskResult.value : null;
  const orderItems = Array.isArray(ordersPayload?.items) ? ordersPayload.items : [];
  const taskItems = Array.isArray(tasksPayload?.items) ? tasksPayload.items : Array.isArray(tasksPayload) ? tasksPayload : [];
  const metric = (label, value, note) => `<article class="card metric-card"><p class="eyebrow">${escapeHtml(label)}</p><h3>${escapeHtml(value)}</h3><p>${escapeHtml(note)}</p></article>`;
  const failed = (result) => result.status === 'rejected' ? `<p class="form-note">Could not load this summary: ${escapeHtml(result.reason?.message || 'request failed')}.</p>` : '';
  const recentOrders = orderItems.slice(0, 5).map((order) => `<tr><td><a href="#orders/${escapeHtml(order.id)}"><strong>${escapeHtml(order.orderNumber || 'Order')}</strong></a><small>${escapeHtml(order.customerName || '')}</small></td><td><span class="pill">${escapeHtml(order.status || '—')}</span></td><td>${escapeHtml(dateLabel(order.promisedDate))}</td><td>${escapeHtml(order.currencyCode || '')} ${escapeHtml(Number(order.total || 0).toLocaleString(undefined, { maximumFractionDigits: 2 }))}</td></tr>`).join('');
  const openTasks = taskItems.filter((task) => !['Completed', 'Cancelled'].includes(task.status));
  container.innerHTML = `<section class="panel"><div class="panel-head"><div><p class="eyebrow">Business overview</p><h2>Welcome back${currentUser?.fullName ? `, ${escapeHtml(currentUser.fullName)}` : ''}</h2><p class="lead">A live summary from the business records your account is allowed to access.</p></div><span class="status-line"><span class="status-dot"></span> Signed in</span></div><div class="metric-grid">${canViewCustomers ? metric('Customers', customers && Number.isFinite(Number(customers.totalCount)) ? Number(customers.totalCount).toLocaleString() : '—', 'Registered customer records') : ''}${canViewOrders ? metric('Orders', ordersPayload && Number.isFinite(Number(ordersPayload.totalCount)) ? Number(ordersPayload.totalCount).toLocaleString() : '—', 'Orders in this business') : ''}${canViewTasks ? metric('Open tasks', tasksPayload ? openTasks.length.toLocaleString() : '—', 'Open tasks in the loaded task window') : ''}</div>${failed(customerResult)}${failed(orderResult)}${failed(taskResult)}${canViewOrders ? `<div class="card panel"><div class="panel-head"><div><h3>Recent orders</h3><p>Latest records returned by the order API.</p></div><a class="button button-outline button-fit" href="#orders">View all</a></div><div class="table-wrap"><table class="data-table"><thead><tr><th>Order</th><th>Status</th><th>Promised date</th><th>Total</th></tr></thead><tbody>${recentOrders || '<tr><td colspan="4">No recent orders returned.</td></tr>'}</tbody></table></div></div>` : ''}${canViewTasks ? `<div class="card panel"><div class="panel-head"><div><h3>Production work</h3><p>Current open tasks in the loaded window.</p></div><a class="button button-outline button-fit" href="#tasks">Open daily work</a></div>${openTasks.length ? `<ul class="dashboard-task-list">${openTasks.slice(0, 5).map((task) => `<li><a href="#tasks/${escapeHtml(task.id)}"><strong>${escapeHtml(task.title || task.orderNumber || 'Production task')}</strong></a><span class="pill">${escapeHtml(task.status || 'Pending')}</span><small>${escapeHtml(task.stageName || '')} · Due ${escapeHtml(dateLabel(task.deadlineUtc))}</small></li>`).join('')}</ul>` : '<p class="empty-state">No open tasks in the loaded window.</p>'}</div>` : ''}<p class="form-note">Summary cards use real API responses. A dash or an error message means that the API did not return a usable count; Avzar does not invent totals.</p></section>`;
}

async function renderBusinessSettings(container) {
  if (!hasPermission('business.settings')) {
    container.innerHTML = '<section class="card panel"><p class="eyebrow">Settings</p><h2>Settings</h2><p class="form-note">Your role cannot change business opening hours or document branding.</p><a class="button button-primary" href="#settings/security">Account security and sessions</a></section>';
    return;
  }
  container.innerHTML = '<section class="card loading-card"><div class="spinner"></div><p>Loading business settings…</p></section>';
  try {
    const [schedule, branding] = await Promise.all([api('/api/business/schedule'), api('/api/business/branding')]);
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const byDay = new Map((schedule.days || []).map((day) => [Number(day.dayOfWeek), day]));
    const hoursRows = days.map((name, index) => {
      const day = byDay.get(index) || { dayOfWeek: index, isOpen: false, opensAt: '09:00', closesAt: '17:00' };
      return `<tr><td>${name}</td><td><input type="checkbox" aria-label="${name} open" data-hours-day="${index}" data-hours-field="isOpen" ${day.isOpen ? 'checked' : ''}></td><td><input type="time" aria-label="${name} opens at" data-hours-day="${index}" data-hours-field="opensAt" value="${escapeHtml(String(day.opensAt || '09:00').slice(0,5))}" ${day.isOpen ? '' : 'disabled'}></td><td><input type="time" aria-label="${name} closes at" data-hours-day="${index}" data-hours-field="closesAt" value="${escapeHtml(String(day.closesAt || '17:00').slice(0,5))}" ${day.isOpen ? '' : 'disabled'}></td></tr>`;
    }).join('');
    const holidays = Array.isArray(schedule.holidays) ? schedule.holidays : [];
    const holidayRows = holidays.map((holiday) => `<tr><td>${escapeHtml(holiday.date)}</td><td>${escapeHtml(holiday.name)}</td><td><button class="button button-outline" type="button" data-remove-holiday="${escapeHtml(holiday.id)}">Remove</button></td></tr>`).join('');
    container.innerHTML = `<section class="panel"><p class="eyebrow">Business setup</p><h2>Business settings</h2><p class="lead">Configure opening hours, holidays and the contact details printed on business documents.</p><p><a class="button button-outline button-fit" href="#settings/security">Account security and sessions</a></p><div id="settings-error"></div><div class="card panel"><h3>Opening hours</h3><p class="form-note">Business time zone: ${escapeHtml(schedule.timeZoneId || 'Not configured')}</p><form id="business-hours-form"><div class="table-wrap"><table class="data-table"><thead><tr><th>Day</th><th>Open</th><th>Opens</th><th>Closes</th></tr></thead><tbody>${hoursRows}</tbody></table></div><button class="button button-primary" type="submit">Save opening hours</button></form></div><div class="card panel"><h3>Business holidays</h3><form id="business-holiday-form" class="filter-form"><div class="field"><label for="holiday-date">Date</label><input id="holiday-date" type="date" required></div><div class="field"><label for="holiday-name">Name</label><input id="holiday-name" maxlength="80" minlength="1" required placeholder="e.g. Public holiday"></div><button class="button button-outline" type="submit">Add holiday</button></form><div class="table-wrap"><table class="data-table"><thead><tr><th>Date</th><th>Holiday</th><th>Action</th></tr></thead><tbody>${holidayRows || '<tr><td colspan="3">No holidays configured.</td></tr>'}</tbody></table></div></div><div class="card panel"><h3>Document branding</h3><form id="business-branding-form"><div class="field"><label for="branding-accent">Accent colour</label><input id="branding-accent" type="color" value="${/^#[0-9a-f]{6}$/i.test(branding.accentColor || '') ? branding.accentColor : '#1F3A5F'}"></div><div class="field"><label for="branding-phone">Business phone</label><input id="branding-phone" maxlength="40" value="${escapeHtml(branding.phone || '')}"></div><div class="field"><label for="branding-email">Business email</label><input id="branding-email" type="email" maxlength="254" value="${escapeHtml(branding.email || '')}"></div><div class="field"><label for="branding-tax">Tax number</label><input id="branding-tax" maxlength="60" value="${escapeHtml(branding.taxNumber || '')}"></div><div class="field"><label for="branding-footer">Document footer</label><textarea id="branding-footer" maxlength="300" rows="3">${escapeHtml(branding.footerNote || '')}</textarea></div><button class="button button-primary" type="submit">Save document branding</button></form></div></section>`;
    const errorBox = document.querySelector('#settings-error');
    const showError = (error) => { errorBox.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'The setting could not be saved.')}</p>`; };
    document.querySelectorAll('[data-hours-day]').forEach((input) => input.addEventListener('change', () => {
      if (input.dataset.hoursField !== 'isOpen') return;
      const day = input.dataset.hoursDay;
      document.querySelectorAll(`[data-hours-day="${day}"][data-hours-field="opensAt"], [data-hours-day="${day}"][data-hours-field="closesAt"]`).forEach((time) => { time.disabled = !input.checked; });
    }));
    document.querySelector('#business-hours-form').addEventListener('submit', async (event) => {
      event.preventDefault();
      const values = days.map((_, dayOfWeek) => {
        const read = (field) => document.querySelector(`[data-hours-day="${dayOfWeek}"][data-hours-field="${field}"]`);
        const isOpen = read('isOpen').checked;
        return { dayOfWeek, isOpen, opensAt: isOpen ? read('opensAt').value : null, closesAt: isOpen ? read('closesAt').value : null };
      });
      if (values.some((day) => day.isOpen && (!day.opensAt || !day.closesAt || day.opensAt >= day.closesAt))) { showError(new Error('For each open day, closing time must be later than opening time.')); return; }
      try { await api('/api/business/schedule/hours', { method: 'PUT', body: { days: values } }); showToast('Business opening hours saved.'); await renderBusinessSettings(container); }
      catch (error) { showError(error); }
    });
    document.querySelector('#business-holiday-form').addEventListener('submit', async (event) => {
      event.preventDefault();
      const form = event.currentTarget;
      try { await api('/api/business/schedule/holidays', { method: 'POST', body: { date: form.elements[0].value, name: form.elements[1].value.trim() } }); showToast('Business holiday added.'); await renderBusinessSettings(container); }
      catch (error) { showError(error); }
    });
    document.querySelectorAll('[data-remove-holiday]').forEach((button) => button.addEventListener('click', async () => {
      if (!confirm('Remove this business holiday?')) return;
      try { await api(`/api/business/schedule/holidays/${encodeURIComponent(button.dataset.removeHoliday)}`, { method: 'DELETE' }); showToast('Business holiday removed.'); await renderBusinessSettings(container); }
      catch (error) { showError(error); }
    }));
    document.querySelector('#business-branding-form').addEventListener('submit', async (event) => {
      event.preventDefault();
      const value = (id) => document.querySelector(id).value.trim();
      const body = { accentColor: value('#branding-accent'), phone: value('#branding-phone') || null, email: value('#branding-email') || null, taxNumber: value('#branding-tax') || null, footerNote: value('#branding-footer') || null, logoMediaId: branding.logoMediaId || null };
      try { await api('/api/business/branding', { method: 'PUT', body }); showToast('Document branding saved.'); }
      catch (error) { showError(error); }
    });
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not load business settings</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p></section>`;
  }
}

async function renderAccountSecurity(container) {
  container.innerHTML = '<section class="card loading-card"><div class="spinner"></div><p>Loading account security…</p></section>';
  try {
    const [security, sessions] = await Promise.all([api('/api/account/security'), api('/api/account/sessions')]);
    const recoveryCodesMarkup = Array.isArray(window.__avzarRecoveryCodes) && window.__avzarRecoveryCodes.length
      ? `<section class="card panel"><h3>Save your recovery codes</h3><p class="form-note">These codes are shown only after enabling two-factor authentication. Store them somewhere safe; each code works once.</p><ul>${window.__avzarRecoveryCodes.map((code) => `<li><code>${escapeHtml(code)}</code></li>`).join('')}</ul><button class="button button-outline" id="dismiss-recovery-codes" type="button">I saved these codes</button></section>` : '';
    const sessionRows = (Array.isArray(sessions) ? sessions : []).map((session) => `<tr><td>${escapeHtml(session.deviceName || 'Unknown device')}</td><td>${escapeHtml(dateLabel(session.lastUsedAtUtc))}</td><td>${escapeHtml(dateLabel(session.expiresAtUtc))}</td><td><button class="button button-outline" type="button" data-revoke-session="${escapeHtml(session.id)}">Revoke</button></td></tr>`).join('');
    container.innerHTML = `<section class="panel"><p><a href="#settings">← Business settings</a></p><p class="eyebrow">Your account</p><h2>Security and sessions</h2><div id="security-error"></div>${recoveryCodesMarkup}<div class="card panel"><h3>Account security</h3><dl class="detail-list"><dt>Email verified</dt><dd>${security.emailConfirmed ? 'Yes' : 'Not yet verified'}</dd><dt>Two-factor authentication</dt><dd>${security.twoFactorEnabled ? 'Enabled' : 'Disabled'}</dd><dt>Recovery codes remaining</dt><dd>${Number(security.recoveryCodesLeft) || 0}</dd><dt>Active device sessions</dt><dd>${Number(security.activeSessions) || 0}</dd></dl>${!security.emailConfirmed ? '<button class="button button-outline" id="resend-verification" type="button">Resend verification email</button>' : ''}${security.twoFactorEnabled ? `<form id="disable-two-factor-form"><h4>Disable two-factor authentication</h4><div class="field"><label for="disable-2fa-password">Current password</label><input id="disable-2fa-password" type="password" required autocomplete="current-password"></div><div class="field"><label for="disable-2fa-type">Verification method</label><select id="disable-2fa-type"><option value="authenticator">Authenticator code</option><option value="recovery">Recovery code</option></select></div><div class="field"><label for="disable-2fa-code">Verification code</label><input id="disable-2fa-code" maxlength="80" required autocomplete="one-time-code"></div><button class="button button-outline" type="submit">Disable two-factor authentication</button></form>` : '<button class="button button-primary" id="begin-two-factor" type="button">Set up two-factor authentication</button>'}</div><div class="card panel"><h3>Change password</h3><form id="change-password-form"><div class="field"><label for="current-password">Current password</label><input id="current-password" type="password" required autocomplete="current-password"></div><div class="field"><label for="new-password">New password</label><input id="new-password" type="password" minlength="10" maxlength="128" required autocomplete="new-password"></div><div class="field"><label for="confirm-password">Confirm new password</label><input id="confirm-password" type="password" minlength="10" maxlength="128" required autocomplete="new-password"></div><button class="button button-primary" type="submit">Change password</button></form></div><div class="card panel"><div class="panel-head"><div><h3>Device sessions</h3><p>Revoke sessions you no longer recognize or use.</p></div><button class="button button-outline button-fit" type="button" id="revoke-all-sessions">Revoke all</button></div><div class="table-wrap"><table class="data-table"><thead><tr><th>Device</th><th>Last used</th><th>Expires</th><th>Action</th></tr></thead><tbody>${sessionRows || '<tr><td colspan="4">No device sessions were returned.</td></tr>'}</tbody></table></div></div><div id="two-factor-setup"></div></section>`;
    const errorBox = document.querySelector('#security-error');
    const showError = (error) => { errorBox.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'The security change failed.')}</p>`; };
    document.querySelector('#dismiss-recovery-codes')?.addEventListener('click', () => { window.__avzarRecoveryCodes = null; renderAccountSecurity(container); });
    document.querySelector('#resend-verification')?.addEventListener('click', async () => { try { await api('/api/account/resend-verification', { method: 'POST' }); showToast('If verification can be resent, an email has been queued.'); } catch (error) { showError(error); } });
    document.querySelector('#begin-two-factor')?.addEventListener('click', async () => {
      try { const setup = await api('/api/account/two-factor/setup', { method: 'POST' }); const target = document.querySelector('#two-factor-setup'); const otpAuthUri = typeof setup.otpAuthUri === 'string' && setup.otpAuthUri.startsWith('otpauth://') ? setup.otpAuthUri : null; target.innerHTML = `<section class="card panel"><h3>Finish two-factor setup</h3><p>Enter this key in your authenticator app, then submit the current code. Keep the key private.</p><p><strong>Setup key</strong></p><p><code>${escapeHtml(setup.sharedKey)}</code></p>${otpAuthUri ? `<p><a href="${escapeHtml(otpAuthUri)}">Open authenticator link</a></p>` : ''}<form id="enable-two-factor-form"><div class="field"><label for="enable-2fa-code">Authenticator code</label><input id="enable-2fa-code" inputmode="numeric" autocomplete="one-time-code" minlength="6" maxlength="12" required></div><button class="button button-primary" type="submit">Enable two-factor authentication</button></form></section>`; document.querySelector('#enable-two-factor-form').addEventListener('submit', async (event) => { event.preventDefault(); try { const result = await api('/api/account/two-factor/enable', { method: 'POST', body: { code: document.querySelector('#enable-2fa-code').value.trim() } }); window.__avzarRecoveryCodes = result.codes || []; showToast('Two-factor authentication enabled. Save your recovery codes now.'); await renderAccountSecurity(container); } catch (error) { showError(error); } }); }
      catch (error) { showError(error); }
    });
    document.querySelector('#disable-two-factor-form')?.addEventListener('submit', async (event) => { event.preventDefault(); const value = document.querySelector('#disable-2fa-code').value.trim(); const method = document.querySelector('#disable-2fa-type').value; if (!value) { showError(new Error('Enter the selected verification code.')); return; } try { await api('/api/account/two-factor/disable', { method: 'POST', body: { password: document.querySelector('#disable-2fa-password').value, ...(method === 'recovery' ? { recoveryCode: value } : { code: value }) } }); window.__avzarRecoveryCodes = null; showToast('Two-factor authentication disabled.'); await renderAccountSecurity(container); } catch (error) { showError(error); } });
    document.querySelector('#change-password-form').addEventListener('submit', async (event) => { event.preventDefault(); const currentPassword = document.querySelector('#current-password').value; const newPassword = document.querySelector('#new-password').value; const confirmPassword = document.querySelector('#confirm-password').value; if (newPassword !== confirmPassword) { showError(new Error('The new password and confirmation do not match.')); return; } try { await api('/api/account/change-password', { method: 'POST', body: { currentPassword, newPassword } }); showToast('Password changed successfully.'); event.currentTarget.reset(); } catch (error) { showError(error); } });
    document.querySelectorAll('[data-revoke-session]').forEach((button) => button.addEventListener('click', async () => { if (!confirm('Revoke this device session?')) return; try { await api(`/api/account/sessions/${encodeURIComponent(button.dataset.revokeSession)}`, { method: 'DELETE' }); showToast('Device session revoked.'); await renderAccountSecurity(container); } catch (error) { showError(error); } }));
    document.querySelector('#revoke-all-sessions')?.addEventListener('click', async () => { if (!confirm('Revoke all device sessions? This may sign out other devices.')) return; try { await api('/api/account/sessions/revoke-all', { method: 'POST' }); showToast('Device sessions revoked.'); await renderAccountSecurity(container); } catch (error) { showError(error); } });
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not load account security</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p><a href="#settings">Back to settings</a></section>`;
  }
}

async function renderOrderList(container) {
  if (!hasPermission('orders.view')) {
    container.innerHTML = '<section class="card"><h2>Orders</h2><p class="form-error">Your account does not have permission to view orders.</p></section>';
    return;
  }
  const query = new URLSearchParams(location.hash.split('?')[1] || '');
  const search = (query.get('search') || '').slice(0, 100);
  const status = query.get('status') || '';
  const page = Math.max(1, Math.min(10000, Number.parseInt(query.get('page') || '1', 10) || 1));
  const params = new URLSearchParams({ page: String(page), pageSize: '20' });
  if (search) params.set('search', search);
  if (status) params.set('status', status);
  container.innerHTML = `<section class="card panel"><div class="panel-head"><div><p class="eyebrow">Production pipeline</p><h2>Orders</h2><p>Track customer orders and promised delivery dates.</p></div>${hasPermission('orders.manage') ? '<a class="button button-primary button-fit" href="#orders/new">New order</a>' : ''}</div><form id="order-search" class="filter-form"><div class="field"><label for="order-search-text">Search</label><input id="order-search-text" name="search" maxlength="100" value="${escapeHtml(search)}" placeholder="Order number or customer"></div><div class="field"><label for="order-status">Status</label><select id="order-status" name="status"><option value="" ${status === '' ? 'selected' : ''}>All statuses</option>${['Draft','Quoted','Confirmed','InProduction','Ready','Delivered','Cancelled'].map((value) => `<option value="${value}" ${status === value ? 'selected' : ''}>${value}</option>`).join('')}</select></div><button class="button button-outline" type="submit">Apply filters</button></form><div id="order-results" class="loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading orders…</p></div></section>`;
  document.querySelector('#order-search').addEventListener('submit', (event) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const next = new URLSearchParams();
    if (String(data.get('search') || '').trim()) next.set('search', String(data.get('search')).trim());
    if (String(data.get('status') || '')) next.set('status', String(data.get('status')));
    location.hash = `orders${next.size ? `?${next}` : ''}`;
  });
  try {
    const result = await api(`/api/orders?${params.toString()}`);
    const rows = Array.isArray(result?.items) ? result.items : Array.isArray(result?.Items) ? result.Items : [];
    const totalCount = Number(result?.totalCount ?? result?.TotalCount ?? rows.length) || 0;
    const pages = Math.max(1, Math.ceil(totalCount / 20));
    const table = rows.length ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Order</th><th>Customer</th><th>Promised date</th><th>Status</th><th>Total</th></tr></thead><tbody>${rows.map((order) => `<tr><td><a href="#orders/${escapeHtml(order.id)}"><strong>${escapeHtml(order.orderNumber)}</strong></a><small>${Number(order.garmentCount) || 0} garment line(s)</small></td><td>${escapeHtml(order.customerName)}</td><td>${escapeHtml(dateLabel(order.promisedDate))}</td><td><span class="pill">${escapeHtml(order.status)}</span></td><td>${escapeHtml(order.currencyCode || currentUser.currencyCode || '')} ${escapeHtml(Number(order.total || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))}</td></tr>`).join('')}</tbody></table></div>` : `<p class="empty-state">${search || status ? 'No orders match these filters.' : 'No orders have been created yet.'}</p>`;
    const prev = page > 1 ? `#orders?${new URLSearchParams({ ...(search ? { search } : {}), ...(status ? { status } : {}), page: String(page - 1) })}` : '';
    const next = page < pages ? `#orders?${new URLSearchParams({ ...(search ? { search } : {}), ...(status ? { status } : {}), page: String(page + 1) })}` : '';
    document.querySelector('#order-results').innerHTML = `${table}<div class="pager"><span>${totalCount} order${totalCount === 1 ? '' : 's'} · page ${Math.min(page, pages)} of ${pages}</span><span>${prev ? `<a class="button button-outline" href="${prev}">Previous</a>` : ''} ${next ? `<a class="button button-outline" href="${next}">Next</a>` : ''}</span></div>`;
  } catch (error) {
    const target = document.querySelector('#order-results');
    if (target) target.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not load orders.')}</p><button class="button button-outline" id="retry-orders" type="button">Try again</button>`;
    document.querySelector('#retry-orders')?.addEventListener('click', () => renderOrderList(container));
  }
}

async function renderOrderDetail(container, orderId) {
  if (!hasPermission('orders.view')) {
    container.innerHTML = '<section class="card"><h2>Order access</h2><p class="form-error">Your account does not have permission to view orders.</p></section>';
    return;
  }
  if (!/^[0-9a-f-]{36}$/i.test(orderId)) {
    container.innerHTML = '<section class="card"><h2>Order not found</h2><a href="#orders">Back to orders</a></section>';
    return;
  }
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading order…</p></section>';
  try {
    const order = await api(`/api/orders/${encodeURIComponent(orderId)}`);
    const garments = Array.isArray(order.garments) ? order.garments : [];
    const progress = order.progress;
    const garmentRows = garments.map((line) => `<tr><td><strong>${escapeHtml(line.name)}</strong><small>${escapeHtml([line.category, line.fabric, line.colour].filter(Boolean).join(' · ') || 'Garment')}</small></td><td>${Number(line.quantity) || 0}</td><td>${escapeHtml(Number(line.unitPrice || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))}</td><td>${escapeHtml(Number(line.lineTotal || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))}</td></tr>`).join('');
    const canManage = hasPermission('orders.manage');
    const actions = [];
    if (hasPermission('tasks.manage') && ['Confirmed', 'InProduction'].includes(order.status) && (!progress || Number(progress.totalTasks) === 0)) actions.push('<button class="button button-outline" id="order-generate-tasks">Generate production tasks</button>');
    if (canManage && ['Draft', 'Quoted'].includes(order.status)) actions.push(`<a class="button button-outline" href="#orders/${escapeHtml(order.id)}/edit">Edit order</a>`);
    if (canManage && order.status === 'Draft') actions.push('<button class="button button-outline" id="order-quote">Prepare quote</button>');
    if (hasPermission('orders.confirm') && order.status === 'Quoted') actions.push('<button class="button button-primary" id="order-confirm">Confirm order</button>');
    if (canManage && !['Cancelled', 'Delivered'].includes(order.status)) actions.push('<button class="button button-outline" id="order-cancel">Cancel order</button>');
    if (canManage && order.status === 'Ready') actions.push('<button class="button button-primary" id="order-deliver">Mark delivered</button>');
    container.innerHTML = `<section class="panel"><p><a href="#orders">← Back to orders</a></p><div class="card panel"><div class="panel-head"><div><p class="eyebrow">Order details</p><h2>${escapeHtml(order.orderNumber)}</h2><p class="lead">For <a href="#customers/${escapeHtml(order.customerId)}">${escapeHtml(order.customerName)}</a></p></div><span class="pill">${escapeHtml(order.status)}</span></div><dl class="detail-list"><dt>Promised date</dt><dd>${escapeHtml(dateLabel(order.promisedDate))}</dd><dt>Priority</dt><dd>${escapeHtml(order.priority)}</dd><dt>Delivery method</dt><dd>${escapeHtml(order.deliveryMethod)}</dd><dt>Created</dt><dd>${escapeHtml(dateLabel(order.createdAtUtc))}</dd><dt>Delivery instructions</dt><dd>${escapeHtml(order.deliveryInstructions || '—')}</dd><dt>Notes</dt><dd class="pre-wrap">${escapeHtml(order.notes || '—')}</dd></dl>${progress ? `<h3>Production progress</h3><p>${Number(progress.percentComplete) || 0}% complete · ${Number(progress.completed) || 0} of ${Number(progress.totalTasks) || 0} tasks completed · ${Number(progress.blocked) || 0} blocked</p>` : ''}<div id="order-action-error"></div><div class="account-line">${actions.join('')}</div><p><a href="#tasks?orderId=${encodeURIComponent(order.id)}">View production tasks</a></p></div><div class="card panel"><h3>Garments</h3><div class="table-wrap"><table class="data-table"><thead><tr><th>Garment</th><th>Qty</th><th>Unit price</th><th>Line total</th></tr></thead><tbody>${garmentRows || '<tr><td colspan="4">No garment lines.</td></tr>'}</tbody></table></div><dl class="detail-list"><dt>Subtotal</dt><dd>${escapeHtml(order.currencyCode)} ${escapeHtml(Number(order.subtotal || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))}</dd><dt>Discount</dt><dd>${escapeHtml(Number(order.discount || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))}</dd><dt>Total</dt><dd><strong>${escapeHtml(order.currencyCode)} ${escapeHtml(Number(order.total || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))}</strong></dd></dl></div></section>`;
    const action = async (id, method = 'POST', body) => {
      const box = document.querySelector('#order-action-error');
      box.innerHTML = '';
      try { await api(`/api/orders/${encodeURIComponent(order.id)}/${id}`, { method, ...(body ? { body } : {}) }); showToast('Order updated.'); await renderOrderDetail(container, order.id); }
      catch (error) { box.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; }
    };
    document.querySelector('#order-generate-tasks')?.addEventListener('click', async () => { const box = document.querySelector('#order-action-error'); try { const result = await api(`/api/orders/${encodeURIComponent(order.id)}/generate-tasks`, { method: 'POST', body: {} }); showToast(`Created ${Number(result.tasksCreated) || 0} production tasks.`); await renderOrderDetail(container, order.id); } catch (error) { box.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; } });
    document.querySelector('#order-quote')?.addEventListener('click', () => action('quote'));
    document.querySelector('#order-confirm')?.addEventListener('click', () => { if (confirm('Confirm this order and generate its production tasks?')) action('confirm'); });
    document.querySelector('#order-deliver')?.addEventListener('click', () => { if (confirm('Mark this order as delivered? The API will reject this if production tasks remain open.')) action('deliver'); });
    document.querySelector('#order-cancel')?.addEventListener('click', () => { const reason = prompt('Reason for cancelling this order (at least 3 characters):'); if (reason && reason.trim().length >= 3) action('cancel', 'POST', { reason: reason.trim() }); });
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not open order</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p><a href="#orders">Back to orders</a></section>`;
  }
}

function garmentLineMarkup(garment = {}, index = 0) {
  return `<fieldset class="garment-line card" data-garment-id="${escapeHtml(garment.id || '')}"><legend>Garment ${index + 1}</legend><div class="field"><label>Garment name *</label><input data-field="name" maxlength="120" required value="${escapeHtml(garment.name || '')}" placeholder="e.g. Senator outfit"></div><div class="field"><label>Category</label><input data-field="category" maxlength="80" value="${escapeHtml(garment.category || '')}"></div><div class="field"><label>Quantity *</label><input data-field="quantity" type="number" min="1" max="10000" value="${Number(garment.quantity) || 1}" required></div><div class="field"><label>Unit price *</label><input data-field="unitPrice" type="number" min="0" max="100000000" step="0.01" value="${garment.unitPrice !== undefined ? escapeHtml(garment.unitPrice) : ''}" required></div><div class="field"><label>Fabric</label><input data-field="fabric" maxlength="120" value="${escapeHtml(garment.fabric || '')}"></div><div class="field"><label>Colour</label><input data-field="colour" maxlength="60" value="${escapeHtml(garment.colour || '')}"></div><div class="field"><label>Style instructions</label><textarea data-field="styleInstructions" maxlength="4000" rows="3">${escapeHtml(garment.styleInstructions || '')}</textarea></div><button class="button button-outline" type="button" data-remove-garment>Remove garment</button></fieldset>`;
}

async function renderOrderForm(container, orderId = null) {
  if (!hasPermission('orders.manage')) { container.innerHTML = '<section class="card"><h2>Order access</h2><p class="form-error">Your account does not have permission to manage orders.</p></section>'; return; }
  let existing = null;
  if (orderId) {
    try { existing = await api(`/api/orders/${encodeURIComponent(orderId)}`); }
    catch (error) { container.innerHTML = `<section class="card"><h2>Could not open order</h2><p class="form-error">${escapeHtml(error.message)}</p><a href="#orders">Back to orders</a></section>`; return; }
    if (!['Draft', 'Quoted'].includes(existing.status)) { container.innerHTML = `<section class="card"><h2>Order cannot be edited</h2><p>Orders in ${escapeHtml(existing.status)} status cannot be edited here.</p><a href="#orders/${escapeHtml(existing.id)}">Back to order</a></section>`; return; }
  }
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading customers and locations…</p></section>';
  try {
    const [customerResult, locations] = await Promise.all([existing ? Promise.resolve({ items: [] }) : api('/api/customers?page=1&pageSize=100'), api('/api/locations')]);
    const customers = Array.isArray(customerResult?.items) ? customerResult.items : [];
    const activeLocations = Array.isArray(locations) ? locations.filter((l) => l.isActive !== false) : [];
    const selectedCustomer = existing?.customerId || '';
    const selectedLocation = existing?.locationId || '';
    const selectedGarment = existing?.garments?.[0] || {};
    const selectedDate = String(existing?.promisedDate || '').slice(0, 10);
    container.innerHTML = `<section class="card panel"><p><a href="${existing ? `#orders/${escapeHtml(existing.id)}` : '#orders'}">← Back to orders</a></p><p class="eyebrow">Production pipeline</p><h2>${existing ? 'Edit order' : 'Create order'}</h2><p class="lead">The API validates pricing, permissions, status transitions and business rules.</p><div id="order-form-error"></div><form id="order-create-form">
      ${existing ? '' : `<div class="field"><label for="order-customer">Customer *</label><select id="order-customer" name="customerId" required><option value="">Choose customer</option>${customers.map((c) => `<option value="${escapeHtml(c.id)}">${escapeHtml(c.fullName)}</option>`).join('')}</select></div>`}
      <div class="field"><label for="order-location">Workshop location *</label><select id="order-location" name="locationId" required><option value="">Choose location</option>${activeLocations.map((l) => `<option value="${escapeHtml(l.id)}">${escapeHtml(l.name)}${l.id === selectedLocation ? ' (current)' : ''}</option>`).join('')}</select></div>
      <div class="field"><label for="order-promised">Promised date *</label><input id="order-promised" name="promisedDate" type="date" required value="${escapeHtml(selectedDate)}"></div>
      <div class="field"><label for="order-priority">Priority</label><select id="order-priority" name="priority">${['Low','Normal','High','Urgent'].map((v) => `<option ${((existing?.priority || 'Normal') === v) ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
      <div class="field"><label for="order-delivery-method">Delivery method</label><select id="order-delivery-method" name="deliveryMethod">${['Collection','Delivery'].map((v) => `<option ${((existing?.deliveryMethod || 'Collection') === v) ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
      <div class="field"><label for="order-address">Delivery address</label><input id="order-address" name="deliveryAddress" maxlength="500" value="${escapeHtml(existing?.deliveryAddress || '')}"></div>
      <div class="field"><label for="order-instructions">Delivery instructions</label><textarea id="order-instructions" name="deliveryInstructions" maxlength="1000" rows="2">${escapeHtml(existing?.deliveryInstructions || '')}</textarea></div>
      <hr><div class="panel-head"><div><h3>Garments</h3><p>Add every garment included in this order.</p></div><button class="button button-outline" id="add-garment-line" type="button">Add garment</button></div><div id="garment-lines">${(existing?.garments?.length ? existing.garments : [{}]).map((g, i) => garmentLineMarkup(g, i)).join('')}</div>
      <div class="field"><label for="order-discount">Discount</label><input id="order-discount" name="discount" type="number" min="0" max="100000000" step="0.01" value="${Number(existing?.discount) || 0}"></div>
      <div class="field"><label for="order-notes">Order notes</label><textarea id="order-notes" name="notes" maxlength="4000" rows="3">${escapeHtml(existing?.notes || '')}</textarea></div>
      <div class="account-line"><button id="save-order" class="button button-primary" type="submit">${existing ? 'Save changes' : 'Create draft order'}</button><a class="button button-outline" href="${existing ? `#orders/${escapeHtml(existing.id)}` : '#orders'}">Cancel</a></div></form></section>`;
    const customerSelect = document.querySelector('#order-customer'); if (customerSelect) customerSelect.value = selectedCustomer;
    const locationSelect = document.querySelector('#order-location'); locationSelect.value = selectedLocation;
    if (!selectedDate) { const now = new Date(); document.querySelector('#order-promised').value = `${now.getFullYear()}-${String(now.getMonth()+1).padStart(2,'0')}-${String(now.getDate()).padStart(2,'0')}`; }
    document.querySelector('#add-garment-line').addEventListener('click', () => { const lines = document.querySelector('#garment-lines'); const index = lines.querySelectorAll('.garment-line').length; lines.insertAdjacentHTML('beforeend', garmentLineMarkup({}, index)); });
    document.querySelector('#garment-lines').addEventListener('click', (event) => { const remove = event.target.closest('[data-remove-garment]'); if (!remove) return; const lines = document.querySelectorAll('.garment-line'); if (lines.length <= 1) { showToast('An order must contain at least one garment.'); return; } remove.closest('.garment-line').remove(); });
    document.querySelector('#order-create-form').addEventListener('submit', async (event) => {
      event.preventDefault(); const form = event.currentTarget; const data = new FormData(form); const btn = document.querySelector('#save-order'); const err = document.querySelector('#order-form-error');
      const discount = Number(data.get('discount') || 0);
      if (!Number.isFinite(discount) || discount < 0) { err.innerHTML = '<p class="form-error" role="alert">Check the discount amount.</p>'; return; }
      const lineElements = [...document.querySelectorAll('.garment-line')];
      const invalidLine = lineElements.find((line) => { const read = (name) => line.querySelector(`[data-field="${name}"]`)?.value ?? ''; const qty = Number(read('quantity')); const price = Number(read('unitPrice')); return !read('name').trim() || !Number.isInteger(qty) || qty < 1 || qty > 10000 || !Number.isFinite(price) || price < 0; });
      if (invalidLine) { err.innerHTML = '<p class="form-error" role="alert">Each garment needs a name, a whole-number quantity above zero and a valid non-negative unit price.</p>'; return; }
      const garments = lineElements.map((line) => {
        const read = (name) => line.querySelector(`[data-field="${name}"]`)?.value ?? '';
        const quantity = Number(read('quantity')); const unitPrice = Number(read('unitPrice'));
        const originalId = line.dataset.garmentId;
        const original = existing?.garments?.find(g => g.id === originalId);
        return { ...(originalId ? { id: originalId } : {}), name: read('name').trim(), category: read('category').trim() || null, quantity, unitPrice, fabric: read('fabric').trim() || null, colour: read('colour').trim() || null, styleInstructions: read('styleInstructions').trim() || null, ...(original ? { measurementSetId: original.measurementSetId, measurementRevision: original.measurementRevision, designId: original.designId, priceOverrideReason: original.priceOverrideReason, alterationNotes: original.alterationNotes } : {}) };
      });
      const body = { locationId: String(data.get('locationId')), promisedDate: String(data.get('promisedDate')), priority: String(data.get('priority')), deliveryMethod: String(data.get('deliveryMethod')), deliveryAddress: String(data.get('deliveryAddress') || '').trim() || null, deliveryInstructions: String(data.get('deliveryInstructions') || '').trim() || null, notes: String(data.get('notes') || '').trim() || null, discount, garments };
      if (!existing) body.customerId = String(data.get('customerId'));
      if (existing) body.version = existing.version;
      btn.disabled = true; btn.textContent = existing ? 'Saving…' : 'Creating…'; err.innerHTML = '';
      try { const saved = await api(existing ? `/api/orders/${encodeURIComponent(existing.id)}` : '/api/orders', { method: existing ? 'PUT' : 'POST', headers: existing ? {} : { 'Idempotency-Key': crypto.randomUUID() }, body }); showToast(existing ? 'Order saved.' : 'Draft order created.'); location.hash = `orders/${encodeURIComponent(saved.id || existing?.id)}`; }
      catch (error) { err.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not save the order.')}</p>`; btn.disabled = false; btn.textContent = existing ? 'Save changes' : 'Create draft order'; }
    });
  } catch (error) { container.innerHTML = `<section class="card"><h2>Could not prepare order</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Could not load customers and locations.')}</p><a href="#orders">Back to orders</a></section>`; }
}

async function renderFittings(container) {
  if (!hasPermission('orders.view')) { container.innerHTML = '<section class="card"><h2>Fittings</h2><p class="form-error">Your account does not have permission to view fitting appointments.</p></section>'; return; }
  container.innerHTML = '<section class="card loading-card"><div class="spinner"></div><p>Loading fitting appointments…</p></section>';
  try {
    const [fittingsPayload, customersPayload, locations] = await Promise.all([
      api('/api/fittings?page=1&pageSize=100'),
      hasPermission('customers.view') ? api('/api/customers?page=1&pageSize=100&status=Active') : Promise.resolve({ items: [] }),
      hasPermission('fittings.manage') ? api('/api/locations') : Promise.resolve([])
    ]);
    const fittings = Array.isArray(fittingsPayload?.items) ? fittingsPayload.items : [];
    const customers = Array.isArray(customersPayload?.items) ? customersPayload.items : [];
    const canManage = hasPermission('fittings.manage');
    const customerOptions = customers.map((c) => `<option value="${escapeHtml(c.id)}">${escapeHtml(c.fullName)}</option>`).join('');
    const locationOptions = (locations || []).map((l) => `<option value="${escapeHtml(l.id)}">${escapeHtml(l.name)}</option>`).join('');
    const rows = fittings.map((f) => `<tr><td><strong>${escapeHtml(customers.find((c) => c.id === f.customerId)?.fullName || f.customerName || 'Customer')}</strong><small>${escapeHtml(f.notes || '')}</small></td><td>${escapeHtml(new Date(f.scheduledAtUtc).toLocaleString())}</td><td>${Number(f.durationMinutes) || 30} min</td><td><span class="pill">${escapeHtml(f.status)}</span></td><td>${canManage && f.status === 'Scheduled' ? `<select aria-label="Update fitting status" data-fitting-status="${escapeHtml(f.id)}"><option value="">Change status…</option><option>Completed</option><option>Cancelled</option><option>NoShow</option></select>` : '—'}</td></tr>`).join('');
    const createForm = canManage ? `<div class="card panel"><h3>Schedule a fitting</h3><form id="fitting-create-form"><div class="field"><label for="fitting-customer">Customer *</label><select id="fitting-customer" required>${customerOptions}</select></div><div class="field"><label for="fitting-location">Location *</label><select id="fitting-location" required>${locationOptions}</select></div><div class="field"><label for="fitting-date">Date and time *</label><input id="fitting-date" type="datetime-local" required></div><div class="field"><label for="fitting-duration">Duration (minutes)</label><input id="fitting-duration" type="number" min="5" max="480" step="5" value="30" required></div><div class="field"><label for="fitting-notes">Notes</label><textarea id="fitting-notes" maxlength="2000" rows="2"></textarea></div><button class="button button-primary" type="submit">Schedule fitting</button></form></div>` : '';
    container.innerHTML = `<section class="panel"><div class="panel-head"><div><p class="eyebrow">Appointments</p><h2>Fittings</h2><p class="lead">Schedule fittings and track whether the customer attended.</p></div></div>${createForm}<div class="card panel"><h3>Recent fitting appointments</h3><div class="table-wrap"><table class="data-table"><thead><tr><th>Customer</th><th>Scheduled</th><th>Duration</th><th>Status</th><th>Action</th></tr></thead><tbody>${rows || '<tr><td colspan="5">No fitting appointments returned.</td></tr>'}</tbody></table></div><p class="form-note">Showing up to 100 records. Permissions and schedule rules are enforced by the API.</p></div></section>`;
    document.querySelector('#fitting-create-form')?.addEventListener('submit', async (event) => {
      event.preventDefault(); const form = event.currentTarget; const button = form.querySelector('button[type="submit"]');
      const localDate = document.querySelector('#fitting-date').value; const date = new Date(localDate); const durationMinutes = Number(document.querySelector('#fitting-duration').value);
      if (!localDate || Number.isNaN(date.getTime()) || !Number.isInteger(durationMinutes) || durationMinutes < 5 || durationMinutes > 480) { showToast('Enter a valid fitting date and duration.'); return; }
      if (!customers.length || !locations?.length) { showToast('An active customer and workshop location are required.'); return; }
      button.disabled = true; button.textContent = 'Scheduling…';
      try { await api('/api/fittings', { method: 'POST', body: { customerId: document.querySelector('#fitting-customer').value, locationId: document.querySelector('#fitting-location').value, scheduledAt: date.toISOString(), durationMinutes, notes: document.querySelector('#fitting-notes').value.trim() || null } }); showToast('Fitting appointment scheduled.'); await renderFittings(container); }
      catch (error) { showToast(error.message || 'Could not schedule fitting.'); button.disabled = false; button.textContent = 'Schedule fitting'; }
    });
    document.querySelectorAll('[data-fitting-status]').forEach((select) => select.addEventListener('change', async () => {
      if (!select.value) return;
      const status = select.value; select.disabled = true;
      try { await api(`/api/fittings/${encodeURIComponent(select.dataset.fittingStatus)}/status`, { method: 'POST', body: { status } }); showToast('Fitting status updated.'); await renderFittings(container); }
      catch (error) { showToast(error.message || 'Could not update fitting status.'); select.disabled = false; select.value = ''; }
    }));
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not load fittings</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p></section>`;
  }
}

async function renderTaskBoard(container) {
  if (!hasPermission('tasks.view') && !hasPermission('tasks.work')) { container.innerHTML = '<section class="card"><h2>Daily work</h2><p class="form-error">Your account does not have permission to view tasks.</p></section>'; return; }
  const query = new URLSearchParams(location.hash.split('?')[1] || '');
  const managerView = hasPermission('tasks.view');
  const status = query.get(managerView ? 'status' : 'show') || '';
  const orderId = query.get('orderId') || '';
  const params = new URLSearchParams({ page: '1', pageSize: '100' }); if (status) params.set(managerView ? 'status' : 'show', status); if (orderId && managerView) params.set('orderId', orderId);
  container.innerHTML = `<section class="card panel"><div class="panel-head"><div><p class="eyebrow">Production and staff scheduling</p><h2>Daily work</h2><p>Tasks, assignments, deadlines and shop-wide planning.</p></div>${managerView ? '<div class="account-line"><a class="button button-outline button-fit" href="#tasks/workers">Worker schedules</a><a class="button button-outline button-fit" href="#tasks/schedule">Plan shop schedule</a></div>' : ''}</div><form id="task-filters" class="filter-form"><div class="field"><label for="task-status">${managerView ? 'Status' : 'Tasks to show'}</label><select id="task-status" name="status">${managerView ? `<option value="">All statuses</option>${['Pending','Scheduled','InProgress','Paused','Blocked','Completed','Cancelled'].map(v => `<option ${status===v?'selected':''}>${v}</option>`).join('')}` : `${[['open','Open tasks'],['completed','Completed tasks'],['all','All assigned tasks']].map(([v,label]) => `<option value="${v}" ${status===v?'selected':''}>${label}</option>`).join('')}`}</select></div><button class="button button-outline" type="submit">Filter</button></form><div id="task-results" class="loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading production tasks…</p></div></section>`;
  document.querySelector('#task-filters').addEventListener('submit', e => { e.preventDefault(); const val = e.currentTarget.elements.status.value; const next = new URLSearchParams(); if (val) next.set(managerView ? 'status' : 'show', val); if (orderId && managerView) next.set('orderId', orderId); location.hash = `tasks${next.size ? `?${next}` : ''}`; });
  try {
    const result = await api(`${managerView ? '/api/tasks' : '/api/my/tasks'}?${params.toString()}`);
    const rows = Array.isArray(result?.items) ? result.items : [];
    const workers = hasPermission('tasks.assign') ? await api('/api/workers').catch(() => []) : [];
    const table = rows.length ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Task</th><th>Order</th><th>Stage / status</th><th>Assignee</th><th>Deadline</th><th>Action</th></tr></thead><tbody>${rows.map(t => `<tr><td><a href="#tasks/${escapeHtml(t.id)}"><strong>${escapeHtml(t.title)}</strong></a><small>${escapeHtml(t.garmentName || '')}${t.requiredSkill ? ` · ${escapeHtml(t.requiredSkill)}` : ''}</small></td><td><a href="#orders/${escapeHtml(t.orderId)}">${escapeHtml(t.orderNumber || 'View order')}</a></td><td>${escapeHtml(t.stageName || '')}<small><span class="pill">${escapeHtml(t.status)}</span>${t.isOverdue ? ' <span class="pill">Overdue</span>' : ''}</small></td><td>${escapeHtml(t.assigneeName || 'Unassigned')}</td><td>${escapeHtml(dateLabel(t.deadlineUtc))}</td><td>${hasPermission('tasks.assign') && !['Completed','Cancelled'].includes(t.status) ? `<button class="button button-outline task-assign" data-task="${escapeHtml(t.id)}">Assign</button>` : '—'}</td></tr>`).join('')}</tbody></table></div>` : '<p class="empty-state">No production tasks match this view. Confirm an order to generate tasks, or check another filter.</p>';
    document.querySelector('#task-results').className = 'results-content'; document.querySelector('#task-results').innerHTML = `${table}<p class="form-note">${rows.length} task(s) loaded. Task permissions and workflow rules are enforced by the API.</p>`;
    document.querySelectorAll('.task-assign').forEach(btn => btn.addEventListener('click', async () => {
      const options = (Array.isArray(workers) ? workers : []).filter(w => w.isActive).map(w => `${w.membershipId}|${w.displayName}`).join('\n');
      if (!options) { showToast('No active workers are available to assign.'); return; }
      const choice = prompt(`Enter the worker membership ID to assign. Available workers:\n${options}`); if (choice === null) return;
      const membershipId = choice.trim(); if (!/^[0-9a-f-]{36}$/i.test(membershipId)) { showToast('Enter a valid worker membership ID.'); return; }
      try { await api(`/api/tasks/${encodeURIComponent(btn.dataset.task)}/assign`, { method: 'POST', body: { membershipId, override: false } }); showToast('Task assigned.'); renderTaskBoard(container); }
      catch (error) { showToast(error.message || 'Assignment failed.'); }
    }));
  } catch (error) { document.querySelector('#task-results').innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not load tasks.')}</p><button id="retry-tasks" class="button button-outline">Try again</button>`; document.querySelector('#retry-tasks')?.addEventListener('click', () => renderTaskBoard(container)); }
}

async function renderTaskDetail(container, taskId) {
  if (!/^[0-9a-f-]{36}$/i.test(taskId)) { container.innerHTML = '<section class="card"><h2>Task not found</h2><a href="#tasks">Back to daily work</a></section>'; return; }
  const manager = hasPermission('tasks.view');
  if (!manager && !hasPermission('tasks.work')) { container.innerHTML = '<section class="card"><h2>Task access</h2><p class="form-error">You cannot view this task.</p></section>'; return; }
  try {
    const task = await api(`${manager ? '/api/tasks' : '/api/my/tasks'}/${encodeURIComponent(taskId)}`);
    const t = task.task || task.Task || task;
    const checks = task.requiredChecks || task.RequiredChecks || [];
    container.innerHTML = `<section class="card panel"><p><a href="#tasks">← Back to daily work</a></p><p class="eyebrow">Production task</p><h2>${escapeHtml(t.title)}</h2><p><span class="pill">${escapeHtml(t.status)}</span> · ${escapeHtml(t.orderNumber || '')} · ${escapeHtml(t.garmentName || '')}</p><dl class="detail-list"><dt>Stage</dt><dd>${escapeHtml(t.stageName || '—')}</dd><dt>Assigned to</dt><dd>${escapeHtml(t.assigneeName || 'Unassigned')}</dd><dt>Deadline</dt><dd>${escapeHtml(dateLabel(t.deadlineUtc))}</dd><dt>Instructions</dt><dd>${escapeHtml(task.instructions || '—')}</dd><dt>Dependencies remaining</dt><dd>${Number(t.unmetDependencies) || 0}</dd></dl>${checks.length ? `<h3>Completion checklist</h3><ul>${checks.map(c => `<li>${escapeHtml(c)}</li>`).join('')}</ul>` : ''}<div id="task-detail-error"></div><div class="account-line">${manager && hasPermission('tasks.assign') && !['Completed','Cancelled'].includes(t.status) ? '<button class="button button-outline" id="task-detail-assign">Assign worker</button>' : ''}${hasPermission('tasks.work') && ['Scheduled','Pending'].includes(t.status) ? '<button class="button button-primary" id="task-start">Start task</button>' : ''}${hasPermission('tasks.work') && ['InProgress','Paused'].includes(t.status) ? '<button class="button button-primary" id="task-complete">Complete task</button>' : ''}${hasPermission('tasks.work') && ['Scheduled','Pending','InProgress'].includes(t.status) ? '<button class="button button-outline" id="task-issue">Report issue</button>' : ''}${manager && hasPermission('tasks.manage') && t.status === 'Blocked' ? '<button class="button button-outline" id="task-unblock">Unblock task</button>' : ''}${manager && hasPermission('tasks.manage') && !['Completed','Cancelled'].includes(t.status) ? '<button class="button button-outline" id="task-cancel">Cancel task</button>' : ''}${manager && hasPermission('tasks.override') && ['Scheduled','Pending'].includes(t.status) ? '<button class="button button-outline" id="task-override-start">Override start</button>' : ''}${hasPermission('inventory.manage') && hasPermission('inventory.view') && !['Completed','Cancelled'].includes(t.status) ? '<button class="button button-outline" id="task-materials-manage">Reserve / consume stock</button>' : ''}</div><div id="task-materials-area" class="stack"></div></section>`;
    const perform = async (url, body) => { try { await api(url, { method: 'POST', ...(body ? { body } : {}) }); showToast('Task updated.'); renderTaskDetail(container, taskId); } catch (error) { document.querySelector('#task-detail-error').innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; } };
    document.querySelector('#task-start')?.addEventListener('click', () => perform(`/api/my/tasks/${taskId}/start`));
    document.querySelector('#task-complete')?.addEventListener('click', () => { const notes = prompt('Completion notes (optional):') || ''; perform(`/api/my/tasks/${taskId}/complete`, { notes, completedChecks: checks }); });
    document.querySelector('#task-issue')?.addEventListener('click', () => { const description = prompt('Describe the issue (at least 3 characters):'); if (description && description.trim().length >= 3) perform(`/api/my/tasks/${taskId}/issues`, { type: 'Other', description: description.trim() }); });
    document.querySelector('#task-unblock')?.addEventListener('click', () => { const note = prompt('What changed so this task can resume?') || ''; perform(`/api/tasks/${taskId}/unblock`, { note }); });
    document.querySelector('#task-cancel')?.addEventListener('click', () => { const reason = prompt('Reason for cancelling this task (at least 3 characters):'); if (reason && reason.trim().length >= 3) perform(`/api/tasks/${taskId}/cancel`, { reason: reason.trim() }); });
    document.querySelector('#task-override-start')?.addEventListener('click', () => { const reason = prompt('Audited override reason (at least 5 characters):'); if (reason && reason.trim().length >= 5) perform(`/api/tasks/${taskId}/override`, { action: 'Start', reason: reason.trim() }); });
    document.querySelector('#task-detail-assign')?.addEventListener('click', async () => { const workers = await api('/api/workers').catch(() => []); const choice = prompt(`Worker membership ID:\n${workers.filter(w=>w.isActive).map(w=>`${w.membershipId} — ${w.displayName}`).join('\n')}`); if (choice && /^[0-9a-f-]{36}$/i.test(choice.trim())) perform(`/api/tasks/${taskId}/assign`, { membershipId: choice.trim(), override: false }); });
    document.querySelector('#task-materials-manage')?.addEventListener('click', async () => {
      const area = document.querySelector('#task-materials-area'); area.innerHTML = '<p>Loading available stock…</p>';
      try {
        const materials = await api('/api/inventory');
        if (!Array.isArray(materials) || !materials.length) { area.innerHTML = '<p>No stock materials are visible at your permitted locations.</p>'; return; }
        area.innerHTML = `<div class="card"><h3>Task material reservations</h3><p>Choose stock and quantities. Reserve before consuming; the API checks available stock, task status, location, and existing reservations.</p><div class="table-wrap"><table><thead><tr><th>Select</th><th>Material</th><th>Available</th><th>Quantity</th></tr></thead><tbody>${materials.map((m) => `<tr><td><input type="checkbox" data-task-material-check="${escapeHtml(m.id)}" aria-label="Select ${escapeHtml(m.name)}"></td><td>${escapeHtml(m.name)} <small>${escapeHtml(m.sku)} · ${escapeHtml(m.unit)}</small></td><td>${escapeHtml(m.available)}</td><td><input type="number" min="0.001" step="0.001" value="1" data-task-material-quantity="${escapeHtml(m.id)}" aria-label="Quantity for ${escapeHtml(m.name)}"></td></tr>`).join('')}</tbody></table></div><div class="field"><label for="task-material-note">Note (optional)</label><input id="task-material-note" maxlength="500"></div><div class="account-line"><button id="task-material-reserve" class="button button-primary" type="button">Reserve selected</button><button id="task-material-release" class="button button-outline" type="button">Release selected</button><button id="task-material-consume" class="button button-outline" type="button">Consume selected</button><button id="task-material-cancel" class="button button-outline" type="button">Close</button></div><p id="task-material-error" class="form-error" role="alert"></p></div>`;
        area.querySelector('#task-material-cancel').addEventListener('click', () => { area.innerHTML = ''; });
        const submitMaterials = async (kind) => {
          const errorBox = area.querySelector('#task-material-error');
          const selected = [...area.querySelectorAll('[data-task-material-check]:checked')].map((check) => {
            const id = check.dataset.taskMaterialCheck; const input = area.querySelector(`[data-task-material-quantity="${id}"]`); return { materialId: id, quantity: Number(input?.value) };
          });
          if (!selected.length || selected.some((m) => !Number.isFinite(m.quantity) || m.quantity <= 0)) { errorBox.textContent = 'Select at least one material and enter a positive quantity for each.'; return; }
          if (!confirm(`${kind === 'reserve' ? 'Reserve' : 'Consume'} the selected stock for this task?`)) return;
          const button = area.querySelector(kind === 'reserve' ? '#task-material-reserve' : kind === 'release' ? '#task-material-release' : '#task-material-consume'); button.disabled = true; errorBox.textContent = '';
          try { await api(`/api/inventory/tasks/${encodeURIComponent(taskId)}/${kind}`, { method: 'POST', body: { materials: selected, note: area.querySelector('#task-material-note').value.trim() || null } }); showToast(kind === 'reserve' ? 'Stock reserved for this task.' : kind === 'release' ? 'Task stock reservation released.' : 'Reserved stock consumed for this task.'); await renderTaskDetail(container, taskId); }
          catch (error) { errorBox.textContent = error.message; button.disabled = false; }
        };
        area.querySelector('#task-material-reserve').addEventListener('click', () => submitMaterials('reserve'));
        area.querySelector('#task-material-release').addEventListener('click', () => submitMaterials('release'));
        area.querySelector('#task-material-consume').addEventListener('click', () => submitMaterials('consume'));
      } catch (error) { area.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; }
    });
  } catch (error) { container.innerHTML = `<section class="card"><h2>Could not load task</h2><p class="form-error">${escapeHtml(error.message)}</p><a href="#tasks">Back to daily work</a></section>`; }
}

async function renderShopSchedule(container) {
  if (!hasPermission('tasks.view')) { container.innerHTML = '<section class="card"><h2>Shop schedule</h2><p class="form-error">Your account cannot view the shop schedule.</p></section>'; return; }
  const start = new Date(); start.setHours(0,0,0,0); const startValue = `${start.getFullYear()}-${String(start.getMonth()+1).padStart(2,'0')}-${String(start.getDate()).padStart(2,'0')}`;
  container.innerHTML = `<section class="card panel"><p><a href="#tasks">← Back to daily work</a></p><p class="eyebrow">Capacity-aware planning</p><h2>Shop-wide schedule</h2><p>Preview assigns work using the server's worker availability, skills, dependencies and capacity. Applying a plan revalidates task versions on the server.</p><form id="schedule-form"><div class="field"><label for="schedule-start">Start date</label><input id="schedule-start" name="start" type="date" required value="${startValue}"></div><div class="field"><label for="schedule-days">Planning horizon (days)</label><input id="schedule-days" name="days" type="number" min="1" max="366" value="14" required></div><button class="button button-primary" type="submit">Preview schedule</button></form><div id="schedule-results"></div></section>`;
  document.querySelector('#schedule-form').addEventListener('submit', async e => { e.preventDefault(); const data = new FormData(e.currentTarget); const startUtc = new Date(`${data.get('start')}T00:00:00`).toISOString(); const horizonDays = Number(data.get('days')); const target = document.querySelector('#schedule-results'); target.innerHTML = '<p>Calculating a fresh schedule preview…</p>';
    try { const preview = await api('/api/tasks/schedule/preview', { method: 'POST', body: { startUtc, horizonDays } }); const scheduled = preview.scheduled || []; const unscheduled = preview.unscheduled || []; target.innerHTML = `<h3>Preview result</h3><p>${scheduled.length} task(s) scheduled · ${unscheduled.length} unscheduled · ${escapeHtml(preview.timeZoneId || '')}</p>${unscheduled.length ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Task ID</th><th>Reason</th></tr></thead><tbody>${unscheduled.map(x=>`<tr><td>${escapeHtml(x.taskId)}</td><td>${escapeHtml(x.reason)}</td></tr>`).join('')}</tbody></table></div>` : ''}<div class="table-wrap"><table class="data-table"><thead><tr><th>Task ID</th><th>Start</th><th>End</th></tr></thead><tbody>${scheduled.map(x=>`<tr><td>${escapeHtml(x.taskId)}</td><td>${escapeHtml(new Date(x.startUtc).toLocaleString())}</td><td>${escapeHtml(new Date(x.endUtc).toLocaleString())}</td></tr>`).join('') || '<tr><td colspan="3">No tasks scheduled.</td></tr>'}</tbody></table></div>${hasPermission('tasks.manage') && scheduled.length && !unscheduled.length ? '<button class="button button-primary" id="apply-schedule">Apply this complete schedule</button>' : ''}<p class="form-note">This is a preview. No task planned times are written until you explicitly apply it.</p>`;
      document.querySelector('#apply-schedule')?.addEventListener('click', async () => { if (!confirm('Apply this schedule? The server will revalidate the plan before saving.')) return; try { const result = await api('/api/tasks/schedule/apply', { method: 'POST', body: { startUtc, horizonDays, expectedTaskVersions: preview.taskVersions, expectedSlots: scheduled } }); showToast(`Schedule applied to ${result.updatedTasks} tasks. Notifications queued: ${result.notificationsQueued ? 'yes' : 'no'}.`); } catch (error) { target.insertAdjacentHTML('afterbegin', `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`); } });
    } catch (error) { target.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Schedule preview failed.')}</p>`; }
  });
}

async function renderWorkerSchedules(container) {
  if (!hasPermission('tasks.view')) { container.innerHTML = '<section class="card"><h2>Worker schedules</h2><p class="form-error">Your account cannot view worker schedules.</p></section>'; return; }
  container.innerHTML = '<section class="card loading-card"><div class="spinner"></div><p>Loading active workers…</p></section>';
  try {
    const workers = await api('/api/workers');
    const active = (Array.isArray(workers) ? workers : []).filter(w => w.isActive);
    if (!active.length) { container.innerHTML = '<section class="card panel"><p><a href="#tasks">← Back to daily work</a></p><h2>Worker schedules</h2><p class="empty-state">No active workers found. Add team members and ensure they have an active worker membership.</p></section>'; return; }
    container.innerHTML = `<section class="card panel"><p><a href="#tasks">← Back to daily work</a></p><p class="eyebrow">Individual availability</p><h2>Worker schedules</h2><p>Configure each worker's recurring availability, review leave, and inspect their scheduled tasks.</p><div class="field"><label for="schedule-worker">Worker</label><select id="schedule-worker">${active.map(w => `<option value="${escapeHtml(w.membershipId)}">${escapeHtml(w.displayName)}</option>`).join('')}</select></div><div id="worker-schedule-detail"><p>Select a worker to load their schedule.</p></div></section>`;
    const select = document.querySelector('#schedule-worker');
    const load = async () => {
      const id = select.value; const target = document.querySelector('#worker-schedule-detail'); target.innerHTML = '<p>Loading availability, leave and calendar…</p>';
      try {
        const from = new Date(); from.setHours(0,0,0,0); const to = new Date(from); to.setDate(to.getDate()+14);
        const qs = `fromUtc=${encodeURIComponent(from.toISOString())}&toUtc=${encodeURIComponent(to.toISOString())}`;
        const [availability, leave, calendar] = await Promise.all([api(`/api/workers/${id}/schedule/availability`), api(`/api/workers/${id}/schedule/leave?${qs}`), api(`/api/workers/${id}/schedule/calendar?${qs}`)]);
        const days = ['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday']; const dayMap = new Map((availability.days || []).map(d => [Number(d.dayOfWeek), d]));
        const dayFields = days.map((day, i) => { const d = dayMap.get(i) || { dayOfWeek:i, isAvailable:i>0 && i<6, opensAt:'09:00', closesAt:'17:00', capacityMinutes:i>0 && i<6 ? 480 : 0 }; return `<tr><td>${day}</td><td><input aria-label="${day} available" type="checkbox" data-day="${i}" data-part="isAvailable" ${d.isAvailable?'checked':''}></td><td><input aria-label="${day} opens" type="time" data-day="${i}" data-part="opensAt" value="${escapeHtml(String(d.opensAt || '09:00').slice(0,5))}"></td><td><input aria-label="${day} closes" type="time" data-day="${i}" data-part="closesAt" value="${escapeHtml(String(d.closesAt || '17:00').slice(0,5))}"></td><td><input aria-label="${day} capacity minutes" type="number" min="0" max="1440" data-day="${i}" data-part="capacityMinutes" value="${Number(d.capacityMinutes)||0}"></td></tr>`; }).join('');
        const leaveRows = (leave || []).map(l => `<tr><td>${escapeHtml(dateLabel(l.startsAtUtc))}</td><td>${escapeHtml(dateLabel(l.endsAtUtc))}</td><td>${escapeHtml(l.reason)}</td><td><span class="pill">${escapeHtml(l.status)}</span></td><td>${hasPermission('workers.manage') && l.status === 'Pending' ? `<button class="button button-outline" data-leave-review="approve" data-leave="${escapeHtml(l.id)}">Approve</button> <button class="button button-outline" data-leave-review="reject" data-leave="${escapeHtml(l.id)}">Reject</button>` : '—'}</td></tr>`).join('');
        const calendarRows = (calendar || []).map(c => `<tr><td>${escapeHtml(c.title)}</td><td><a href="#orders/${escapeHtml(c.orderId)}">Open order</a></td><td>${escapeHtml(new Date(c.startUtc).toLocaleString())}</td><td>${escapeHtml(new Date(c.endUtc).toLocaleString())}</td><td><span class="pill">${escapeHtml(c.status)}</span></td></tr>`).join('');
        target.innerHTML = `<h3>${escapeHtml(active.find(w=>w.membershipId===id)?.displayName || 'Worker')} · recurring availability</h3><p>${availability.isConfigured ? 'Availability configured' : 'Default availability is in use until saved.'}</p>${hasPermission('workers.manage') ? '<button class="button button-outline" id="worker-edit-skills" type="button">Edit skills</button>' : ''}<form id="worker-availability-form"><div class="table-wrap"><table class="data-table"><thead><tr><th>Day</th><th>Available</th><th>Opens</th><th>Closes</th><th>Capacity (min)</th></tr></thead><tbody>${dayFields}</tbody></table></div>${hasPermission('workers.manage') ? '<button class="button button-primary" type="submit">Save availability</button>' : '<p class="form-note">You can view availability but cannot change it.</p>'}</form><hr><h3>Leave</h3>${hasPermission('workers.manage') ? `<form id="worker-leave-form"><div class="field"><label for="leave-start">Starts</label><input id="leave-start" type="datetime-local" required></div><div class="field"><label for="leave-end">Ends</label><input id="leave-end" type="datetime-local" required></div><div class="field"><label for="leave-reason">Reason</label><input id="leave-reason" maxlength="300" minlength="2" required></div><button class="button button-outline" type="submit">Record leave</button></form>` : ''}<div class="table-wrap"><table class="data-table"><thead><tr><th>From</th><th>To</th><th>Reason</th><th>Status</th><th>Review</th></tr></thead><tbody>${leaveRows || '<tr><td colspan="5">No leave records in the next 14 days.</td></tr>'}</tbody></table></div><hr><h3>Scheduled work · next 14 days</h3><div class="table-wrap"><table class="data-table"><thead><tr><th>Task</th><th>Order</th><th>Start</th><th>End</th><th>Status</th></tr></thead><tbody>${calendarRows || '<tr><td colspan="5">No scheduled tasks found.</td></tr>'}</tbody></table></div><div id="worker-schedule-error"></div>`;
        document.querySelector('#worker-edit-skills')?.addEventListener('click', async () => { const worker = active.find(w => w.membershipId === id); const raw = prompt('Enter this worker’s skills separated by commas:', (worker?.skills || []).join(', ')); if (raw === null) return; const skills = [...new Set(raw.split(',').map(v => v.trim()).filter(Boolean))]; if (skills.some(v => v.length > 60) || skills.length > 30) { showToast('Use up to 30 skills, each no longer than 60 characters.'); return; } try { await api(`/api/workers/${id}/skills`, { method:'PUT', body:{ skills } }); showToast('Worker skills updated.'); const refreshed = await api('/api/workers'); const selected = refreshed.find(w => w.membershipId === id); if (selected) active.splice(active.findIndex(w=>w.membershipId===id), 1, selected); load(); } catch(error) { document.querySelector('#worker-schedule-error').innerHTML=`<p class="form-error">${escapeHtml(error.message)}</p>`; } });
        document.querySelector('#worker-availability-form')?.addEventListener('submit', async e => { e.preventDefault(); const days = [...Array(7)].map((_, i) => { const read = part => document.querySelector(`[data-day="${i}"][data-part="${part}"]`); return { dayOfWeek:i, isAvailable:read('isAvailable').checked, opensAt:read('opensAt').value || null, closesAt:read('closesAt').value || null, capacityMinutes:Number(read('capacityMinutes').value) }; }); const err = document.querySelector('#worker-schedule-error'); if (days.some(d=>d.capacityMinutes<0 || d.capacityMinutes>1440 || !Number.isInteger(d.capacityMinutes))) { err.innerHTML='<p class="form-error">Daily capacity must be 0–1440 whole minutes.</p>'; return; } try { await api(`/api/workers/${id}/schedule/availability`, { method:'PUT', body:{ days } }); showToast('Worker availability saved.'); load(); } catch(error) { err.innerHTML=`<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; } });
        document.querySelector('#worker-leave-form')?.addEventListener('submit', async e => { e.preventDefault(); const startAt = new Date(document.querySelector('#leave-start').value); const endAt = new Date(document.querySelector('#leave-end').value); const reason = document.querySelector('#leave-reason').value.trim(); const err = document.querySelector('#worker-schedule-error'); if (!(startAt < endAt) || !reason) { err.innerHTML='<p class="form-error">Enter a valid leave period and reason.</p>'; return; } try { await api(`/api/workers/${id}/schedule/leave`, { method:'POST', body:{ startsAtUtc:startAt.toISOString(), endsAtUtc:endAt.toISOString(), reason } }); showToast('Leave record added.'); load(); } catch(error) { err.innerHTML=`<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; } });
        document.querySelectorAll('[data-leave-review]').forEach(btn => btn.addEventListener('click', async () => { const note = prompt('Review note (optional):') || ''; try { await api(`/api/workers/${id}/schedule/leave-requests/${btn.dataset.leave}/review`, { method:'POST', body:{ approve:btn.dataset.leaveReview==='approve', note } }); showToast('Leave request reviewed.'); load(); } catch(error) { document.querySelector('#worker-schedule-error').innerHTML=`<p class="form-error">${escapeHtml(error.message)}</p>`; } }));
      } catch (error) { target.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not load worker schedule.')}</p>`; }
    };
    select.addEventListener('change', load); await load();
  } catch (error) { container.innerHTML = `<section class="card"><h2>Could not load workers</h2><p class="form-error">${escapeHtml(error.message || 'Try again.')}</p></section>`; }
}




async function renderExpenses(container) {
  const canView = hasPermission('expenses.view'); const canManage = hasPermission('expenses.manage');
  if (!canView) { container.innerHTML = '<section class="card"><h2>Expense access required</h2><p>Your account cannot view expense records.</p></section>'; return; }
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading expenses…</p></section>';
  const results = await Promise.allSettled([api('/api/expenses?page=1&pageSize=100'), api('/api/expense-categories'), api('/api/expenses/summary')]);
  const expenses = results[0].status === 'fulfilled' ? (results[0].value.items || []) : [];
  const allCategories = results[1].status === 'fulfilled' ? results[1].value : [];
  const categories = allCategories.filter((x) => x.isActive);
  const summary = results[2].status === 'fulfilled' ? results[2].value : null;
  const errorMarkup = results.map((r, i) => r.status === 'rejected' ? `<p class="form-error" role="alert">${['Expense list','Categories','Expense summary'][i]}: ${escapeHtml(r.reason.message)}</p>` : '').join('');
  container.innerHTML = `<section class="panel"><div class="card"><p class="eyebrow">Money out</p><h2>Expenses</h2><p>Expenses are separate from revenue and receivables. Voided entries remain in the audit trail.</p>${errorMarkup}<div class="account-line"><span class="pill">Recorded total: ${summary ? `${escapeHtml(summary.total)} ${escapeHtml(summary.currencyCode)}` : 'Unavailable'}</span><span class="pill">Categories: ${summary && Array.isArray(summary.byCategory) ? summary.byCategory.length : '—'}</span></div>${canManage ? '<div class="account-line"><button id="expense-add" class="button button-primary" type="button">Record expense</button><button id="expense-categories-manage" class="button button-outline" type="button">Manage categories</button></div>' : ''}<a class="button button-outline" href="/backend/api/expenses/export" target="_blank" rel="noopener">Export CSV</a></div><div id="expense-form-area" class="card"></div><div id="expense-category-area" class="card" hidden></div><div class="card"><h3>Expense records</h3><div class="table-wrap"><table><thead><tr><th>Date</th><th>Category</th><th>Description</th><th>Vendor</th><th>Amount</th><th>Status</th><th>Action</th></tr></thead><tbody>${expenses.map((e) => `<tr><td>${escapeHtml(e.expenseDate)}</td><td>${escapeHtml(e.categoryName)}</td><td>${escapeHtml(e.description)}</td><td>${escapeHtml(e.vendor || '—')}</td><td>${escapeHtml(e.amount)} ${escapeHtml(e.currencyCode)}</td><td>${e.isVoid ? `Voided: ${escapeHtml(e.voidReason || '')}` : 'Recorded'}</td><td>${canManage && !e.isVoid ? `<button class="button button-small button-outline" type="button" data-expense-void="${escapeHtml(e.id)}">Void</button>` : '—'}</td></tr>`).join('') || '<tr><td colspan="7">No expenses were returned.</td></tr>'}</tbody></table></div></div></section>`;
  const area = container.querySelector('#expense-form-area');
  const categoryArea = container.querySelector('#expense-category-area');
  container.querySelector('#expense-categories-manage')?.addEventListener('click', () => {
    categoryArea.hidden = false;
    categoryArea.innerHTML = `<div class="section-heading"><div><h3>Expense categories</h3><p>Categories with existing records are deactivated rather than deleted.</p></div><button id="expense-category-close" class="button button-outline" type="button">Close</button></div><form id="expense-category-create" class="form-grid"><div class="field"><label for="expense-category-name">New category name</label><input id="expense-category-name" maxlength="80" minlength="2" required></div><div class="field"><label for="expense-category-sort">Sort order</label><input id="expense-category-sort" type="number" min="0" max="10000" value="100" required></div><button class="button button-primary" type="submit">Create category</button><p id="expense-category-error" class="form-error" role="alert"></p></form><div class="table-wrap"><table><thead><tr><th>Category</th><th>Sort</th><th>Status</th><th>Actions</th></tr></thead><tbody>${allCategories.map((c) => `<tr><td>${escapeHtml(c.name)}</td><td>${escapeHtml(c.sortOrder)}</td><td>${c.isActive ? 'Active' : 'Inactive'}</td><td><button class="button button-small button-outline" type="button" data-expense-category-edit="${escapeHtml(c.id)}">Edit</button></td></tr>`).join('') || '<tr><td colspan="4">No categories.</td></tr>'}</tbody></table></div>`;
    categoryArea.querySelector('#expense-category-close').addEventListener('click', () => { categoryArea.hidden = true; categoryArea.innerHTML = ''; });
    categoryArea.querySelector('#expense-category-create').addEventListener('submit', async (event) => {
      event.preventDefault(); const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); const name = form.querySelector('#expense-category-name').value.trim(); const sortOrder = Number(form.querySelector('#expense-category-sort').value); const error = form.querySelector('#expense-category-error');
      if (name.length < 2 || !Number.isInteger(sortOrder) || sortOrder < 0 || sortOrder > 10000) { error.textContent = 'Enter a name of at least two characters and a valid sort order.'; return; }
      submit.disabled = true;
      try { await api('/api/expense-categories', { method: 'POST', body: { name, isActive: true, sortOrder } }); showToast('Expense category created.'); await renderExpenses(container); }
      catch (e) { error.textContent = e.message; submit.disabled = false; }
    });
    categoryArea.querySelectorAll('[data-expense-category-edit]').forEach((button) => button.addEventListener('click', async () => {
      const category = allCategories.find((c) => c.id === button.dataset.expenseCategoryEdit); if (!category) return;
      const name = prompt('Category name:', category.name); if (name === null) return; const cleanName = name.trim(); if (cleanName.length < 2 || cleanName.length > 80) { showToast('Enter a category name between 2 and 80 characters.'); return; }
      const activeChoice = prompt(`Should this category be active? Enter yes or no. Current: ${category.isActive ? 'yes' : 'no'}`, category.isActive ? 'yes' : 'no'); if (activeChoice === null) return;
      const normalized = activeChoice.trim().toLowerCase(); if (!['yes', 'no'].includes(normalized)) { showToast('Enter yes or no.'); return; }
      const sortText = prompt('Sort order (whole number 0–10000):', String(category.sortOrder)); if (sortText === null) return; const sortOrder = Number(sortText); if (!Number.isInteger(sortOrder) || sortOrder < 0 || sortOrder > 10000) { showToast('Enter a valid sort order.'); return; }
      button.disabled = true;
      try { await api(`/api/expense-categories/${encodeURIComponent(category.id)}`, { method: 'PUT', body: { name: cleanName, isActive: normalized === 'yes', sortOrder } }); showToast('Expense category updated.'); await renderExpenses(container); }
      catch (e) { showToast(e.message); button.disabled = false; }
    }));
  });
  container.querySelector('#expense-add')?.addEventListener('click', () => {
    if (!categories.length) { area.innerHTML = '<p class="form-error" role="alert">No active expense categories are available. An authorized manager must create or activate a category first.</p>'; return; }
    const now = new Date(); const today = `${now.getFullYear()}-${String(now.getMonth()+1).padStart(2,'0')}-${String(now.getDate()).padStart(2,'0')}`;
    area.innerHTML = `<h3>Record expense</h3><form id="expense-form" class="form-grid"><div class="field"><label for="expense-category">Category</label><select id="expense-category" required>${categories.map((c) => `<option value="${escapeHtml(c.id)}">${escapeHtml(c.name)}</option>`).join('')}</select></div><div class="field"><label for="expense-date">Date</label><input id="expense-date" type="date" value="${today}" required></div><div class="field"><label for="expense-amount">Amount</label><input id="expense-amount" type="number" min="0.01" step="0.01" required></div><div class="field"><label for="expense-description">Description</label><input id="expense-description" maxlength="300" required></div><div class="field"><label for="expense-vendor">Vendor (optional)</label><input id="expense-vendor" maxlength="120"></div><div class="field"><label for="expense-reference">Reference (optional)</label><input id="expense-reference" maxlength="100"></div><div class="form-actions"><button class="button button-primary" type="submit">Save expense</button><button class="button button-outline" id="expense-cancel" type="button">Cancel</button></div><p id="expense-error" class="form-error" role="alert"></p></form>`;
    area.querySelector('#expense-cancel').addEventListener('click', () => { area.innerHTML = ''; });
    area.querySelector('#expense-form').addEventListener('submit', async (event) => { event.preventDefault(); const form = event.currentTarget; const button = form.querySelector('[type="submit"]'); button.disabled = true; const amount = Number(form.querySelector('#expense-amount').value); if (!Number.isFinite(amount) || amount <= 0) { form.querySelector('#expense-error').textContent = 'Enter an amount greater than zero.'; button.disabled = false; return; } try { await api('/api/expenses', { method: 'POST', body: { categoryId: form.querySelector('#expense-category').value, expenseDate: form.querySelector('#expense-date').value, amount, description: form.querySelector('#expense-description').value.trim(), vendor: form.querySelector('#expense-vendor').value.trim() || null, reference: form.querySelector('#expense-reference').value.trim() || null } }); showToast('Expense recorded.'); await renderExpenses(container); } catch (error) { form.querySelector('#expense-error').textContent = error.message; button.disabled = false; } });
  });
  container.querySelectorAll('[data-expense-void]').forEach((button) => button.addEventListener('click', async () => { const reason = prompt('Reason for voiding this expense (required):'); if (!reason || reason.trim().length < 3) return; if (!confirm('Void this expense? The record will be retained for audit.')) return; button.disabled = true; try { await api(`/api/expenses/${encodeURIComponent(button.dataset.expenseVoid)}/void`, { method: 'POST', body: { reason: reason.trim() } }); showToast('Expense voided; the audit record was retained.'); await renderExpenses(container); } catch (error) { showToast(error.message); button.disabled = false; } }));
}

async function renderFinance(container, invoiceId = null) {
  const canViewInvoices = hasPermission('invoices.view');
  const canViewPayments = hasPermission('payments.view');
  const canRecordPayments = hasPermission('payments.record');
  const canVerifyPayments = hasPermission('payments.verify');
  if (!canViewInvoices && !canViewPayments) {
    container.innerHTML = '<section class="card"><h2>Finance access required</h2><p>Your account does not have permission to view invoices or payments.</p></section>';
    return;
  }
  if (invoiceId && canViewInvoices) {
    container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading invoice…</p></section>';
    try {
      const invoice = await api(`/api/invoices/${encodeURIComponent(invoiceId)}`);
      const payments = Array.isArray(invoice.payments) ? invoice.payments : [];
      const paymentRows = payments.map((p) => `<tr><td>${escapeHtml(p.receivedOn || '—')}</td><td>${escapeHtml(p.method)}</td><td>${escapeHtml(p.amount)} ${escapeHtml(invoice.currencyCode)}</td><td>${escapeHtml(p.status)}</td><td>${escapeHtml(p.reference || '—')}</td><td>${p.receiptId && p.status === 'Verified' ? `<button class="button button-small button-outline" type="button" data-receipt-view="${escapeHtml(p.receiptId)}">View receipt</button> ` : ''}${p.status === 'Pending' && canVerifyPayments ? `<button class="button button-small button-primary" type="button" data-payment-verify="${escapeHtml(p.id)}">Verify</button> <button class="button button-small button-outline" type="button" data-payment-reject="${escapeHtml(p.id)}">Reject</button>` : (p.receiptId && p.status === 'Verified' ? '' : '—')}</td></tr>`).join('');
      container.innerHTML = `<section class="panel"><p><a href="#finance">← Back to finance</a></p><div class="card"><p class="eyebrow">${escapeHtml(invoice.kind)} invoice</p><h2>${escapeHtml(invoice.number || 'Draft invoice')}</h2><p>${escapeHtml(invoice.customerName)} · ${escapeHtml(invoice.status)}</p><div class="account-line"><span class="pill">Total: ${escapeHtml(invoice.total)} ${escapeHtml(invoice.currencyCode)}</span><span class="pill">Paid: ${escapeHtml(invoice.amountPaid)}</span><span class="pill">Balance: ${escapeHtml(invoice.balance)}</span></div><p>Issue date: ${escapeHtml(invoice.issueDate || 'Not issued')} · Due date: ${escapeHtml(invoice.dueDate || 'Not set')}</p><p>${escapeHtml(invoice.notes || '')}</p><div class="account-line">${invoice.status === 'Draft' && hasPermission('invoices.manage') ? '<button class="button button-outline" id="invoice-edit-draft" type="button">Edit draft</button><button class="button button-outline" id="invoice-delete-draft" type="button">Delete draft</button><button class="button button-primary" id="invoice-issue" type="button">Issue invoice</button>' : ''}${invoice.status === 'Issued' && hasPermission('invoices.manage') ? '<button class="button button-outline" id="invoice-void" type="button">Void invoice</button>' : ''}${invoice.status === 'Issued' && canRecordPayments && Number(invoice.balance) > 0 ? '<button class="button button-primary" id="payment-add" type="button">Record payment</button>' : ''}${invoice.status === 'Issued' && hasPermission('invoices.manage') && Number(invoice.balance) > 0 ? '<button class="button button-outline" id="credit-note-add" type="button">Add credit note</button>' : ''}</div><div id="finance-action" class="stack"></div></div><div class="card"><h3>Credit notes</h3><div class="table-wrap"><table><thead><tr><th>Number</th><th>Amount</th><th>Reason</th><th>Created</th></tr></thead><tbody>${(invoice.creditNotes || []).map((note) => `<tr><td>${escapeHtml(note.number)}</td><td>${escapeHtml(note.amount)} ${escapeHtml(invoice.currencyCode)}</td><td>${escapeHtml(note.reason)}</td><td>${escapeHtml(note.createdAtUtc || '—')}</td></tr>`).join('') || '<tr><td colspan="4">No credit notes issued.</td></tr>'}</tbody></table></div></div><div class="card"><h3>Invoice lines</h3><div class="table-wrap"><table><thead><tr><th>Description</th><th>Qty</th><th>Unit price</th><th>Total</th></tr></thead><tbody>${(invoice.lines || []).map((line) => `<tr><td>${escapeHtml(line.description)}</td><td>${escapeHtml(line.quantity)}</td><td>${escapeHtml(line.unitPrice)}</td><td>${escapeHtml(line.lineTotal)}</td></tr>`).join('') || '<tr><td colspan="4">No lines were returned.</td></tr>'}</tbody></table></div></div><div class="card"><h3>Payments</h3><div class="table-wrap"><table><thead><tr><th>Date</th><th>Method</th><th>Amount</th><th>Status</th><th>Reference</th><th>Actions</th></tr></thead><tbody>${paymentRows || '<tr><td colspan="6">No payments recorded.</td></tr>'}</tbody></table></div></div></section>`;
      container.querySelector('#invoice-edit-draft')?.addEventListener('click', () => {
        const action = container.querySelector('#finance-action');
        let lineIndex = 0;
        const lineMarkup = (line = {}) => {
          const index = lineIndex++;
          return `<fieldset class="card" data-draft-line data-index="${index}"><legend>Invoice line ${index + 1}</legend><div class="field"><label>Description</label><input data-line-field="description" maxlength="200" required value="${escapeHtml(line.description || '')}"></div><div class="field"><label>Quantity</label><input data-line-field="quantity" type="number" min="0.01" step="0.01" required value="${escapeHtml(line.quantity ?? 1)}"></div><div class="field"><label>Unit price</label><input data-line-field="unitPrice" type="number" min="0" step="0.01" required value="${escapeHtml(line.unitPrice ?? 0)}"></div><div class="field"><label>Line discount</label><input data-line-field="discount" type="number" min="0" step="0.01" required value="${escapeHtml(line.discount ?? 0)}"></div><div class="field"><label>Tax percent</label><input data-line-field="taxPercent" type="number" min="0" max="100" step="0.01" required value="${escapeHtml(line.taxPercent ?? 0)}"></div><button class="button button-small button-outline" type="button" data-draft-line-remove>Remove line</button></fieldset>`;
        };
        action.innerHTML = `<h3>Edit draft invoice</h3><p class="form-note">The server recalculates totals and checks the current invoice version. Order-billed invoice lines are locked; only due date, notes and terms can be edited.</p><form id="invoice-draft-form" class="form-grid"><div class="field"><label for="draft-due-date">Due date</label><input id="draft-due-date" type="date" value="${escapeHtml(invoice.dueDate || '')}"></div><div class="field"><label for="draft-notes">Notes</label><textarea id="draft-notes" maxlength="2000">${escapeHtml(invoice.notes || '')}</textarea></div><div class="field"><label for="draft-terms">Terms</label><textarea id="draft-terms" maxlength="2000">${escapeHtml(invoice.terms || '')}</textarea></div>${invoice.orderId ? '' : `<div id="invoice-draft-lines">${(invoice.lines || []).map((line) => lineMarkup(line)).join('')}</div><button id="invoice-draft-add-line" class="button button-outline" type="button">Add line</button>`}<div class="form-actions"><button class="button button-primary" type="submit">Save draft</button><button class="button button-outline" id="invoice-draft-cancel" type="button">Cancel</button></div><p id="invoice-draft-error" class="form-error" role="alert"></p></form>`;
        action.querySelector('#invoice-draft-cancel').addEventListener('click', () => { action.innerHTML = ''; });
        action.querySelector('#invoice-draft-add-line')?.addEventListener('click', () => { action.querySelector('#invoice-draft-lines').insertAdjacentHTML('beforeend', lineMarkup({ quantity: 1, unitPrice: 0, discount: 0, taxPercent: 0 })); });
        action.querySelector('#invoice-draft-lines')?.addEventListener('click', (event) => {
          const remove = event.target.closest('[data-draft-line-remove]'); if (!remove) return;
          const rows = action.querySelectorAll('[data-draft-line]'); if (rows.length <= 1) { action.querySelector('#invoice-draft-error').textContent = 'An invoice must contain at least one line.'; return; }
          remove.closest('[data-draft-line]').remove();
        });
        action.querySelector('#invoice-draft-form').addEventListener('submit', async (event) => {
          event.preventDefault(); const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); const errorBox = form.querySelector('#invoice-draft-error'); submit.disabled = true; errorBox.textContent = '';
          const body = { version: Number(invoice.version), dueDate: form.querySelector('#draft-due-date').value || null, notes: form.querySelector('#draft-notes').value.trim() || null, terms: form.querySelector('#draft-terms').value.trim() || null };
          if (!invoice.orderId) {
            const rows = [...action.querySelectorAll('[data-draft-line]')];
            body.lines = rows.map((row) => ({ description: row.querySelector('[data-line-field="description"]').value.trim(), quantity: Number(row.querySelector('[data-line-field="quantity"]').value), unitPrice: Number(row.querySelector('[data-line-field="unitPrice"]').value), discount: Number(row.querySelector('[data-line-field="discount"]').value), taxPercent: Number(row.querySelector('[data-line-field="taxPercent"]').value) }));
            if (!body.lines.length || body.lines.some((line) => !line.description || line.description.length > 200 || !Number.isFinite(line.quantity) || line.quantity <= 0 || !Number.isFinite(line.unitPrice) || line.unitPrice < 0 || !Number.isFinite(line.discount) || line.discount < 0 || line.discount > line.quantity * line.unitPrice || !Number.isFinite(line.taxPercent) || line.taxPercent < 0 || line.taxPercent > 100)) { errorBox.textContent = 'Check every line: descriptions are required, quantities must be positive, prices/discounts valid, and tax must be 0–100%.'; submit.disabled = false; return; }
          }
          try { await api(`/api/invoices/${encodeURIComponent(invoice.id)}`, { method: 'PUT', body }); showToast('Draft invoice updated.'); await renderFinance(container, invoice.id); }
          catch (error) { errorBox.textContent = error.message; submit.disabled = false; }
        });
      });
      container.querySelector('#invoice-delete-draft')?.addEventListener('click', async (event) => { const button = event.currentTarget; if (!confirm('Delete this draft invoice? This is only allowed before it is issued and has no payment records.')) return; button.disabled = true; try { await api(`/api/invoices/${encodeURIComponent(invoice.id)}`, { method: 'DELETE' }); showToast('Draft invoice deleted.'); await renderFinance(container); } catch (error) { showToast(error.message); button.disabled = false; } });
      container.querySelector('#invoice-void')?.addEventListener('click', async (event) => { const button = event.currentTarget; const reason = prompt('Reason for voiding this invoice (at least 3 characters):'); if (!reason || reason.trim().length < 3) return; if (!confirm('Void this invoice? The server will reject this if payment or credit-note rules prevent it.')) return; button.disabled = true; try { await api(`/api/invoices/${encodeURIComponent(invoice.id)}/void`, { method: 'POST', body: { reason: reason.trim() } }); showToast('Invoice voided; the audit trail was retained.'); await renderFinance(container, invoice.id); } catch (error) { showToast(error.message); button.disabled = false; } });
      container.querySelector('#invoice-issue')?.addEventListener('click', async (event) => { const button = event.currentTarget; if (!confirm('Issue this invoice? The issued document is frozen and gets an invoice number.')) return; button.disabled = true; try { await api(`/api/invoices/${encodeURIComponent(invoice.id)}/issue`, { method: 'POST' }); showToast('Invoice issued.'); await renderFinance(container, invoice.id); } catch (error) { showToast(error.message); button.disabled = false; } });
      container.querySelector('#credit-note-add')?.addEventListener('click', () => {
        const action = container.querySelector('#finance-action');
        action.innerHTML = `<h3>Add credit note</h3><p class="form-note">A credit note reduces the unpaid invoice balance. The server rejects credits above the remaining balance.</p><form id="credit-note-form" class="form-grid"><div class="field"><label for="credit-note-amount">Credit amount (${escapeHtml(invoice.currencyCode)})</label><input id="credit-note-amount" type="number" min="0.01" max="${escapeHtml(invoice.balance)}" step="0.01" required></div><div class="field"><label for="credit-note-reason">Reason</label><textarea id="credit-note-reason" minlength="3" maxlength="500" required></textarea></div><div class="form-actions"><button class="button button-primary" type="submit">Issue credit note</button><button class="button button-outline" id="credit-note-cancel" type="button">Cancel</button></div><p id="credit-note-error" class="form-error" role="alert"></p></form>`;
        action.querySelector('#credit-note-cancel').addEventListener('click', () => { action.innerHTML = ''; });
        action.querySelector('#credit-note-form').addEventListener('submit', async (event) => {
          event.preventDefault(); const form = event.currentTarget; const button = form.querySelector('[type="submit"]'); button.disabled = true;
          const amount = Number(form.querySelector('#credit-note-amount').value); const reason = form.querySelector('#credit-note-reason').value.trim();
          if (!Number.isFinite(amount) || amount <= 0 || amount > Number(invoice.balance) || Math.round(amount * 100) !== amount * 100 || reason.length < 3) { form.querySelector('#credit-note-error').textContent = 'Enter a valid amount within the outstanding balance and a reason of at least three characters.'; button.disabled = false; return; }
          try { await api(`/api/invoices/${encodeURIComponent(invoice.id)}/credit-notes`, { method: 'POST', body: { amount, reason } }); showToast('Credit note issued.'); await renderFinance(container, invoice.id); }
          catch (error) { form.querySelector('#credit-note-error').textContent = error.message; button.disabled = false; }
        });
      });
      container.querySelectorAll('[data-receipt-view]').forEach((button) => button.addEventListener('click', async () => {
        const action = container.querySelector('#finance-action'); action.innerHTML = '<p class="form-note">Loading receipt…</p>'; button.disabled = true;
        try { const receipt = await api(`/api/receipts/${encodeURIComponent(button.dataset.receiptView)}`); action.innerHTML = `<div class="card"><h3>Receipt ${escapeHtml(receipt.number)}</h3><p>Customer: ${escapeHtml(receipt.customerName)}</p><p>Invoice: ${escapeHtml(receipt.invoiceNumber || receipt.invoiceId)}</p><p>Amount received: ${escapeHtml(receipt.amount)} ${escapeHtml(receipt.currencyCode)}</p><p>Method: ${escapeHtml(receipt.method)} · Date: ${escapeHtml(receipt.issuedAtUtc)}</p><p>Reference: ${escapeHtml(receipt.reference || '—')}</p><p>Balance after payment: ${escapeHtml(receipt.balanceAfter)} ${escapeHtml(receipt.currencyCode)}</p>${receipt.isVoid ? '<p class="form-error">This receipt is void.</p>' : ''}<button class="button button-outline" id="receipt-close" type="button">Close receipt</button></div>`; action.querySelector('#receipt-close').addEventListener('click', () => { action.innerHTML = ''; }); }
        catch (error) { action.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; } finally { button.disabled = false; }
      }));
      container.querySelector('#payment-add')?.addEventListener('click', () => {
        const action = container.querySelector('#finance-action'); action.innerHTML = `<h3>Record payment</h3><form id="payment-form" class="form-grid"><div class="field"><label for="payment-amount">Amount (${escapeHtml(invoice.currencyCode)})</label><input id="payment-amount" type="number" min="0.01" max="${escapeHtml(invoice.balance)}" step="0.01" required></div><div class="field"><label for="payment-method">Method</label><select id="payment-method"><option>Cash</option><option>BankTransfer</option><option>Card</option><option>MobileMoney</option><option>Other</option></select></div><div class="field"><label for="payment-date">Received on</label><input id="payment-date" type="date" value="${new Date().getFullYear()}-${String(new Date().getMonth()+1).padStart(2,'0')}-${String(new Date().getDate()).padStart(2,'0')}" required></div><div class="field"><label for="payment-reference">Reference (optional)</label><input id="payment-reference" maxlength="100"></div><div class="field"><label for="payment-notes">Notes (optional)</label><input id="payment-notes" maxlength="500"></div>${canVerifyPayments ? '<label class="checkbox-line"><input id="payment-verify-now" type="checkbox"> Verify immediately (only if funds are confirmed)</label>' : '<p class="form-note">This payment will remain pending until an authorized staff member verifies it.</p>'}<div class="form-actions"><button class="button button-primary" type="submit">Save payment</button><button class="button button-outline" id="payment-cancel" type="button">Cancel</button></div><p id="payment-error" class="form-error" role="alert"></p></form>`;
        action.querySelector('#payment-cancel').addEventListener('click', () => { action.innerHTML = ''; });
        action.querySelector('#payment-form').addEventListener('submit', async (event) => { event.preventDefault(); const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); submit.disabled = true; const amount = Number(form.querySelector('#payment-amount').value); if (!Number.isFinite(amount) || amount <= 0 || amount > Number(invoice.balance)) { form.querySelector('#payment-error').textContent = 'Enter an amount above zero and no greater than the current balance.'; submit.disabled = false; return; } try { await api(`/api/invoices/${encodeURIComponent(invoice.id)}/payments`, { method: 'POST', headers: { 'idempotency-key': crypto.randomUUID() }, body: { amount, method: form.querySelector('#payment-method').value, receivedOn: form.querySelector('#payment-date').value, reference: form.querySelector('#payment-reference').value.trim() || null, notes: form.querySelector('#payment-notes').value.trim() || null, verifyNow: Boolean(form.querySelector('#payment-verify-now')?.checked) } }); showToast('Payment recorded. A pending payment does not count toward the paid balance until verified.'); await renderFinance(container, invoice.id); } catch (error) { form.querySelector('#payment-error').textContent = error.message; submit.disabled = false; } });
      });
      container.querySelectorAll('[data-payment-verify]').forEach((button) => button.addEventListener('click', async () => { if (!confirm('Verify this payment? Only verify funds that have actually been received.')) return; button.disabled = true; try { await api(`/api/payments/${encodeURIComponent(button.dataset.paymentVerify)}/verify`, { method: 'POST' }); showToast('Payment verified.'); await renderFinance(container, invoice.id); } catch (error) { showToast(error.message); button.disabled = false; } }));
      container.querySelectorAll('[data-payment-reject]').forEach((button) => button.addEventListener('click', async () => { const reason = prompt('Why is this payment being rejected?'); if (!reason || reason.trim().length < 3) return; button.disabled = true; try { await api(`/api/payments/${encodeURIComponent(button.dataset.paymentReject)}/reject`, { method: 'POST', body: { reason: reason.trim() } }); showToast('Payment rejected.'); await renderFinance(container, invoice.id); } catch (error) { showToast(error.message); button.disabled = false; } }));
    } catch (error) { container.innerHTML = `<section class="card"><h2>Invoice could not load</h2><p class="form-error" role="alert">${escapeHtml(error.message)}</p><a href="#finance">Back to finance</a></section>`; }
    return;
  }
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading finance records…</p></section>';
  const results = await Promise.allSettled([canViewInvoices ? api('/api/invoices?page=1&pageSize=50') : Promise.resolve(null), canViewPayments ? api('/api/payments?page=1&pageSize=50&status=Pending') : Promise.resolve(null)]);
  const invoicesResult = results[0]; const paymentsResult = results[1];
  const invoices = invoicesResult.status === 'fulfilled' && invoicesResult.value ? (invoicesResult.value.items || []) : [];
  const pendingPayments = paymentsResult.status === 'fulfilled' && paymentsResult.value ? (paymentsResult.value.items || []) : [];
  container.innerHTML = `<section class="panel"><div class="card"><p class="eyebrow">Finance</p><h2>Invoices and payments</h2><p>Balances count verified payments only. Pending claims do not reduce the outstanding balance.</p>${invoicesResult.status === 'rejected' ? `<p class="form-error" role="alert">Invoices: ${escapeHtml(invoicesResult.reason.message)}</p>` : ''}${paymentsResult.status === 'rejected' ? `<p class="form-error" role="alert">Payments: ${escapeHtml(paymentsResult.reason.message)}</p>` : ''}${canViewInvoices && hasPermission('invoices.manage') ? `<div class="account-line">${hasPermission('customers.view') ? '<button id="invoice-create" class="button button-primary" type="button">New invoice</button>' : ''}${hasPermission('orders.view') ? '<button id="invoice-from-order" class="button button-outline" type="button">Invoice from order</button>' : ''}</div>` : ''}</div><div id="invoice-create-area" class="card" hidden></div>${canViewInvoices ? `<div class="card"><h3>Invoices</h3><div class="table-wrap"><table><thead><tr><th>Invoice</th><th>Customer</th><th>Status</th><th>Total</th><th>Balance</th><th>Due</th><th></th></tr></thead><tbody>${invoices.map((i) => `<tr><td>${escapeHtml(i.number || 'Draft')}</td><td>${escapeHtml(i.customerName)}</td><td>${escapeHtml(i.status)}${i.isOverdue ? ' · Overdue' : ''}</td><td>${escapeHtml(i.total)} ${escapeHtml(i.currencyCode)}</td><td>${escapeHtml(i.balance)}</td><td>${escapeHtml(i.dueDate || '—')}</td><td><a class="button button-small button-outline" href="#finance/${encodeURIComponent(i.id)}">Open</a></td></tr>`).join('') || '<tr><td colspan="7">No invoices were returned.</td></tr>'}</tbody></table></div></div>` : ''}${canViewPayments ? `<div class="card"><h3>Pending payments</h3><div class="table-wrap"><table><thead><tr><th>Invoice</th><th>Amount</th><th>Method</th><th>Received</th><th>Status</th><th></th></tr></thead><tbody>${pendingPayments.map((p) => `<tr><td>${escapeHtml(p.invoiceNumber || p.invoiceId)}</td><td>${escapeHtml(p.amount)} ${escapeHtml(p.currencyCode)}</td><td>${escapeHtml(p.method)}</td><td>${escapeHtml(p.receivedOn)}</td><td>${escapeHtml(p.status)}</td><td>${canViewInvoices ? `<a class="button button-small button-outline" href="#finance/${encodeURIComponent(p.invoiceId)}">Open invoice</a>` : '—'}</td></tr>`).join('') || '<tr><td colspan="6">No pending payments were returned.</td></tr>'}</tbody></table></div></div>` : ''}</section>`;
  const createArea = container.querySelector('#invoice-create-area');
  const closeCreateArea = () => { createArea.hidden = true; createArea.innerHTML = ''; };
  container.querySelector('#invoice-create')?.addEventListener('click', async () => {
    createArea.hidden = false; createArea.innerHTML = '<p>Loading customers…</p>';
    try {
      const response = await api('/api/customers?page=1&pageSize=100');
      const customers = (response.items || []).filter((customer) => !customer.status || customer.status === 'Active');
      if (!customers.length) { createArea.innerHTML = '<p class="form-error">Create an active customer before creating an invoice.</p><button class="button button-outline" id="invoice-create-close" type="button">Close</button>'; createArea.querySelector('#invoice-create-close').addEventListener('click', closeCreateArea); return; }
      let lineIndex = 0;
      const lineMarkup = (line = {}) => { const index = lineIndex++; return `<fieldset class="card" data-create-line><legend>Line ${index + 1}</legend><div class="field"><label>Description</label><input data-create-line-field="description" maxlength="200" required value="${escapeHtml(line.description || '')}"></div><div class="field"><label>Quantity</label><input data-create-line-field="quantity" type="number" min="0.01" step="0.01" required value="${escapeHtml(line.quantity ?? 1)}"></div><div class="field"><label>Unit price</label><input data-create-line-field="unitPrice" type="number" min="0" step="0.01" required value="${escapeHtml(line.unitPrice ?? 0)}"></div><div class="field"><label>Discount</label><input data-create-line-field="discount" type="number" min="0" step="0.01" required value="${escapeHtml(line.discount ?? 0)}"></div><div class="field"><label>Tax override % (blank uses invoice default)</label><input data-create-line-field="taxPercent" type="number" min="0" max="100" step="0.01" value="${escapeHtml(line.taxPercent ?? '')}"></div><button class="button button-small button-outline" type="button" data-create-line-remove>Remove line</button></fieldset>`; };
      createArea.innerHTML = `<div class="section-heading"><div><h3>Create invoice</h3><p>Use this for services and other free-form charges. Totals are recalculated by the API.</p></div><button class="button button-outline" id="invoice-create-close" type="button">Close</button></div><form id="invoice-create-form" class="form-grid"><div class="field"><label for="create-invoice-customer">Customer</label><select id="create-invoice-customer" required>${customers.map((c) => `<option value="${escapeHtml(c.id)}">${escapeHtml(c.fullName || c.displayName || c.name || 'Customer')} ${c.status && c.status !== 'Active' ? `(${escapeHtml(c.status)})` : ''}</option>`).join('')}</select></div><div class="field"><label for="create-invoice-due">Due date</label><input id="create-invoice-due" type="date"></div><div class="field"><label for="create-invoice-tax">Default tax % (optional)</label><input id="create-invoice-tax" type="number" min="0" max="100" step="0.01"></div><div class="field"><label for="create-invoice-notes">Notes</label><textarea id="create-invoice-notes" maxlength="2000"></textarea></div><div class="field"><label for="create-invoice-terms">Terms</label><textarea id="create-invoice-terms" maxlength="2000"></textarea></div><div id="create-invoice-lines">${lineMarkup({ quantity: 1, unitPrice: 0, discount: 0 })}</div><button id="create-invoice-add-line" class="button button-outline" type="button">Add line</button><div class="form-actions"><button class="button button-primary" type="submit">Create draft invoice</button></div><p id="create-invoice-error" class="form-error" role="alert"></p></form>`;
      createArea.querySelector('#invoice-create-close').addEventListener('click', closeCreateArea);
      createArea.querySelector('#create-invoice-add-line').addEventListener('click', () => createArea.querySelector('#create-invoice-lines').insertAdjacentHTML('beforeend', lineMarkup({ quantity: 1, unitPrice: 0, discount: 0 })));
      createArea.querySelector('#create-invoice-lines').addEventListener('click', (event) => { const remove = event.target.closest('[data-create-line-remove]'); if (!remove) return; if (createArea.querySelectorAll('[data-create-line]').length <= 1) { createArea.querySelector('#create-invoice-error').textContent = 'An invoice must contain at least one line.'; return; } remove.closest('[data-create-line]').remove(); });
      createArea.querySelector('#invoice-create-form').addEventListener('submit', async (event) => {
        event.preventDefault(); const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); const error = form.querySelector('#create-invoice-error'); submit.disabled = true; error.textContent = '';
        const taxRaw = form.querySelector('#create-invoice-tax').value;
        const lines = [...createArea.querySelectorAll('[data-create-line]')].map((row) => { const value = (name) => row.querySelector(`[data-create-line-field="${name}"]`).value; const tax = value('taxPercent'); return { description: value('description').trim(), quantity: Number(value('quantity')), unitPrice: Number(value('unitPrice')), discount: Number(value('discount')), taxPercent: tax === '' ? null : Number(tax) }; });
        if (!lines.length || lines.some((line) => !line.description || line.description.length > 200 || !Number.isFinite(line.quantity) || line.quantity <= 0 || !Number.isFinite(line.unitPrice) || line.unitPrice < 0 || !Number.isFinite(line.discount) || line.discount < 0 || line.discount > line.quantity * line.unitPrice || (line.taxPercent !== null && (!Number.isFinite(line.taxPercent) || line.taxPercent < 0 || line.taxPercent > 100))) || (taxRaw !== '' && (!Number.isFinite(Number(taxRaw)) || Number(taxRaw) < 0 || Number(taxRaw) > 100))) { error.textContent = 'Check descriptions, positive quantities, prices, discounts, and tax values before saving.'; submit.disabled = false; return; }
        const body = { customerId: form.querySelector('#create-invoice-customer').value, lines, dueDate: form.querySelector('#create-invoice-due').value || null, taxPercent: taxRaw === '' ? null : Number(taxRaw), notes: form.querySelector('#create-invoice-notes').value.trim() || null, terms: form.querySelector('#create-invoice-terms').value.trim() || null };
        try { const invoice = await api('/api/invoices', { method: 'POST', headers: { 'idempotency-key': crypto.randomUUID() }, body }); showToast('Draft invoice created.'); await renderFinance(container, invoice.id); }
        catch (error) { form.querySelector('#create-invoice-error').textContent = error.message; submit.disabled = false; }
      });
    } catch (error) { createArea.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p><button class="button button-outline" id="invoice-create-close" type="button">Close</button>`; createArea.querySelector('#invoice-create-close').addEventListener('click', closeCreateArea); }
  });
  container.querySelector('#invoice-from-order')?.addEventListener('click', async () => {
    createArea.hidden = false; createArea.innerHTML = '<p>Loading billable orders…</p>';
    try {
      const response = await api('/api/orders?page=1&pageSize=100');
      const orders = (response.items || []).filter((order) => ['Confirmed', 'InProduction', 'Ready', 'Delivered'].includes(order.status));
      if (!orders.length) { createArea.innerHTML = '<p class="form-note">No billable orders were returned. Confirm an order first, or use a free-form invoice for services.</p><button class="button button-outline" id="invoice-order-close" type="button">Close</button>'; createArea.querySelector('#invoice-order-close').addEventListener('click', closeCreateArea); return; }
      createArea.innerHTML = `<div class="section-heading"><div><h3>Invoice from order</h3><p>Choose Full for a whole-order invoice, Deposit for a partial payment request, or Balance for the remaining unbilled amount.</p></div><button class="button button-outline" id="invoice-order-close" type="button">Close</button></div><form id="invoice-order-form" class="form-grid"><div class="field"><label for="invoice-order-select">Order</label><select id="invoice-order-select" required>${orders.map((o) => `<option value="${escapeHtml(o.id)}">${escapeHtml(o.orderNumber)} · ${escapeHtml(o.customerName)} · ${escapeHtml(o.status)} · ${escapeHtml(o.total)} ${escapeHtml(o.currencyCode)}</option>`).join('')}</select></div><div class="field"><label for="invoice-order-mode">Invoice mode</label><select id="invoice-order-mode"><option value="Full">Full order</option><option value="Deposit">Deposit</option><option value="Balance">Remaining balance</option></select></div><div class="field"><label for="invoice-order-deposit-amount">Deposit amount (use instead of percent)</label><input id="invoice-order-deposit-amount" type="number" min="0.01" step="0.01"></div><div class="field"><label for="invoice-order-deposit-percent">Deposit percent (use instead of amount)</label><input id="invoice-order-deposit-percent" type="number" min="0.01" max="99.99" step="0.01"></div><div class="field"><label for="invoice-order-due">Due date</label><input id="invoice-order-due" type="date"></div><div class="field"><label for="invoice-order-tax">Tax percent (optional)</label><input id="invoice-order-tax" type="number" min="0" max="100" step="0.01"></div><div class="field"><label for="invoice-order-notes">Notes</label><textarea id="invoice-order-notes" maxlength="2000"></textarea></div><div class="field"><label for="invoice-order-terms">Terms</label><textarea id="invoice-order-terms" maxlength="2000"></textarea></div><button class="button button-primary" type="submit">Create order invoice</button><p id="invoice-order-error" class="form-error" role="alert"></p></form>`;
      createArea.querySelector('#invoice-order-close').addEventListener('click', closeCreateArea);
      createArea.querySelector('#invoice-order-form').addEventListener('submit', async (event) => {
        event.preventDefault(); const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); const error = form.querySelector('#invoice-order-error'); const mode = form.querySelector('#invoice-order-mode').value; const amountRaw = form.querySelector('#invoice-order-deposit-amount').value; const percentRaw = form.querySelector('#invoice-order-deposit-percent').value; const taxRaw = form.querySelector('#invoice-order-tax').value;
        if (mode === 'Deposit' && ((amountRaw === '') === (percentRaw === ''))) { error.textContent = 'For a deposit, enter either an amount or a percentage, but not both.'; return; }
        if (mode !== 'Deposit' && (amountRaw !== '' || percentRaw !== '')) { error.textContent = 'Deposit amount and percentage are only used for Deposit mode.'; return; }
        const body = { mode, dueDate: form.querySelector('#invoice-order-due').value || null, taxPercent: taxRaw === '' ? null : Number(taxRaw), notes: form.querySelector('#invoice-order-notes').value.trim() || null, terms: form.querySelector('#invoice-order-terms').value.trim() || null, depositAmount: mode === 'Deposit' && amountRaw !== '' ? Number(amountRaw) : null, depositPercent: mode === 'Deposit' && percentRaw !== '' ? Number(percentRaw) : null };
        if (body.taxPercent !== null && (!Number.isFinite(body.taxPercent) || body.taxPercent < 0 || body.taxPercent > 100)) { error.textContent = 'Tax must be between 0 and 100 percent.'; return; }
        if ((body.depositAmount !== null && (!Number.isFinite(body.depositAmount) || body.depositAmount <= 0)) || (body.depositPercent !== null && (!Number.isFinite(body.depositPercent) || body.depositPercent <= 0 || body.depositPercent >= 100))) { error.textContent = 'Enter a valid deposit amount or percentage.'; return; }
        submit.disabled = true; error.textContent = '';
        try { const orderId = form.querySelector('#invoice-order-select').value; const invoice = await api(`/api/invoices/from-order/${encodeURIComponent(orderId)}`, { method: 'POST', headers: { 'idempotency-key': crypto.randomUUID() }, body }); showToast('Draft order invoice created.'); await renderFinance(container, invoice.id); }
        catch (e) { error.textContent = e.message; submit.disabled = false; }
      });
    } catch (error) { createArea.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p><button class="button button-outline" id="invoice-order-close" type="button">Close</button>`; createArea.querySelector('#invoice-order-close').addEventListener('click', closeCreateArea); }
  });
}

async function renderInventory(container) {
  if (!hasPermission('inventory.view')) {
    container.innerHTML = '<section class="card"><h2>Inventory access required</h2><p>Your account does not have permission to view stock records.</p></section>';
    return;
  }
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading inventory…</p></section>';
  let materials; let inventoryLocations = [];
  try { [materials, inventoryLocations] = await Promise.all([api('/api/inventory'), api('/api/inventory/locations')]); }
  catch (error) {
    container.innerHTML = `<section class="card"><h2>Inventory could not load</h2><p class="form-error" role="alert">${escapeHtml(error.message)}</p><button class="button button-outline" id="inventory-retry" type="button">Try again</button></section>`;
    container.querySelector('#inventory-retry').addEventListener('click', () => renderInventory(container));
    return;
  }
  if (!Array.isArray(materials)) materials = [];
  const canManage = hasPermission('inventory.manage');
  const rows = materials.map((m) => `<tr><td><strong>${escapeHtml(m.name)}</strong><small>${escapeHtml(m.sku)}</small></td><td>${escapeHtml(m.unit)}</td><td>${escapeHtml(m.onHand)}</td><td>${escapeHtml(m.reserved)}</td><td>${escapeHtml(m.available)}</td><td>${escapeHtml(m.reorderLevel)}</td><td><span class="pill ${m.lowStock ? 'pill-warning' : ''}">${m.lowStock ? 'Low stock' : 'In stock'}</span></td><td><button class="button button-small button-outline" type="button" data-stock-history="${escapeHtml(m.id)}">History</button>${canManage ? ` <button class="button button-small button-outline" type="button" data-stock-change="${escapeHtml(m.id)}" data-stock-version="${escapeHtml(m.version)}" data-stock-name="${escapeHtml(m.name)}">Update stock</button>` : ''}</td></tr>`).join('');
  container.innerHTML = `<section class="card"><div class="section-heading"><div><p class="eyebrow">Materials and stock</p><h2>Inventory</h2><p>Available stock excludes quantities reserved for production tasks.</p></div>${canManage ? '<div class="account-line"><button class="button button-primary" id="inventory-add" type="button">Add material</button><button class="button button-outline" id="inventory-import" type="button">Import CSV</button></div>' : ''}</div><div class="table-wrap"><table><thead><tr><th>Material</th><th>Unit</th><th>On hand</th><th>Reserved</th><th>Available</th><th>Reorder at</th><th>Status</th><th>Actions</th></tr></thead><tbody>${rows || '<tr><td colspan="8">No inventory records were returned.</td></tr>'}</tbody></table></div><p class="form-note">Stock figures come from the business API. Low-stock status is calculated by the server.</p><div id="inventory-action" class="stack"></div></section>`;
  const action = container.querySelector('#inventory-action');
  container.querySelector('#inventory-import')?.addEventListener('click', () => {
    action.innerHTML = `<h3>Import inventory CSV</h3><p>Required columns: <code>sku,name,unit,openingStock,reorderLevel</code>. Optional column: <code>locationId</code> for unrestricted owners; location-scoped staff must include an allowed location ID on every row. The API validates the file and prevents duplicate SKUs.</p>${inventoryLocations.length ? `<details><summary>Allowed location IDs for this import</summary><ul>${inventoryLocations.map((location) => `<li><strong>${escapeHtml(location.name)}</strong>: <code>${escapeHtml(location.id)}</code></li>`).join('')}</ul></details>` : '<p class="form-note">No active location IDs were returned. Restricted users must ask an owner to assign a workshop location before importing stock.</p>'}<form id="inventory-import-form" class="form-grid"><div class="field"><label for="inventory-csv">CSV file (UTF-8, max 5 MB)</label><input id="inventory-csv" type="file" accept=".csv,text/csv" required></div><div class="form-actions"><button class="button button-primary" type="submit">Preview CSV</button><button class="button button-outline" id="inventory-import-cancel" type="button">Cancel</button></div><p id="inventory-import-error" class="form-error" role="alert"></p></form><div id="inventory-import-preview" class="stack"></div>`;
    action.querySelector('#inventory-import-cancel').addEventListener('click', () => { action.innerHTML = ''; });
    action.querySelector('#inventory-import-form').addEventListener('submit', async (event) => {
      event.preventDefault();
      const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); const file = form.querySelector('#inventory-csv').files?.[0];
      const errorBox = form.querySelector('#inventory-import-error'); const previewArea = action.querySelector('#inventory-import-preview');
      if (!file || file.size === 0 || file.size > 5 * 1024 * 1024 || !file.name.toLowerCase().endsWith('.csv')) { errorBox.textContent = 'Choose a non-empty CSV file no larger than 5 MB.'; return; }
      submit.disabled = true; errorBox.textContent = ''; previewArea.innerHTML = '<p>Validating CSV…</p>';
      try {
        const data = new FormData(); data.append('file', file, file.name);
        const preview = await api('/api/inventory/import/preview', { method: 'POST', body: data });
        const problems = Array.isArray(preview.problems) ? preview.problems : [];
        previewArea.innerHTML = `<div class="card"><h4>Import preview</h4><div class="account-line"><span class="pill">Rows: ${escapeHtml(preview.totalRows)}</span><span class="pill">Ready to create: ${escapeHtml(preview.validRows)}</span><span class="pill">Duplicates: ${escapeHtml(preview.duplicateRows)}</span><span class="pill">Invalid: ${escapeHtml(preview.invalidRows)}</span></div>${problems.length ? `<div class="table-wrap"><table><thead><tr><th>Row</th><th>SKU</th><th>Name</th><th>Issue</th></tr></thead><tbody>${problems.map((row) => `<tr><td>${escapeHtml(row.row)}</td><td>${escapeHtml(row.sku || '—')}</td><td>${escapeHtml(row.name || '—')}</td><td>${escapeHtml(row.message || row.outcome)}</td></tr>`).join('')}</tbody></table></div>` : '<p>No duplicate or invalid rows were reported.</p>'}<label class="check-row"><input id="inventory-skip-invalid" type="checkbox"> Skip invalid rows and import the valid rows</label><p id="inventory-import-commit-error" class="form-error" role="alert"></p><button id="inventory-import-commit" class="button button-primary" type="button" ${Number(preview.validRows) < 1 ? 'disabled' : ''}>Import ${escapeHtml(preview.validRows)} materials</button></div>`;
        previewArea.querySelector('#inventory-import-commit')?.addEventListener('click', async () => {
          if (Number(preview.invalidRows) > 0 && !previewArea.querySelector('#inventory-skip-invalid').checked) { previewArea.querySelector('#inventory-import-commit-error').textContent = 'Check the skip-invalid option or correct the CSV before importing.'; return; }
          if (!confirm(`Import ${preview.validRows} new materials? Existing or repeated SKUs will be skipped. This creates stock records and opening-stock ledger entries.`)) return;
          const commit = previewArea.querySelector('#inventory-import-commit'); commit.disabled = true; previewArea.querySelector('#inventory-import-commit-error').textContent = 'Importing…';
          try {
            const payload = new FormData(); payload.append('file', file, file.name); payload.append('expectedHash', preview.fileHash); payload.append('skipInvalidRows', String(previewArea.querySelector('#inventory-skip-invalid').checked));
            const result = await api('/api/inventory/import/commit', { method: 'POST', body: payload });
            showToast(`Inventory import finished: ${result.created} created, ${result.skipped} skipped, ${result.failed} failed.`); await renderInventory(container);
          } catch (error) { previewArea.querySelector('#inventory-import-commit-error').textContent = error.message; commit.disabled = false; }
        });
      } catch (error) { previewArea.innerHTML = ''; errorBox.textContent = error.message; }
      finally { submit.disabled = false; }
    });
  });
  container.querySelector('#inventory-add')?.addEventListener('click', () => {
    action.innerHTML = `<h3>Add material</h3><form id="inventory-create" class="form-grid"><div class="field"><label for="material-sku">SKU</label><input id="material-sku" maxlength="64" required></div><div class="field"><label for="material-name">Material name</label><input id="material-name" maxlength="160" required></div><div class="field"><label for="material-unit">Unit (e.g. m, yd, pcs)</label><input id="material-unit" maxlength="24" value="m" required></div><div class="field"><label for="material-location">Workshop location</label><select id="material-location" ${inventoryLocations.length ? 'required' : ''}><option value="">${inventoryLocations.length ? 'Choose a location' : 'No active locations available'}</option>${inventoryLocations.map((location) => `<option value="${escapeHtml(location.id)}">${escapeHtml(location.name)}</option>`).join('')}</select><p class="form-note">Restricted staff can only assign stock to locations returned by the API.</p></div><div class="field"><label for="material-opening">Opening stock</label><input id="material-opening" type="number" min="0" step="0.001" value="0" required></div><div class="field"><label for="material-reorder">Reorder level</label><input id="material-reorder" type="number" min="0" step="0.001" value="0" required></div><div class="field"><label for="material-notes">Notes (optional)</label><input id="material-notes" maxlength="1000"></div><div class="form-actions"><button class="button button-primary" type="submit">Save material</button><button class="button button-outline" id="inventory-cancel" type="button">Cancel</button></div><p id="inventory-form-error" class="form-error" role="alert"></p></form>`;
    action.querySelector('#inventory-cancel').addEventListener('click', () => { action.innerHTML = ''; });
    action.querySelector('#inventory-create').addEventListener('submit', async (event) => {
      event.preventDefault(); const form = event.currentTarget; const button = form.querySelector('[type="submit"]'); button.disabled = true;
      const openingStock = Number(form.querySelector('#material-opening').value); const reorderLevel = Number(form.querySelector('#material-reorder').value);
      if (!Number.isFinite(openingStock) || openingStock < 0 || !Number.isFinite(reorderLevel) || reorderLevel < 0) { form.querySelector('#inventory-form-error').textContent = 'Stock and reorder level must be valid non-negative numbers.'; button.disabled = false; return; }
      try { await api('/api/inventory', { method: 'POST', body: { sku: form.querySelector('#material-sku').value.trim(), name: form.querySelector('#material-name').value.trim(), unit: form.querySelector('#material-unit').value.trim(), locationId: form.querySelector('#material-location').value || null, openingStock, reorderLevel, notes: form.querySelector('#material-notes').value.trim() || null } }); showToast('Material added.'); await renderInventory(container); }
      catch (error) { form.querySelector('#inventory-form-error').textContent = error.message; button.disabled = false; }
    });
  });
  container.querySelectorAll('[data-stock-history]').forEach((button) => button.addEventListener('click', async () => {
    action.innerHTML = '<p>Loading stock movement history…</p>';
    try { const history = await api(`/api/inventory/${encodeURIComponent(button.dataset.stockHistory)}/movements?take=100`); const entries = Array.isArray(history) ? history : []; action.innerHTML = `<h3>Stock movement history</h3>${entries.length ? `<div class="table-wrap"><table><thead><tr><th>Date</th><th>Action</th><th>Quantity</th><th>On hand</th><th>Reserved</th><th>Note</th></tr></thead><tbody>${entries.map((x) => `<tr><td>${escapeHtml(new Date(x.createdAtUtc).toLocaleString())}</td><td>${escapeHtml(x.kind)}</td><td>${escapeHtml(x.quantity)}</td><td>${escapeHtml(x.onHandAfter)}</td><td>${escapeHtml(x.reservedAfter)}</td><td>${escapeHtml(x.note || '—')}</td></tr>`).join('')}</tbody></table></div>` : '<p>No stock movements were returned.</p>'}`; }
    catch (error) { action.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; }
  }));
  container.querySelectorAll('[data-stock-change]').forEach((button) => button.addEventListener('click', () => {
    action.innerHTML = `<h3>Update ${escapeHtml(button.dataset.stockName)}</h3><form id="inventory-stock-change" class="form-grid"><div class="field"><label for="stock-action">Action</label><select id="stock-action"><option value="receive">Receive additional stock</option><option value="adjust">Set physical stock count</option></select></div><div class="field"><label for="stock-quantity">Quantity</label><input id="stock-quantity" type="number" min="0.001" step="0.001" required></div><div class="field"><label for="stock-note">Reason / note</label><input id="stock-note" maxlength="500"></div><div class="form-actions"><button class="button button-primary" type="submit">Save stock update</button><button class="button button-outline" id="stock-cancel" type="button">Cancel</button></div><p class="form-note">“Receive” adds quantity. “Set physical stock count” sets total on-hand stock and cannot reduce it below reserved stock.</p><p id="stock-change-error" class="form-error" role="alert"></p></form>`;
    action.querySelector('#stock-cancel').addEventListener('click', () => { action.innerHTML = ''; });
    action.querySelector('#inventory-stock-change').addEventListener('submit', async (event) => {
      event.preventDefault(); const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); submit.disabled = true;
      const quantity = Number(form.querySelector('#stock-quantity').value); const actionName = form.querySelector('#stock-action').value;
      if (!Number.isFinite(quantity) || quantity <= 0) { form.querySelector('#stock-change-error').textContent = 'Enter a quantity greater than zero.'; submit.disabled = false; return; }
      try { await api(`/api/inventory/${encodeURIComponent(button.dataset.stockChange)}/${actionName}`, { method: 'POST', body: { quantity, note: form.querySelector('#stock-note').value.trim() || null, expectedVersion: Number(button.dataset.stockVersion) } }); showToast('Stock updated.'); await renderInventory(container); }
      catch (error) { form.querySelector('#stock-change-error').textContent = error.message; submit.disabled = false; }
    });
  }));
}

async function renderStaffList(container) {
  if (!hasPermission('staff.view')) { container.innerHTML = '<section class="card"><h2>Staff</h2><p class="form-error">Your account does not have permission to view staff.</p></section>'; return; }
  container.innerHTML = '<section class="card loading-card"><div class="spinner"></div><p>Loading staff and roles…</p></section>';
  try { const [staff, roles] = await Promise.all([api('/api/staff'), api('/api/roles').catch(() => [])]); const canManage = hasPermission('staff.manage'); const rows = staff.map(m => `<tr><td><strong>${escapeHtml(m.displayName)}</strong><small>${escapeHtml(m.email)}</small></td><td>${escapeHtml(m.status)}</td><td>${m.isOwner ? 'Owner' : (m.roleIds || []).map(id => roles.find(r => r.id === id)?.name || 'Role').map(escapeHtml).join(', ')}</td><td>${(m.locationIds || []).length}</td><td>${canManage && !m.isOwner ? `<button class="button button-outline" data-staff-action="${m.status === 'Active' ? 'deactivate' : 'reactivate'}" data-membership="${escapeHtml(m.membershipId)}">${m.status === 'Active' ? 'Deactivate' : 'Reactivate'}</button> <button class="button button-outline" data-staff-edit="roles" data-membership="${escapeHtml(m.membershipId)}">Roles</button> <button class="button button-outline" data-staff-edit="locations" data-membership="${escapeHtml(m.membershipId)}">Locations</button>` : '—'}</td></tr>`).join(''); container.innerHTML = `<section class="card panel"><div class="panel-head"><div><p class="eyebrow">Team management</p><h2>Staff</h2><p>Manage active membership and role assignments for this business.</p></div>${canManage ? '<a class="button button-primary button-fit" href="#staff/invite">Invite staff</a>' : ''}</div><div class="table-wrap"><table class="data-table"><thead><tr><th>Team member</th><th>Status</th><th>Roles</th><th>Locations</th><th>Action</th></tr></thead><tbody>${rows || '<tr><td colspan="5">No staff members found.</td></tr>'}</tbody></table></div><p class="form-note">Invitations require an email-delivery integration to reach the invited person. The API reports invitation creation status.</p></section>`;
    document.querySelectorAll('[data-staff-action]').forEach(btn => btn.addEventListener('click', async () => { const action = btn.dataset.staffAction; if (!confirm(`${action === 'deactivate' ? 'Deactivate' : 'Reactivate'} this team member?`)) return; try { await api(`/api/staff/${btn.dataset.membership}/${action}`, { method: 'POST' }); showToast('Staff membership updated.'); renderStaffList(container); } catch (error) { showToast(error.message || 'Could not update staff.'); } }));
    document.querySelectorAll('[data-staff-edit]').forEach(btn => btn.addEventListener('click', async () => { const member = staff.find(m => m.membershipId === btn.dataset.membership); if (!member) return; const isRoles = btn.dataset.staffEdit === 'roles'; const options = isRoles ? roles : await api('/api/locations').catch(() => []); const current = isRoles ? member.roleIds : member.locationIds; const choices = options.map(o => `${o.id} | ${o.name}`).join('\n'); const raw = prompt(`${isRoles ? 'Enter role names or IDs' : 'Enter location names or IDs'}, separated by commas. Available options:\n${choices}\nLeave blank to clear selection${isRoles ? ' (roles cannot be empty)' : ''}.`, current.map(id => options.find(o => o.id === id)?.name || id).join(', ')); if (raw === null) return; const tokens = raw.split(',').map(v => v.trim()).filter(Boolean); const ids = tokens.map(token => options.find(o => o.id.toLowerCase() === token.toLowerCase() || o.name.toLowerCase() === token.toLowerCase())?.id || token); if (ids.some(id => !/^[0-9a-f-]{36}$/i.test(id)) || (isRoles && !ids.length)) { showToast(isRoles ? 'Select one or more valid roles.' : 'Enter valid location names or IDs.'); return; } try { await api(`/api/staff/${member.membershipId}/${isRoles ? 'roles' : 'locations'}`, { method: 'PUT', body: isRoles ? { roleIds: ids } : { locationIds: ids } }); showToast(`${isRoles ? 'Roles' : 'Locations'} updated.`); renderStaffList(container); } catch (error) { showToast(error.message || 'Could not update staff assignments.'); } }));
  } catch (error) { container.innerHTML = `<section class="card"><h2>Could not load staff</h2><p class="form-error">${escapeHtml(error.message)}</p></section>`; }
}

async function renderStaffInvite(container) {
  if (!hasPermission('staff.manage')) { container.innerHTML = '<section class="card"><h2>Invite staff</h2><p class="form-error">Your account cannot invite staff.</p></section>'; return; }
  try { const [roles, locations] = await Promise.all([api('/api/roles'), api('/api/locations')]); container.innerHTML = `<section class="card panel"><p><a href="#staff">← Back to staff</a></p><p class="eyebrow">Team management</p><h2>Invite a team member</h2><div id="staff-invite-error"></div><form id="staff-invite-form"><div class="field"><label for="invite-name">Full name *</label><input id="invite-name" name="fullName" required minlength="2" maxlength="120"></div><div class="field"><label for="invite-email">Email *</label><input id="invite-email" name="email" type="email" required maxlength="256"></div><fieldset><legend>Roles *</legend>${roles.map(r=>`<label class="check-row"><input type="checkbox" name="roleIds" value="${escapeHtml(r.id)}"> ${escapeHtml(r.name)}</label>`).join('')}</fieldset><fieldset><legend>Workshop locations</legend>${locations.map(l=>`<label class="check-row"><input type="checkbox" name="locationIds" value="${escapeHtml(l.id)}"> ${escapeHtml(l.name)}</label>`).join('')}</fieldset><button class="button button-primary" type="submit">Create invitation</button></form></section>`;
    document.querySelector('#staff-invite-form').addEventListener('submit', async e => { e.preventDefault(); const f = e.currentTarget; const data = new FormData(f); const roleIds = data.getAll('roleIds'); const locationIds = data.getAll('locationIds'); const errorBox = document.querySelector('#staff-invite-error'); if (!roleIds.length) { errorBox.innerHTML = '<p class="form-error">Select at least one role.</p>'; return; } const submitButton = f.querySelector('button[type="submit"]'); submitButton.disabled = true; submitButton.textContent = 'Creating invitation…'; try { const invite = await api('/api/staff/invitations', { method: 'POST', body: { fullName: String(data.get('fullName')).trim(), email: String(data.get('email')).trim(), roleIds, locationIds } }); container.innerHTML = `<section class="card panel"><h2>Invitation created</h2><p>Invitation for ${escapeHtml(invite.email)} created. Expires ${escapeHtml(dateLabel(invite.expiresAtUtc))}.</p><p class="form-note">The invitation email depends on the configured email provider.</p><a class="button button-primary" href="#staff">Return to staff</a></section>`; } catch (error) { errorBox.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not create invitation.')}</p>`; submitButton.disabled = false; submitButton.textContent = 'Create invitation'; } });
  } catch (error) { container.innerHTML = `<section class="card"><h2>Could not prepare invitation</h2><p class="form-error">${escapeHtml(error.message)}</p></section>`; }
}

async function renderCustomerList(container) {
  if (!hasPermission('customers.view')) {
    container.innerHTML = `<section class="card"><h2>Customers</h2><p class="form-error">Your account does not have permission to view customers.</p></section>`;
    return;
  }
  const query = new URLSearchParams(location.hash.split('?')[1] || '');
  const search = (query.get('search') || '').slice(0, 100);
  const status = query.get('status') || '';
  const sort = query.get('sort') === 'created' ? 'created' : 'name';
  const descending = query.get('descending') === 'true';
  const page = Math.max(1, Math.min(100000, Number.parseInt(query.get('page') || '1', 10) || 1));
  const params = new URLSearchParams({ page: String(page), pageSize: '25', sort, descending: String(descending) });
  if (search) params.set('search', search);
  if (status) params.set('status', status);
  container.innerHTML = `<section class="card panel"><div class="panel-head"><div><p class="eyebrow">Customer records</p><h2>Customers</h2><p>Search and manage customer profiles for this business.</p></div>${hasPermission('customers.manage') ? '<a class="button button-primary button-fit" href="#customers/new">New customer</a>' : ''}</div><form id="customer-search" class="filter-form"><div class="field"><label for="customer-search-text">Search</label><input id="customer-search-text" name="search" maxlength="100" value="${escapeHtml(search)}" placeholder="Name, email or phone"></div><div class="field"><label for="customer-status">Status</label><select id="customer-status" name="status"><option value="" ${status === '' ? 'selected' : ''}>Active and inactive</option><option value="Active" ${status === 'Active' ? 'selected' : ''}>Active</option><option value="Inactive" ${status === 'Inactive' ? 'selected' : ''}>Inactive</option><option value="Merged" ${status === 'Merged' ? 'selected' : ''}>Merged</option></select></div><button class="button button-outline" type="submit">Apply filters</button></form><div id="customer-results" class="loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading customers…</p></div></section>`;
  document.querySelector('#customer-search').addEventListener('submit', (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const next = new URLSearchParams();
    const newSearch = form.elements.search.value.trim();
    const newStatus = form.elements.status.value;
    if (newSearch) next.set('search', newSearch);
    if (newStatus) next.set('status', newStatus);
    const suffix = next.toString();
    location.hash = `customers${suffix ? `?${suffix}` : ''}`;
  });
  try {
    const result = await api(`/api/customers?${params.toString()}`);
    const rows = Array.isArray(result?.items) ? result.items : [];
    const totalCount = Number(result?.totalCount) || 0;
    const pages = Math.max(1, Math.ceil(totalCount / (Number(result?.pageSize) || 25)));
    const resultContainer = document.querySelector('#customer-results');
    if (!resultContainer) return;
    const table = rows.length ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Name</th><th>Contact</th><th>Status</th><th>Orders</th></tr></thead><tbody>${rows.map((customer) => `<tr><td><a href="#customers/${escapeHtml(customer.id)}"><strong>${escapeHtml(customer.fullName)}</strong></a><small>Added ${escapeHtml(dateLabel(customer.createdAtUtc))}</small></td><td>${escapeHtml(customer.email || '—')}<small>${escapeHtml(customer.phone || '—')}</small></td><td><span class="pill">${escapeHtml(customer.status)}</span></td><td>${Number(customer.orderCount) || 0}</td></tr>`).join('')}</tbody></table></div>` : `<p class="empty-state">${search || status ? 'No customers match these filters.' : 'No customers yet. Create your first customer to get started.'}</p>`;
    const prevHash = page > 1 ? `#customers?${new URLSearchParams({ ...(search ? { search } : {}), ...(status ? { status } : {}), page: String(page - 1) }).toString()}` : '';
    const nextHash = page < pages ? `#customers?${new URLSearchParams({ ...(search ? { search } : {}), ...(status ? { status } : {}), page: String(page + 1) }).toString()}` : '';
    resultContainer.className = 'results-content';
    resultContainer.innerHTML = `${table}<div class="pager"><span>${totalCount} customer${totalCount === 1 ? '' : 's'} · page ${Math.min(page, pages)} of ${pages}</span><span>${prevHash ? `<a class="button button-outline" href="${prevHash}">Previous</a>` : ''} ${nextHash ? `<a class="button button-outline" href="${nextHash}">Next</a>` : ''}</span></div>`;
  } catch (error) {
    const resultContainer = document.querySelector('#customer-results');
    if (resultContainer) resultContainer.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not load customers.')}</p><button class="button button-outline" id="retry-customers" type="button">Try again</button>`;
    document.querySelector('#retry-customers')?.addEventListener('click', () => renderCustomerList(container));
  }
}

function customerFieldsMarkup(customer = {}) {
  const fields = [
    ['fullName', 'Full name', 'text', 120, true],
    ['email', 'Email', 'email', 256, false],
    ['phone', 'Phone', 'tel', 40, false],
    ['addressLine', 'Address', 'text', 300, false],
    ['city', 'City', 'text', 100, false],
  ];
  const regular = fields.map(([name, label, type, max, required]) => `<div class="field"><label for="field-${name}">${label}${required ? ' *' : ''}</label><input id="field-${name}" name="${name}" type="${type}" maxlength="${max}" ${required ? 'required minlength="2"' : ''} value="${escapeHtml(customer[name] || '')}"></div>`).join('');
  return `${regular}<div class="field"><label for="field-garmentPreferences">Garment preferences</label><textarea id="field-garmentPreferences" name="garmentPreferences" maxlength="2000" rows="3">${escapeHtml(customer.garmentPreferences || '')}</textarea><small>Fabrics, fit, colours and details to remember.</small></div><div class="field"><label for="field-notes">Notes</label><textarea id="field-notes" name="notes" maxlength="4000" rows="3">${escapeHtml(customer.notes || '')}</textarea></div>`;
}

async function renderCustomerForm(container, customerId = null) {
  if (!hasPermission('customers.manage')) {
    container.innerHTML = `<section class="card"><h2>Customer access</h2><p class="form-error">Your account does not have permission to manage customers.</p></section>`;
    return;
  }
  let customer = null;
  if (customerId) {
    try { customer = await api(`/api/customers/${encodeURIComponent(customerId)}`); }
    catch (error) {
      container.innerHTML = `<section class="card"><h2>Could not open customer</h2><p class="form-error">${escapeHtml(error.message)}</p><a href="#customers">Back to customers</a></section>`;
      return;
    }
  }
  customerDraft = customerDraft || (customer ? { ...customer } : {});
  customerDuplicateCandidates = null;
  container.innerHTML = `<section class="card panel"><p><a href="${customer ? `#customers/${escapeHtml(customer.id)}` : '#customers'}">← Back to customers</a></p><div><p class="eyebrow">Customer records</p><h2>${customer ? 'Edit customer' : 'New customer'}</h2><p class="lead">Contact details and notes are stored in your business workspace.</p></div><div id="customer-form-error"></div><form id="customer-form">${customerFieldsMarkup(customerDraft)}<div class="account-line"><button class="button button-primary" type="submit">${customer ? 'Save changes' : 'Create customer'}</button><a class="button button-outline" href="${customer ? `#customers/${escapeHtml(customer.id)}` : '#customers'}">Cancel</a></div></form></section>`;
  document.querySelector('#customer-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    if (!form.reportValidity()) return;
    const button = form.querySelector('button[type="submit"]');
    const formData = new FormData(form);
    const body = { fullName: String(formData.get('fullName') || '').trim(), allowDuplicate: false };
    for (const key of ['email', 'phone', 'addressLine', 'city', 'garmentPreferences', 'notes']) {
      const value = String(formData.get(key) || '').trim();
      if (value) body[key] = value;
    }
    if (customer) body.version = customer.version;
    customerDraft = { ...customerDraft, ...body };
    button.disabled = true;
    button.textContent = 'Saving…';
    try {
      const saved = customer
        ? await api(`/api/customers/${encodeURIComponent(customer.id)}`, { method: 'PUT', body })
        : await api('/api/customers', { method: 'POST', body });
      customerDraft = null;
      location.hash = `customers/${saved.id}`;
      showToast(customer ? 'Customer updated.' : 'Customer created.');
    } catch (error) {
      button.disabled = false;
      button.textContent = customer ? 'Save changes' : 'Create customer';
      const errorBox = document.querySelector('#customer-form-error');
      if (error.code === 'possible_duplicate' && Array.isArray(error.body?.candidates)) {
        customerDuplicateCandidates = error.body.candidates;
        errorBox.innerHTML = `<div class="form-error"><strong>A possible duplicate was found.</strong><p>Check these profiles before continuing.</p><ul>${customerDuplicateCandidates.map((candidate) => `<li><a href="#customers/${escapeHtml(candidate.id)}">${escapeHtml(candidate.fullName)}</a> — ${escapeHtml(candidate.email || candidate.phone || 'No contact details')} (matched on ${escapeHtml(candidate.matchedOn)})</li>`).join('')}</ul><button id="create-duplicate-anyway" class="button button-outline" type="button">Continue anyway</button></div>`;
        document.querySelector('#create-duplicate-anyway').addEventListener('click', async (retryEvent) => {
          const retryButton = retryEvent.currentTarget;
          body.allowDuplicate = true;
          retryButton.disabled = true;
          button.disabled = true;
          try {
            const saved = customer ? await api(`/api/customers/${encodeURIComponent(customer.id)}`, { method: 'PUT', body }) : await api('/api/customers', { method: 'POST', body });
            customerDraft = null;
            location.hash = `customers/${saved.id}`;
            showToast(customer ? 'Customer updated.' : 'Customer created.');
          } catch (retryError) {
            button.disabled = false;
            retryButton.disabled = false;
            errorBox.innerHTML = `<p class="form-error">${escapeHtml(retryError.message || 'Could not save customer.')}</p>`;
          }
        });
      } else if (error.code === 'stale_version') {
        errorBox.innerHTML = `<div class="form-error" role="alert">${escapeHtml(error.message || 'This customer changed in another session.')}<p>Your edits are still on this screen. Copy any changes you need, then reload the latest customer before saving again.</p><button id="reload-customer" class="button button-outline" type="button">Reload customer</button></div>`;
        document.querySelector('#reload-customer').addEventListener('click', () => { customerDraft = null; renderCustomerForm(container, customerId); });
      } else {
        errorBox.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not save customer.')}</p>`;
      }
    }
  });
}

async function renderCustomerDetail(container, customerId) {
  if (!hasPermission('customers.view')) {
    container.innerHTML = `<section class="card"><h2>Customer access</h2><p class="form-error">Your account does not have permission to view customers.</p></section>`;
    return;
  }
  if (!/^[0-9a-f-]{36}$/i.test(customerId)) {
    container.innerHTML = `<section class="card"><h2>Customer not found</h2><a href="#customers">Back to customers</a></section>`;
    return;
  }
  container.innerHTML = `<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading customer…</p></section>`;
  try {
    const customer = await api(`/api/customers/${encodeURIComponent(customerId)}`);
    const [measurements, communications] = await Promise.all([
      api(`/api/customers/${encodeURIComponent(customerId)}/measurements`).catch(() => null),
      api(`/api/customers/${encodeURIComponent(customerId)}/communications`).catch(() => null)
    ]);
    const merged = customer.status === 'Merged';
    const measurementContent = measurements === null
      ? '<p class="form-note">Measurement profiles could not be loaded for this account.</p>'
      : Array.isArray(measurements) && measurements.length
        ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Profile</th><th>Template</th><th>Current revision</th><th>Updated</th></tr></thead><tbody>${measurements.map((set) => `<tr><td><a href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(set.id)}/history"><strong>${escapeHtml(set.label)}</strong></a><small>${escapeHtml(set.values?.length || 0)} recorded values</small></td><td>${escapeHtml(set.templateName)}</td><td>${Number(set.currentRevision) || 1}</td><td>${escapeHtml(dateLabel(set.updatedAtUtc))}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="empty-state">No measurement profiles recorded yet.</p>';
    const measurementAction = hasPermission('measurements.record') && !merged
      ? `<div class="account-line"><a class="button button-primary button-fit" href="#customers/${escapeHtml(customer.id)}/measurements/new">Record measurements</a></div>`
      : '';
    const communicationRows = Array.isArray(communications) ? communications.map((item) => `<article class="revision-card"><div class="panel-head"><div><h3>${escapeHtml(item.subject || item.channel)}</h3><p>${escapeHtml(item.channel)} · ${escapeHtml(item.direction)} · ${escapeHtml(dateLabel(item.createdAtUtc))}</p></div></div><p class="pre-wrap">${escapeHtml(item.body)}</p></article>`).join('') : '<p class="form-note">Communication history could not be loaded.</p>';
    const communicationForm = hasPermission('customers.manage') && !merged ? `<form id="customer-communication-form"><div class="field"><label for="communication-channel">Channel</label><select id="communication-channel"><option>Email</option><option>Phone</option><option>WhatsApp</option><option>InPerson</option><option>Other</option></select></div><div class="field"><label for="communication-direction">Direction</label><select id="communication-direction"><option>Outbound</option><option>Inbound</option></select></div><div class="field"><label for="communication-subject">Subject (optional)</label><input id="communication-subject" maxlength="200"></div><div class="field"><label for="communication-body">Summary *</label><textarea id="communication-body" maxlength="4000" minlength="1" required rows="3"></textarea></div><button class="button button-primary" type="submit">Add communication record</button></form>` : '';
    const mergeAction = hasPermission('customers.merge') && hasPermission('locations.all') && !merged ? '<div class="account-line"><button class="button button-outline" id="merge-customer" type="button">Merge into another customer</button></div>' : hasPermission('customers.merge') && !hasPermission('locations.all') && !merged ? '<p class="form-note">Customer merges affect orders and fittings across workshop locations, so access to all locations is required.</p>' : '';
    container.innerHTML = `<section class="panel"><p><a href="#customers">← Customers</a></p><div class="card panel"><div class="panel-head"><div><p class="eyebrow">Customer profile</p><h2>${escapeHtml(customer.fullName)}</h2><span class="pill">${escapeHtml(customer.status)}</span></div>${hasPermission('customers.manage') && !merged ? `<a class="button button-outline" href="#customers/${escapeHtml(customer.id)}/edit">Edit</a>` : ''}</div>${merged ? '<p class="form-error">This profile has been merged and cannot be edited.</p>' : ''}<dl class="detail-list"><dt>Email</dt><dd>${escapeHtml(customer.email || '—')}</dd><dt>Phone</dt><dd>${escapeHtml(customer.phone || '—')}</dd><dt>Address</dt><dd>${escapeHtml([customer.addressLine, customer.city].filter(Boolean).join(', ') || '—')}</dd><dt>Customer since</dt><dd>${escapeHtml(dateLabel(customer.createdAtUtc))}</dd><dt>Garment preferences</dt><dd>${escapeHtml(customer.garmentPreferences || '—')}</dd><dt>Notes</dt><dd class="pre-wrap">${escapeHtml(customer.notes || '—')}</dd></dl>${hasPermission('customers.manage') && !merged ? `<div class="account-line"><button class="button button-outline" id="toggle-customer-status" type="button">${customer.status === 'Inactive' ? 'Reactivate customer' : 'Mark inactive'}</button>${customer.email ? '<button class="button button-outline" id="send-customer-portal" type="button">Send customer portal link</button><button class="button button-outline" id="revoke-customer-portal" type="button">Revoke portal links</button>' : ''}</div>` : ''}${mergeAction}</div><div class="card panel"><div class="panel-head"><div><p class="eyebrow">Measurements</p><h2>Measurement profiles</h2><p class="lead">Profiles preserve revision history and can be linked to garment orders.</p></div></div>${measurementAction}${measurementContent}</div><div class="card panel"><div><p class="eyebrow">Customer relationship</p><h2>Communication history</h2><p class="lead">Record calls, messages and other customer interactions for the next team member.</p></div>${communicationForm}<div class="revision-list">${communicationRows || '<p class="empty-state">No communications recorded yet.</p>'}</div></div></section>`;
    document.querySelector('#customer-communication-form')?.addEventListener('submit', async (event) => {
      event.preventDefault();
      const body = document.querySelector('#communication-body').value.trim();
      if (!body) return;
      const button = event.currentTarget.querySelector('button[type="submit"]'); button.disabled = true; button.textContent = 'Saving record…';
      try {
        await api(`/api/customers/${encodeURIComponent(customer.id)}/communications`, { method: 'POST', body: { channel: document.querySelector('#communication-channel').value, direction: document.querySelector('#communication-direction').value, subject: document.querySelector('#communication-subject').value.trim() || null, body } });
        showToast('Communication record added.'); await renderCustomerDetail(container, customerId);
      } catch (error) { showToast(error.message || 'Could not save communication.'); button.disabled = false; button.textContent = 'Add communication record'; }
    });
    document.querySelector('#merge-customer')?.addEventListener('click', async () => {
      if (!hasPermission('customers.merge') || !hasPermission('locations.all')) return;
      try {
        const result = await api('/api/customers?page=1&pageSize=100&status=Active');
        const targets = (result.items || []).filter((item) => item.id !== customer.id && item.status !== 'Merged');
        if (!targets.length) { showToast('No other active customer profiles are available to merge into.'); return; }
        const options = targets.map((item) => `${item.id} | ${item.fullName}${item.email ? ` (${item.email})` : ''}`).join('\n');
        const targetId = prompt(`Enter the ID of the customer that should be kept. This profile will be marked merged.\n${options}`);
        if (targetId === null) return;
        const target = targets.find((item) => item.id.toLowerCase() === targetId.trim().toLowerCase());
        if (!target) { showToast('Choose a target ID from the listed customers.'); return; }
        if (!confirm(`Merge ${customer.fullName} into ${target.fullName}? The source profile will remain as a merged record.`)) return;
        await api(`/api/customers/${encodeURIComponent(customer.id)}/merge-into/${encodeURIComponent(target.id)}`, { method: 'POST' });
        showToast('Customer profiles merged.'); location.hash = `customers/${encodeURIComponent(target.id)}`;
      } catch (error) { showToast(error.message || 'Could not merge customer profiles.'); }
    });
    document.querySelector('#send-customer-portal')?.addEventListener('click', async (event) => {
      const button = event.currentTarget; button.disabled = true; button.textContent = 'Sending link…';
      try { const result = await api(`/api/customer-portal/customers/${encodeURIComponent(customer.id)}/link`, { method: 'POST' }); showToast(`Portal link emailed to ${result.maskedEmail}. It expires ${dateLabel(result.expiresAtUtc)}.`); }
      catch (error) { showToast(error.message || 'Could not send customer portal link.'); }
      finally { button.disabled = false; button.textContent = 'Send customer portal link'; }
    });
    document.querySelector('#revoke-customer-portal')?.addEventListener('click', async (event) => {
      if (!confirm('Revoke all active customer portal links for this customer? Existing links will stop working.')) return;
      const button = event.currentTarget; button.disabled = true;
      try { await api(`/api/customer-portal/customers/${encodeURIComponent(customer.id)}/links`, { method: 'DELETE' }); showToast('Active customer portal links revoked.'); }
      catch (error) { showToast(error.message || 'Could not revoke portal links.'); }
      finally { button.disabled = false; }
    });
    const statusButton = document.querySelector('#toggle-customer-status');
    if (statusButton) statusButton.addEventListener('click', async () => {
      statusButton.disabled = true;
      try {
        await api(`/api/customers/${encodeURIComponent(customer.id)}/status`, { method: 'POST', body: { status: customer.status === 'Inactive' ? 'Active' : 'Inactive' } });
        showToast(customer.status === 'Inactive' ? 'Customer reactivated.' : 'Customer marked inactive.');
        await renderCustomerDetail(container, customerId);
      } catch (error) {
        statusButton.disabled = false;
        showToast(error.message || 'Could not update customer status.');
      }
    });
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not open customer</h2><p class="form-error">${escapeHtml(error.message || 'Try again shortly.')}</p><a href="#customers">Back to customers</a></section>`;
  }
}


function measurementValuesObject(values = []) {
  return Object.fromEntries((Array.isArray(values) ? values : []).map((item) => [item.key, item.value]));
}

function measurementFieldsMarkup(fields, values = {}) {
  if (!Array.isArray(fields) || fields.length === 0) {
    return '<p class="empty-state">This template has no measurement fields. Choose another template or ask a business manager to configure one.</p>';
  }
  return `<div class="measurement-grid">${[...fields].sort((a, b) => (a.sortOrder || 0) - (b.sortOrder || 0)).map((field) => {
    const hasValue = values[field.key] !== undefined && values[field.key] !== null && values[field.key] !== '';
    const min = field.minValue === null || field.minValue === undefined ? '' : `min="${escapeHtml(field.minValue)}"`;
    const max = field.maxValue === null || field.maxValue === undefined ? '' : `max="${escapeHtml(field.maxValue)}"`;
    return `<div class="field"><label for="measurement-${escapeHtml(field.key)}">${escapeHtml(field.label)}${field.isRequired ? ' *' : ''} <span class="unit-label">(${escapeHtml(field.unit || 'cm')})</span></label><input id="measurement-${escapeHtml(field.key)}" name="${escapeHtml(field.key)}" type="number" inputmode="decimal" step="any" ${field.isRequired ? 'required' : ''} ${min} ${max} value="${hasValue ? escapeHtml(values[field.key]) : ''}" aria-describedby="measurement-help-${escapeHtml(field.key)}"><small id="measurement-help-${escapeHtml(field.key)}">${field.isRequired ? 'Required' : 'Optional'}${field.minValue != null || field.maxValue != null ? ` · Range ${field.minValue ?? 'any'}–${field.maxValue ?? 'any'}` : ''}</small></div>`;
  }).join('')}</div>`;
}

async function renderMeasurementForm(container, customerId, setId = null) {
  if (!hasPermission('measurements.record')) {
    container.innerHTML = '<section class="card"><h2>Measurement access</h2><p class="form-error">Your account does not have permission to record measurements.</p><a href="#customers">Back to customers</a></section>';
    return;
  }
  if (!/^[0-9a-f-]{36}$/i.test(customerId) || (setId && !/^[0-9a-f-]{36}$/i.test(setId))) {
    container.innerHTML = '<section class="card"><h2>Measurement profile not found</h2><a href="#customers">Back to customers</a></section>';
    return;
  }
  const isRevision = Boolean(setId);
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Preparing measurement form…</p></section>';
  try {
    const customer = await api(`/api/customers/${encodeURIComponent(customerId)}`);
    let set = null;
    let templates = [];
    let template = null;
    if (isRevision) {
      const sets = await api(`/api/customers/${encodeURIComponent(customerId)}/measurements`);
      set = Array.isArray(sets) ? sets.find((item) => item.id === setId) : null;
      if (!set) throw new ApiError(404, { message: 'This measurement profile does not belong to the selected customer.' });
      template = await api(`/api/measurement-templates/${encodeURIComponent(set.templateId)}`);
    } else {
      templates = await api('/api/measurement-templates');
      templates = Array.isArray(templates) ? templates.filter((item) => item?.isActive && Array.isArray(item.fields) && item.fields.length) : [];
      if (templates.length) template = templates[0];
    }
    const currentValues = isRevision ? measurementValuesObject(set.values) : {};
    const backLink = `#customers/${escapeHtml(customerId)}`;
    if (!isRevision && !templates.length) {
      container.innerHTML = `<section class="card panel"><p><a href="${backLink}">← Back to customer</a></p><h2>No measurement templates available</h2><p class="form-note">A business manager must create an active measurement template before a profile can be recorded.</p></section>`;
      return;
    }
    const templateSelect = !isRevision ? `<div class="field"><label for="measurement-template">Measurement template *</label><select id="measurement-template" name="templateId" required>${templates.map((item) => `<option value="${escapeHtml(item.id)}" ${item.id === template?.id ? 'selected' : ''}>${escapeHtml(item.name)}</option>`).join('')}</select>${template?.description ? `<small>${escapeHtml(template.description)}</small>` : ''}</div>` : `<p class="form-note">Template: <strong>${escapeHtml(template?.name || set.templateName)}</strong>. A revision records a complete new set of values; it does not patch the previous revision.</p>`;
    container.innerHTML = `<section class="card panel"><p><a href="${backLink}">← Back to ${escapeHtml(customer.fullName)}</a></p><div><p class="eyebrow">Customer measurements</p><h2>${isRevision ? 'Record new revision' : 'New measurement profile'}</h2><p class="lead">${isRevision ? `Current revision: ${Number(set.currentRevision) || 1}. Previous revisions remain in history.` : 'Choose a template and enter the values you measured.'}</p></div><div id="measurement-form-error"></div><form id="measurement-form">${isRevision ? '' : templateSelect}<div class="field"><label for="measurement-label">Profile label *</label><input id="measurement-label" name="label" maxlength="80" minlength="1" required value="${escapeHtml(set?.label || '')}" ${isRevision ? 'readonly' : ''} placeholder="e.g. Shirt measurements"></div><div id="measurement-fields">${measurementFieldsMarkup(template?.fields || [], currentValues)}</div><div class="field"><label for="measurement-notes">Notes</label><textarea id="measurement-notes" name="notes" maxlength="2000" rows="3" placeholder="Optional fitting or measurement notes"></textarea></div><div class="account-line"><button class="button button-primary" type="submit">${isRevision ? 'Save revision' : 'Save measurement profile'}</button><a class="button button-outline" href="${backLink}">Cancel</a></div></form></section>`;
    const form = document.querySelector('#measurement-form');
    const fieldsContainer = document.querySelector('#measurement-fields');
    const templateSelectElement = document.querySelector('#measurement-template');
    if (templateSelectElement) templateSelectElement.addEventListener('change', async () => {
      fieldsContainer.innerHTML = '<div class="loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading template fields…</p></div>';
      try {
        const selected = await api(`/api/measurement-templates/${encodeURIComponent(templateSelectElement.value)}`);
        fieldsContainer.innerHTML = measurementFieldsMarkup(selected.fields || []);
        const description = templateSelectElement.parentElement.querySelector('small');
        if (description) description.textContent = selected.description || '';
      } catch (error) {
        fieldsContainer.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not load template fields.')}</p>`;
      }
    });
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!form.reportValidity()) return;
      const button = form.querySelector('button[type="submit"]');
      const formData = new FormData(form);
      const values = {};
      for (const field of template?.fields || []) {
        const raw = String(formData.get(field.key) ?? '').trim();
        if (raw !== '') values[field.key] = Number(raw);
      }
      // When the user changes templates, the fields are loaded asynchronously; use the active field inputs as the source of truth.
      for (const input of fieldsContainer.querySelectorAll('input[type="number"]')) {
        const raw = input.value.trim();
        if (raw !== '') values[input.name] = Number(raw);
        else delete values[input.name];
      }
      if (Object.values(values).some((value) => !Number.isFinite(value))) {
        document.querySelector('#measurement-form-error').innerHTML = '<p class="form-error" role="alert">Enter valid numeric values for all measurements.</p>';
        return;
      }
      const body = { values };
      const notes = String(formData.get('notes') || '').trim();
      if (notes) body.notes = notes;
      if (!isRevision) {
        body.templateId = String(formData.get('templateId') || '');
        body.label = String(formData.get('label') || '').trim();
      }
      button.disabled = true;
      button.textContent = isRevision ? 'Saving revision…' : 'Saving profile…';
      try {
        if (isRevision) await api(`/api/measurement-sets/${encodeURIComponent(setId)}/revisions`, { method: 'POST', body });
        else await api(`/api/customers/${encodeURIComponent(customerId)}/measurements`, { method: 'POST', body });
        showToast(isRevision ? 'Measurement revision saved.' : 'Measurement profile created.');
        location.hash = isRevision
          ? `customers/${encodeURIComponent(customerId)}/measurements/${encodeURIComponent(setId)}/history`
          : `customers/${encodeURIComponent(customerId)}`;
      } catch (error) {
        button.disabled = false;
        button.textContent = isRevision ? 'Save revision' : 'Save measurement profile';
        document.querySelector('#measurement-form-error').innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message || 'Could not save measurements.')}</p>`;
      }
    });
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not open measurements</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p><a href="#customers/${escapeHtml(customerId)}">Back to customer</a></section>`;
  }
}

async function renderMeasurementHistory(container, customerId, setId) {
  if (!hasPermission('customers.view')) {
    container.innerHTML = '<section class="card"><h2>Measurement access</h2><p class="form-error">Your account does not have permission to view measurements.</p></section>';
    return;
  }
  if (!/^[0-9a-f-]{36}$/i.test(customerId) || !/^[0-9a-f-]{36}$/i.test(setId)) {
    container.innerHTML = '<section class="card"><h2>Measurement profile not found</h2><a href="#customers">Back to customers</a></section>';
    return;
  }
  container.innerHTML = '<section class="card loading-card"><div class="spinner" aria-hidden="true"></div><p>Loading measurement history…</p></section>';
  try {
    const [customer, sets, history] = await Promise.all([
      api(`/api/customers/${encodeURIComponent(customerId)}`),
      api(`/api/customers/${encodeURIComponent(customerId)}/measurements`),
      api(`/api/measurement-sets/${encodeURIComponent(setId)}/history`)
    ]);
    const set = Array.isArray(sets) ? sets.find((item) => item.id === setId) : null;
    if (!set || !Array.isArray(history)) throw new ApiError(404, { message: 'Measurement history was not found for this customer.' });
    const revisions = [...history].sort((a, b) => Number(b.revisionNumber) - Number(a.revisionNumber));
    const revisionCards = revisions.length ? revisions.map((revision) => {
      const revisionNumber = Number(revision.revisionNumber) || 1;
      const values = Array.isArray(revision.values) ? revision.values : [];
      const valueRows = values.map((value) => `<tr><td>${escapeHtml(value.label || value.key)}</td><td>${escapeHtml(value.value)} ${escapeHtml(value.unit || '')}</td></tr>`).join('');
      const currentRevision = Number(set.currentRevision) || revisionNumber;
      const compareFrom = revisionNumber === currentRevision ? Math.max(1, currentRevision - 1) : Math.min(revisionNumber, currentRevision);
      const compareTo = revisionNumber === currentRevision ? currentRevision : Math.max(revisionNumber, currentRevision);
      const compareAction = revisions.length > 1
        ? `<a class="button button-outline button-fit" href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(setId)}/compare?from=${compareFrom}&to=${compareTo}">Compare with current</a>`
        : '';
      return `<article class="revision-card"><div class="panel-head"><div><h3>Revision ${revisionNumber}${revisionNumber === Number(set.currentRevision) ? ' <span class="pill">Current</span>' : ''}</h3><p>${escapeHtml(dateLabel(revision.recordedAtUtc))}</p></div>${compareAction}</div>${revision.notes ? `<p class="pre-wrap">${escapeHtml(revision.notes)}</p>` : ''}<div class="table-wrap"><table class="data-table"><thead><tr><th>Measurement</th><th>Value</th></tr></thead><tbody>${valueRows || '<tr><td colspan="2">No values recorded.</td></tr>'}</tbody></table></div></article>`;
    }).join('') : '<p class="empty-state">No measurement revisions were found.</p>';
    container.innerHTML = `<section class="panel"><p><a href="#customers/${escapeHtml(customerId)}">← Back to ${escapeHtml(customer.fullName)}</a></p><div class="card panel"><div class="panel-head"><div><p class="eyebrow">Measurement history</p><h2>${escapeHtml(set.label)}</h2><p class="lead">${escapeHtml(set.templateName)} · Current revision ${Number(set.currentRevision) || 1}</p></div>${hasPermission('measurements.record') ? `<a class="button button-primary button-fit" href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(setId)}/revise">Record revision</a>` : ''}</div><div class="revision-list">${revisionCards}</div></div></section>`;
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not load measurement history</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p><a href="#customers/${escapeHtml(customerId)}">Back to customer</a></section>`;
  }
}

async function renderMeasurementCompare(container, customerId, setId, fromValue, toValue) {
  if (!hasPermission('customers.view')) {
    container.innerHTML = '<section class="card"><h2>Measurement access</h2><p class="form-error">You do not have permission to compare measurements.</p></section>';
    return;
  }
  if (!/^[0-9a-f-]{36}$/i.test(customerId) || !/^[0-9a-f-]{36}$/i.test(setId)) {
    container.innerHTML = '<section class="card"><h2>Measurement profile not found</h2><a href="#customers">Back to customers</a></section>';
    return;
  }
  container.innerHTML = '<section class="card loading-card"><div class="spinner"></div><p>Loading revision comparison…</p></section>';
  try {
    const [customer, sets, history] = await Promise.all([
      api(`/api/customers/${encodeURIComponent(customerId)}`),
      api(`/api/customers/${encodeURIComponent(customerId)}/measurements`),
      api(`/api/measurement-sets/${encodeURIComponent(setId)}/history`)
    ]);
    const set = Array.isArray(sets) ? sets.find((item) => item.id === setId) : null;
    if (!set || !Array.isArray(history)) throw new ApiError(404, { message: 'Measurement history was not found for this customer.' });
    const revisions = [...history].map((item) => Number(item.revisionNumber)).filter((n) => Number.isInteger(n) && n > 0).sort((a, b) => a - b);
    if (revisions.length < 2) {
      container.innerHTML = `<section class="card panel"><p><a href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(setId)}/history">← Back to measurement history</a></p><h2>Not enough revisions to compare</h2><p class="lead">Record another revision before comparing changes.</p></section>`;
      return;
    }
    const latest = Math.max(...revisions);
    let from = Number.parseInt(fromValue || String(Math.max(1, latest - 1)), 10);
    let to = Number.parseInt(toValue || String(latest), 10);
    if (!revisions.includes(from) || !revisions.includes(to) || from === to) { from = Math.max(1, latest - 1); to = latest; }
    const loadComparison = async (fromRevision, toRevision) => api(`/api/measurement-sets/${encodeURIComponent(setId)}/compare?from=${fromRevision}&to=${toRevision}`);
    const comparison = await loadComparison(from, to);
    const rows = Array.isArray(comparison) ? comparison : [];
    const rowMarkup = rows.map((item) => {
      const delta = item.delta;
      const formatted = (value) => value === null || value === undefined ? '—' : `${value}${item.unit ? ` ${item.unit}` : ''}`;
      return `<tr><td>${escapeHtml(item.label || item.key)}</td><td>${escapeHtml(formatted(item.from))}</td><td>${escapeHtml(formatted(item.to))}</td><td>${escapeHtml(formatted(delta))}</td></tr>`;
    }).join('');
    const options = (selected) => revisions.map((n) => `<option value="${n}" ${n === selected ? 'selected' : ''}>Revision ${n}</option>`).join('');
    container.innerHTML = `<section class="panel"><p><a href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(setId)}/history">← Back to ${escapeHtml(set.label)} history</a></p><div class="card panel"><p class="eyebrow">Measurement comparison</p><h2>${escapeHtml(customer.fullName)}</h2><p class="lead">${escapeHtml(set.label)} · ${escapeHtml(set.templateName || '')}</p><form id="measurement-compare-form" class="filter-form"><div class="field"><label for="compare-from">Compare from</label><select id="compare-from" name="from">${options(from)}</select></div><div class="field"><label for="compare-to">Compare to</label><select id="compare-to" name="to">${options(to)}</select></div><button class="button button-primary" type="submit">Compare revisions</button></form><div class="table-wrap"><table class="data-table"><thead><tr><th>Measurement</th><th>From</th><th>To</th><th>Change</th></tr></thead><tbody>${rowMarkup || '<tr><td colspan="4">No measurement differences were returned.</td></tr>'}</tbody></table></div><p class="form-note">Changes are calculated by the existing API. Previous revisions remain unchanged.</p></div></section>`;
    document.querySelector('#measurement-compare-form').addEventListener('submit', (event) => {
      event.preventDefault();
      const form = event.currentTarget;
      const nextFrom = Number.parseInt(form.elements.from.value, 10);
      const nextTo = Number.parseInt(form.elements.to.value, 10);
      if (!Number.isInteger(nextFrom) || !Number.isInteger(nextTo) || nextFrom === nextTo) {
        showToast('Choose two different measurement revisions.');
        return;
      }
      location.hash = `customers/${encodeURIComponent(customerId)}/measurements/${encodeURIComponent(setId)}/compare?from=${nextFrom}&to=${nextTo}`;
    });
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not compare measurements</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p><a href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(setId)}/history">Back to measurement history</a></section>`;
  }
}


async function renderCustomerPortal() {
  const token = location.hash.length > 1 ? decodeURIComponent(location.hash.slice(1)) : '';
  // The capability token is removed from the address bar immediately and is never persisted.
  history.replaceState(null, '', `${location.pathname}${location.search}`);
  app.innerHTML = `<section class="auth-wrap card"><p class="eyebrow">Customer portal</p><h1>Secure order updates</h1><p class="lead">Checking your private portal link…</p><div class="loading-card"><div class="spinner" aria-hidden="true"></div></div></section>`;
  if (!token) {
    app.innerHTML = '<section class="auth-wrap card"><p class="eyebrow">Customer portal</p><h1>Portal link required</h1><p>This page needs the secure link sent by your tailoring business. Open the complete link from your email, or contact the business for a new one.</p></section>';
    return;
  }
  try {
    const response = await fetch('/backend/api/customer-portal/access', { method: 'POST', headers: { accept: 'application/json', 'content-type': 'application/json' }, credentials: 'omit', cache: 'no-store', body: JSON.stringify({ token }) });
    const result = await readBody(response);
    if (!response.ok) throw new ApiError(response.status, result);
    const orders = Array.isArray(result.orders) ? result.orders : [];
    const invoices = Array.isArray(result.invoices) ? result.invoices : [];
    app.innerHTML = `<section class="panel"><div class="welcome"><p class="eyebrow">Customer portal · ${escapeHtml(result.businessName)}</p><h1>Hello, ${escapeHtml(result.customerName)}.</h1><p>View your order progress and invoice balances. This link is private to you; do not forward it.</p><div class="account-line"><span class="pill">${orders.length} order${orders.length === 1 ? '' : 's'}</span><span class="pill">${invoices.length} invoice${invoices.length === 1 ? '' : 's'}</span></div></div><div class="card"><h2>Your orders</h2>${orders.map((order) => `<article class="revision-card"><div class="panel-head"><div><h3>Order ${escapeHtml(order.orderNumber)}</h3><p>${escapeHtml(order.status)} · Promised ${escapeHtml(order.promisedDate)}</p></div><span class="pill">${escapeHtml(order.total)} ${escapeHtml(order.currencyCode || result.currencyCode)}</span></div><div class="revision-list">${(order.progress || []).map((step) => `<div class="revision-card"><strong>${escapeHtml(step.stage)}</strong><p>${escapeHtml(step.status)}${step.completedAtUtc ? ` · Completed ${escapeHtml(dateLabel(step.completedAtUtc))}` : ''}</p></div>`).join('') || '<p>Production progress has not been published yet.</p>'}</div></article>`).join('') || '<p class="empty-state">No active orders are available.</p>'}</div><div class="card"><h2>Your invoices</h2>${invoices.map((invoice) => `<article class="revision-card"><div class="panel-head"><div><h3>${escapeHtml(invoice.number || 'Invoice')}</h3><p>${escapeHtml(invoice.status)}${invoice.dueDate ? ` · Due ${escapeHtml(invoice.dueDate)}` : ''}</p></div><span class="pill">Balance: ${escapeHtml(invoice.balance)} ${escapeHtml(invoice.currencyCode)}</span></div><p>Total ${escapeHtml(invoice.total)} ${escapeHtml(invoice.currencyCode)} · Verified paid ${escapeHtml(invoice.verifiedPaid)}</p><div class="table-wrap"><table><thead><tr><th>Date</th><th>Method</th><th>Amount</th><th>Reference</th></tr></thead><tbody>${(invoice.payments || []).map((payment) => `<tr><td>${escapeHtml(payment.receivedOn)}</td><td>${escapeHtml(payment.method)}</td><td>${escapeHtml(payment.amount)} ${escapeHtml(payment.currencyCode)}</td><td>${escapeHtml(payment.reference || '—')}</td></tr>`).join('') || '<tr><td colspan="4">No verified payments are recorded.</td></tr>'}</tbody></table></div></article>`).join('') || '<p class="empty-state">No invoices are available.</p>'}</div><p class="form-note">Payment history shows verified payments only. To make a payment or ask a question, contact ${escapeHtml(result.businessName)} directly. This portal does not accept card details.</p></section>`;
  } catch (error) {
    app.innerHTML = `<section class="auth-wrap card"><p class="eyebrow">Customer portal</p><h1>Link unavailable</h1><p class="form-error" role="alert">${escapeHtml(error.message || 'This link is invalid, revoked or expired.')}</p><p>For your security, request a new portal link from the business if you still need access.</p></section>`;
  }
}

async function signOut() {
  const button = document.querySelector('#sign-out');
  if (button) { button.disabled = true; button.textContent = 'Signing out…'; }
  try {
    await api('/api/auth/logout', { method: 'POST' });
  } catch (error) {
    // Clear the visible session even if the network is unavailable. The server cookie may expire naturally.
    showToast(`Sign-out could not be confirmed by the server: ${error.message}`);
  }
  currentUser = null;
  loginState = { email: '', password: '', choices: [], businessId: '', twoFactorRequired: false, useRecovery: false, code: '' };
  await checkSession();
}

async function checkSession() {
  app.innerHTML = '<section class="loading-card" aria-live="polite"><div class="spinner" aria-hidden="true"></div><p>Checking your session…</p></section>';
  try {
    const user = await api('/api/auth/me');
    currentUser = user;
    await renderDashboard();
  } catch (error) {
    if (error.status !== 401 && error.status !== 403) {
      renderLogin(`Avzar could not verify your session. ${error.message}`);
      return;
    }
    renderLogin();
  }
}

window.addEventListener('hashchange', () => {
  const view = activeViewFromHash();
  if (view !== 'customers/new' && !/^customers\/[^/]+\/edit$/.test(view)) customerDraft = null;
  if (currentUser) renderDashboard();
});
window.addEventListener('online', setConnectionStatus);
window.addEventListener('offline', setConnectionStatus);
setConnectionStatus();

window.addEventListener('beforeinstallprompt', (event) => {
  event.preventDefault();
  installPrompt = event;
  installButton.hidden = false;
});
window.addEventListener('appinstalled', () => {
  installPrompt = null;
  installButton.hidden = true;
  showToast('Avzar has been installed. You can open it from your Home Screen.');
});
installButton.addEventListener('click', async () => {
  if (installPrompt) {
    await installPrompt.prompt();
    installPrompt = null;
    installButton.hidden = true;
    return;
  }
  showToast(/iPhone|iPad|iPod/.test(navigator.userAgent)
    ? 'On iPhone: open Safari’s Share menu, then choose Add to Home Screen.'
    : 'Open your browser menu and choose Install app or Add to Home screen.');
});

const updateNotice = document.querySelector('#update-notice');
const updateButton = document.querySelector('#update-button');
let waitingWorker = null;
let reloadingForUpdate = false;

function showUpdateNotice(worker) {
  waitingWorker = worker;
  updateNotice.hidden = false;
}

updateButton.addEventListener('click', () => {
  if (!waitingWorker) return;
  updateButton.disabled = true;
  updateButton.textContent = 'Updating…';
  waitingWorker.postMessage({ type: 'SKIP_WAITING' });
});

navigator.serviceWorker?.addEventListener('controllerchange', () => {
  if (!waitingWorker || reloadingForUpdate) return;
  reloadingForUpdate = true;
  window.location.reload();
});

if ('serviceWorker' in navigator && (location.protocol === 'https:' || ['localhost', '127.0.0.1'].includes(location.hostname))) {
  navigator.serviceWorker.register('/sw.js').then((registration) => {
    if (registration.waiting && navigator.serviceWorker.controller) showUpdateNotice(registration.waiting);
    registration.addEventListener('updatefound', () => {
      const installing = registration.installing;
      if (!installing) return;
      installing.addEventListener('statechange', () => {
        if (installing.state === 'installed' && navigator.serviceWorker.controller) showUpdateNotice(installing);
      });
    });
    window.addEventListener('online', () => registration.update().catch(() => {}));
  }).catch(() => showToast('Offline shell could not be enabled on this visit. Avzar can still work online.'));
}

if (location.pathname.replace(/\/$/, '') === '/customer-portal') renderCustomerPortal();
else checkSession();
