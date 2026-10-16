/** Mirror Tailoring.Application.Orders DTOs (read side only; costs live behind pricing.view and are not shown here). */
export const ORDER_STATUSES = ['Draft', 'Quoted', 'Confirmed', 'InProduction', 'Ready', 'Delivered', 'Cancelled'] as const;
export type OrderStatus = (typeof ORDER_STATUSES)[number];

export interface OrderSummary {
  id: string;
  orderNumber: string;
  status: string;
  customerId: string;
  customerName: string;
  promisedDate: string; // yyyy-MM-dd
  priority: string;
  total: number;
  currencyCode: string;
  garmentCount: number;
}

export interface OrderGarment {
  id: string;
  lineNumber: number;
  name: string;
  category: string | null;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  fabric: string | null;
  colour: string | null;
  styleInstructions: string | null;
  alterationNotes: string | null;
  measurementRevision: number | null;
  designName: string | null;
}

export interface ProductionProgress { totalTasks: number; completed: number; inProgress: number; blocked: number; overdue: number; percentComplete: number }

export interface Order {
  id: string;
  orderNumber: string;
  status: string;
  customerId: string;
  customerName: string;
  promisedDate: string;
  priority: string;
  deliveryMethod: string;
  deliveryAddress: string | null;
  deliveryInstructions: string | null;
  notes: string | null;
  currencyCode: string;
  subtotal: number;
  discount: number;
  total: number;
  createdAtUtc: string;
  quotedAtUtc: string | null;
  confirmedAtUtc: string | null;
  readyAtUtc: string | null;
  deliveredAtUtc: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  garments: OrderGarment[];
  progress: ProductionProgress | null;
}
