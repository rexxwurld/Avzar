/** Mirrors Tailoring.Application.Auth.CurrentUserDto. */
export interface CurrentUser {
  userId: string;
  email: string;
  fullName: string;
  businessId: string;
  businessName: string;
  timeZoneId: string;
  currencyCode: string;
  isOwner: boolean;
  permissions: string[];
}

export interface BusinessChoice { businessId: string; businessName: string }
