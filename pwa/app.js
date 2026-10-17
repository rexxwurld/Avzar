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
  if (options.body !== undefined) headers['content-type'] = 'application/json';
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
      body: options.body === undefined ? undefined : JSON.stringify(options.body)
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
    <a class="nav-item" href="#tasks" ${root === 'tasks' ? 'aria-current="page"' : ''}>Daily work</a>
    ${hasPermission('staff.view') ? `<a class="nav-item" href="#staff" ${root === 'staff' ? 'aria-current="page"' : ''}>Staff</a>` : ''}
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
  if (view === 'home') {
    container.innerHTML = `<section class="card panel"><div class="panel-head"><div><h2>Your workspace is connected</h2><p>Avzar verified your signed-in session with the existing API. Business modules will be connected in staged releases.</p></div></div><p class="status-line"><span class="status-dot"></span> Signed in securely</p><p class="form-note">Customers, orders and production scheduling connect to the existing API. Finance, inventory, storefront editing and offline write synchronization are separate rollout stages.</p></section>`;
    return;
  }
  if (view === 'customers') return renderCustomerList(container);
  if (view === 'customers/new') return renderCustomerForm(container);
  if (view === 'orders') return renderOrderList(container);
  if (view === 'orders/new') return renderOrderForm(container);
  if (view === 'tasks') return renderTaskBoard(container);
  if (view === 'tasks/schedule') return renderShopSchedule(container);
  if (view === 'tasks/workers') return renderWorkerSchedules(container);
  if (view === 'staff') return renderStaffList(container);
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
  const names = { settings: 'Settings' };
  const title = names[parts[0]];
  if (title) {
    container.innerHTML = `<section class="card panel"><p class="eyebrow">Coming next</p><h2>${title}</h2><p class="lead">This module has not yet been connected to the PWA. The existing application remains available during migration; no sample data is shown here.</p></section>`;
    return;
  }
  container.innerHTML = `<section class="card"><h2>Page not found</h2><p><a href="#home">Return to overview</a></p></section>`;
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
    container.innerHTML = `<section class="card panel"><p><a href="#tasks">← Back to daily work</a></p><p class="eyebrow">Production task</p><h2>${escapeHtml(t.title)}</h2><p><span class="pill">${escapeHtml(t.status)}</span> · ${escapeHtml(t.orderNumber || '')} · ${escapeHtml(t.garmentName || '')}</p><dl class="detail-list"><dt>Stage</dt><dd>${escapeHtml(t.stageName || '—')}</dd><dt>Assigned to</dt><dd>${escapeHtml(t.assigneeName || 'Unassigned')}</dd><dt>Deadline</dt><dd>${escapeHtml(dateLabel(t.deadlineUtc))}</dd><dt>Instructions</dt><dd>${escapeHtml(task.instructions || '—')}</dd><dt>Dependencies remaining</dt><dd>${Number(t.unmetDependencies) || 0}</dd></dl>${checks.length ? `<h3>Completion checklist</h3><ul>${checks.map(c => `<li>${escapeHtml(c)}</li>`).join('')}</ul>` : ''}<div id="task-detail-error"></div><div class="account-line">${manager && hasPermission('tasks.assign') && !['Completed','Cancelled'].includes(t.status) ? '<button class="button button-outline" id="task-detail-assign">Assign worker</button>' : ''}${hasPermission('tasks.work') && ['Scheduled','Pending'].includes(t.status) ? '<button class="button button-primary" id="task-start">Start task</button>' : ''}${hasPermission('tasks.work') && ['InProgress','Paused'].includes(t.status) ? '<button class="button button-primary" id="task-complete">Complete task</button>' : ''}${hasPermission('tasks.work') && ['Scheduled','Pending','InProgress'].includes(t.status) ? '<button class="button button-outline" id="task-issue">Report issue</button>' : ''}${manager && hasPermission('tasks.manage') && t.status === 'Blocked' ? '<button class="button button-outline" id="task-unblock">Unblock task</button>' : ''}${manager && hasPermission('tasks.manage') && !['Completed','Cancelled'].includes(t.status) ? '<button class="button button-outline" id="task-cancel">Cancel task</button>' : ''}${manager && hasPermission('tasks.override') && ['Scheduled','Pending'].includes(t.status) ? '<button class="button button-outline" id="task-override-start">Override start</button>' : ''}</div></section>`;
    const perform = async (url, body) => { try { await api(url, { method: 'POST', ...(body ? { body } : {}) }); showToast('Task updated.'); renderTaskDetail(container, taskId); } catch (error) { document.querySelector('#task-detail-error').innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; } };
    document.querySelector('#task-start')?.addEventListener('click', () => perform(`/api/my/tasks/${taskId}/start`));
    document.querySelector('#task-complete')?.addEventListener('click', () => { const notes = prompt('Completion notes (optional):') || ''; perform(`/api/my/tasks/${taskId}/complete`, { notes, completedChecks: checks }); });
    document.querySelector('#task-issue')?.addEventListener('click', () => { const description = prompt('Describe the issue (at least 3 characters):'); if (description && description.trim().length >= 3) perform(`/api/my/tasks/${taskId}/issues`, { type: 'Other', description: description.trim() }); });
    document.querySelector('#task-unblock')?.addEventListener('click', () => { const note = prompt('What changed so this task can resume?') || ''; perform(`/api/tasks/${taskId}/unblock`, { note }); });
    document.querySelector('#task-cancel')?.addEventListener('click', () => { const reason = prompt('Reason for cancelling this task (at least 3 characters):'); if (reason && reason.trim().length >= 3) perform(`/api/tasks/${taskId}/cancel`, { reason: reason.trim() }); });
    document.querySelector('#task-override-start')?.addEventListener('click', () => { const reason = prompt('Audited override reason (at least 5 characters):'); if (reason && reason.trim().length >= 5) perform(`/api/tasks/${taskId}/override`, { action: 'Start', reason: reason.trim() }); });
    document.querySelector('#task-detail-assign')?.addEventListener('click', async () => { const workers = await api('/api/workers').catch(() => []); const choice = prompt(`Worker membership ID:\n${workers.filter(w=>w.isActive).map(w=>`${w.membershipId} — ${w.displayName}`).join('\n')}`); if (choice && /^[0-9a-f-]{36}$/i.test(choice.trim())) perform(`/api/tasks/${taskId}/assign`, { membershipId: choice.trim(), override: false }); });
  } catch (error) { container.innerHTML = `<section class="card"><h2>Could not load task</h2><p class="form-error">${escapeHtml(error.message)}</p><a href="#tasks">Back to daily work</a></section>`; }
}

