import { money } from "./reservations";

export type Campaign = {
  id: number; companyId: number; companyName: string; routeId: number | null; routeName: string | null;
  name: string; description: string; code: string | null; discountType: "Percentage" | "Fixed"; value: number;
  minimumSubtotal: number; maximumDiscount: number | null; usageLimit: number | null; perUserLimit: number;
  startsAt: string; endsAt: string; departureFrom: string | null; departureUntil: string | null;
  isActive: boolean; isPublic: boolean; version: string; used: number; reserved: number; totalDiscount: number;
};
export type CampaignPrice = { originalAmount: number; discountAmount: number; amount: number; campaignId: number | null; campaignName: string | null; couponCode: string | null };
export const discountLabel = (c: Pick<Campaign, "discountType" | "value">) => c.discountType === "Percentage" ? `%${c.value} indirim` : `${money.format(c.value)} indirim`;
export function campaignStatus(c: Campaign) {
  if (!c.isActive) return "Pasif";
  if (new Date(c.startsAt).getTime() > Date.now()) return "Yakında";
  if (new Date(c.endsAt).getTime() <= Date.now()) return "Sona erdi";
  if (c.usageLimit != null && c.used + c.reserved >= c.usageLimit) return "Limit doldu";
  return "Aktif";
}
