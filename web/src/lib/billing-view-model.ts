export type BillingPeriod = 'monthly' | 'annual';

export function priceForPeriod(plan: { monthlyPrice: number; annualPrice: number | null }, period: BillingPeriod): number | null {
  const value = period === 'annual' ? plan.annualPrice : plan.monthlyPrice;
  return value !== null && Number.isFinite(value) && value > 0 ? value : null;
}

export function canStartCheckout(price: number | null, busy: boolean): boolean {
  return !busy && price !== null && Number.isFinite(price) && price > 0;
}
