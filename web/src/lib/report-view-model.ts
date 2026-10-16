import { money } from '@/lib/format';

export type ReportMetrics = {
  currencyCode: string;
  ordersCreated: number;
  ordersDelivered: number;
  orderValue: number;
  verifiedRevenue: number;
  expenses: number;
  grossAfterExpenses: number;
  openTasks: number;
  completedTasks: number;
  overdueTasks: number;
};

/** Shared presentation model for dashboard and detailed reports. Keeps labels and revenue semantics consistent. */
export function dashboardMetricEntries(report: ReportMetrics): [string, string][] {
  return [
    ['Orders created', String(report.ordersCreated)],
    ['Orders delivered', String(report.ordersDelivered)],
    ['Order value', money(report.orderValue, report.currencyCode)],
    ['Verified revenue', money(report.verifiedRevenue, report.currencyCode)],
    ['Expenses', money(report.expenses, report.currencyCode)],
    ['Revenue less expenses', money(report.grossAfterExpenses, report.currencyCode)],
    ['Open production tasks', String(report.openTasks)],
    ['Overdue tasks', String(report.overdueTasks)],
  ];
}

export function reportMetricEntries(report: ReportMetrics): [string, string][] {
  return [
    ...dashboardMetricEntries(report).slice(0, 6),
    ['Open production tasks', String(report.openTasks)],
    ['Completed tasks in period', String(report.completedTasks)],
    ['Overdue tasks', String(report.overdueTasks)],
  ];
}
