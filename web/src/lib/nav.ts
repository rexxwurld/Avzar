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
  { href: '/customers', label: 'Customers', anyOf: ['customers.view'], ready: false },
  { href: '/orders', label: 'Orders', anyOf: ['orders.view'], ready: false },
  { href: '/tasks', label: 'Production', anyOf: ['tasks.view', 'tasks.work'], ready: false },
  { href: '/finance', label: 'Finance', anyOf: ['invoices.view', 'payments.view', 'expenses.view'], ready: false },
  { href: '/storefront', label: 'Storefront', anyOf: ['storefront.view'], ready: false },
  { href: '/staff', label: 'Staff & roles', anyOf: ['staff.view', 'roles.manage'], ready: false },
  { href: '/settings', label: 'Settings', anyOf: ['business.settings'], ready: false },
];

export function visibleNav(user: Pick<CurrentUser, 'isOwner' | 'permissions'>, items: NavItem[] = NAV): NavItem[] {
  return items.filter((i) => i.ready && (i.anyOf.length === 0 || user.isOwner || i.anyOf.some((p) => user.permissions.includes(p))));
}
