import type { CurrentUser } from './auth-types';

export interface NavItem {
  href: string;
  label: string;
  /** Visible when the user holds ANY of these permissions (owners always see it). Empty = everyone. */
  anyOf: string[];
  /** false = the page does not exist yet; hidden so the menu never leads to a 404. Flip as each slice ships. */
  ready: boolean;
}

export const NAV: NavItem[] = [
  { href: '/dashboard', label: 'Dashboard', anyOf: [], ready: true },
  { href: '/customers', label: 'Customers', anyOf: ['customers.view'], ready: true },
  { href: '/orders', label: 'Orders', anyOf: ['orders.view'], ready: true },
  { href: '/tasks', label: 'Production', anyOf: ['tasks.view', 'tasks.work'], ready: true },
  { href: '/tasks/schedule', label: 'Shop schedule', anyOf: ['tasks.view'], ready: true },
  { href: '/workers/schedule', label: 'Worker schedules', anyOf: ['tasks.view'], ready: true },
  { href: '/finance', label: 'Finance', anyOf: ['invoices.view', 'payments.view', 'expenses.view'], ready: true },
  { href: '/inventory', label: 'Inventory', anyOf: ['inventory.view', 'inventory.manage'], ready: true },
  { href: '/ai-studio', label: 'AI studio', anyOf: ['designs.manage', 'storefront.edit'], ready: true },
  { href: '/reports', label: 'Reports', anyOf: ['reports.view'], ready: true },
  { href: '/storefront', label: 'Storefront', anyOf: ['storefront.view'], ready: true },
  { href: '/staff', label: 'Staff & roles', anyOf: ['staff.view'], ready: true },
  { href: '/settings', label: 'Settings', anyOf: ['business.settings'], ready: true },
];

export function visibleNav(user: Pick<CurrentUser, 'isOwner' | 'permissions'>, items: NavItem[] = NAV): NavItem[] {
  return items.filter((i) => i.ready && (i.anyOf.length === 0 || user.isOwner || i.anyOf.some((p) => user.permissions.includes(p))));
}