async function renderShopSchedule(container) {
  if (!hasPermission('tasks.view')) { container.innerHTML = '<section class="card"><h2>Shop schedule</h2><p class="form-error">Your account cannot view the shop schedule.</p></section>'; return; }
  const start = new Date(); start.setHours(0,0,0,0); const startValue = start.toISOString();
  container.innerHTML = `<section class="card panel"><p><a href="#tasks">← Back to daily work</a></p><p class="eyebrow">Capacity-aware planning</p><h2>Shop-wide schedule</h2><p>Preview assigns work using the server's worker availability, skills, dependencies and capacity. Applying a plan revalidates task versions on the server.</p><form id="schedule-form"><div class="field"><label for="schedule-start">Start date</label><input id="schedule-start" name="start" type="date" required value="${startValue.slice(0,10)}"></div><div class="field"><label for="schedule-days">Planning horizon (days)</label><input id="schedule-days" name="days" type="number" min="1" max="366" value="14" required></div><button class="button button-primary" type="submit">Preview schedule</button></form><div id="schedule-results"></div></section>`;
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
    document.querySelector('#staff-invite-form').addEventListener('submit', async e => { e.preventDefault(); const f = e.currentTarget; const data = new FormData(f); const roleIds = data.getAll('roleIds'); const locationIds = data.getAll('locationIds'); const error = document.querySelector('#staff-invite-error'); if (!roleIds.length) { error.innerHTML = '<p class="form-error">Select at least one role.</p>'; return; } try { const invite = await api('/api/staff/invitations', { method: 'POST', body: { fullName: String(data.get('fullName')).trim(), email: String(data.get('email')).trim(), roleIds, locationIds } }); container.innerHTML = `<section class="card panel"><h2>Invitation created</h2><p>Invitation for ${escapeHtml(invite.email)} created. Expires ${escapeHtml(dateLabel(invite.expiresAtUtc))}.</p><p class="form-note">The invitation email depends on the configured email provider.</p><a class="button button-primary" href="#staff">Return to staff</a></section>`; } catch (error) { error.innerHTML = `<p class="form-error" role="alert">${escapeHtml(error.message)}</p>`; } });
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
    const measurements = await api(`/api/customers/${encodeURIComponent(customerId)}/measurements`).catch(() => null);
    const merged = customer.status === 'Merged';
    const measurementContent = measurements === null
      ? '<p class="form-note">Measurement profiles could not be loaded for this account.</p>'
      : Array.isArray(measurements) && measurements.length
        ? `<div class="table-wrap"><table class="data-table"><thead><tr><th>Profile</th><th>Template</th><th>Current revision</th><th>Updated</th></tr></thead><tbody>${measurements.map((set) => `<tr><td><a href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(set.id)}/history"><strong>${escapeHtml(set.label)}</strong></a><small>${escapeHtml(set.values?.length || 0)} recorded values</small></td><td>${escapeHtml(set.templateName)}</td><td>${Number(set.currentRevision) || 1}</td><td>${escapeHtml(dateLabel(set.updatedAtUtc))}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="empty-state">No measurement profiles recorded yet.</p>';
    const measurementAction = hasPermission('measurements.record') && !merged
      ? `<div class="account-line"><a class="button button-primary button-fit" href="#customers/${escapeHtml(customer.id)}/measurements/new">Record measurements</a></div>`
      : '';
    container.innerHTML = `<section class="panel"><p><a href="#customers">← Customers</a></p><div class="card panel"><div class="panel-head"><div><p class="eyebrow">Customer profile</p><h2>${escapeHtml(customer.fullName)}</h2><span class="pill">${escapeHtml(customer.status)}</span></div>${hasPermission('customers.manage') && !merged ? `<a class="button button-outline" href="#customers/${escapeHtml(customer.id)}/edit">Edit</a>` : ''}</div>${merged ? '<p class="form-error">This profile has been merged and cannot be edited.</p>' : ''}<dl class="detail-list"><dt>Email</dt><dd>${escapeHtml(customer.email || '—')}</dd><dt>Phone</dt><dd>${escapeHtml(customer.phone || '—')}</dd><dt>Address</dt><dd>${escapeHtml([customer.addressLine, customer.city].filter(Boolean).join(', ') || '—')}</dd><dt>Customer since</dt><dd>${escapeHtml(dateLabel(customer.createdAtUtc))}</dd><dt>Garment preferences</dt><dd>${escapeHtml(customer.garmentPreferences || '—')}</dd><dt>Notes</dt><dd class="pre-wrap">${escapeHtml(customer.notes || '—')}</dd></dl>${hasPermission('customers.manage') && !merged ? `<div class="account-line"><button class="button button-outline" id="toggle-customer-status" type="button">${customer.status === 'Inactive' ? 'Reactivate customer' : 'Mark inactive'}</button></div>` : ''}</div><div class="card panel"><div class="panel-head"><div><p class="eyebrow">Measurements</p><h2>Measurement profiles</h2><p class="lead">Profiles preserve revision history and can be linked to garment orders.</p></div></div>${measurementAction}${measurementContent}</div></section>`;
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
      const values = Array.isArray(revision.values) ? revision.values : [];
      const valueRows = values.map((value) => `<tr><td>${escapeHtml(value.label || value.key)}</td><td>${escapeHtml(value.value)} ${escapeHtml(value.unit || '')}</td></tr>`).join('');
      return `<article class="revision-card"><div class="panel-head"><div><h3>Revision ${Number(revision.revisionNumber) || '—'}${Number(revision.revisionNumber) === Number(set.currentRevision) ? ' <span class="pill">Current</span>' : ''}</h3><p>${escapeHtml(dateLabel(revision.recordedAtUtc))}</p></div></div>${revision.notes ? `<p class="pre-wrap">${escapeHtml(revision.notes)}</p>` : ''}<div class="table-wrap"><table class="data-table"><thead><tr><th>Measurement</th><th>Value</th></tr></thead><tbody>${valueRows || '<tr><td colspan="2">No values recorded.</td></tr>'}</tbody></table></div></article>`;
    }).join('') : '<p class="empty-state">No measurement revisions were found.</p>';
    container.innerHTML = `<section class="panel"><p><a href="#customers/${escapeHtml(customerId)}">← Back to ${escapeHtml(customer.fullName)}</a></p><div class="card panel"><div class="panel-head"><div><p class="eyebrow">Measurement history</p><h2>${escapeHtml(set.label)}</h2><p class="lead">${escapeHtml(set.templateName)} · Current revision ${Number(set.currentRevision) || 1}</p></div>${hasPermission('measurements.record') ? `<a class="button button-primary button-fit" href="#customers/${escapeHtml(customerId)}/measurements/${escapeHtml(setId)}/revise">Record revision</a>` : ''}</div><div class="revision-list">${revisionCards}</div></div></section>`;
  } catch (error) {
    container.innerHTML = `<section class="card"><h2>Could not load measurement history</h2><p class="form-error" role="alert">${escapeHtml(error.message || 'Try again shortly.')}</p><a href="#customers/${escapeHtml(customerId)}">Back to customer</a></section>`;
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

if ('serviceWorker' in navigator && (location.protocol === 'https:' || ['localhost', '127.0.0.1'].includes(location.hostname))) {
  navigator.serviceWorker.register('/sw.js').catch(() => showToast('Offline shell could not be enabled on this visit. Avzar can still work online.'));
}

checkSession();
