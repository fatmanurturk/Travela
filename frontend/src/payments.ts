import type { Journey } from "./seatPlans";
export type Purchase = Journey & {
  id: string; purchaseGroupId?: string | null; pnr: string | null; kind: string; tripId: number; seatNumber: number;
  companyName: string; departureCity: string; arrivalCity: string; departureTime: string;
  passengerName: string; arrivalTime: string; paidAt: string | null; retrievedAt: string;
  passengerEmail?: string | null; passengerPhone?: string | null; passengerCapturedAt?: string | null; passengerSnapshotSource?: string | null;
  passengerGender?: "Female" | "Male" | null;
  cancelDeadline: string; holdExpiresAt: string | null; amount: number; currency: string;
  originalAmount?: number; discountAmount?: number; campaignName?: string | null; couponCode?: string | null;
  isTest: boolean; provider?: string; paymentStatus: string; ticketStatus: string; refundStatus: string;
  legalAccepted?: boolean;
  refundAmount: number; canCancel: boolean; cancellationReason: string | null;
  changeCancellationUntil?: string | null; busPlateNumber?: string | null;
  refundStatusLabel: string; refundDescription: string;
  cancelledAt: string | null; lastError: string | null; needsAttention: boolean; checkoutUrl: string | null;
};
export function isIssuedTicket(item: Pick<Purchase, "pnr" | "kind" | "paymentStatus">): boolean {
  return Boolean(item.pnr && item.kind === "Ticket" && item.paymentStatus === "Paid");
}
export function paymentLabel(status: string): string {
  return ({ Pending: "Ödeme bekleniyor", Paid: "Ödeme doğrulandı", Failed: "Ödeme başarısız", Unpaid: "Ödeme alınmadı" } as Record<string, string>)[status] ?? "Kontrol ediliyor";
}
export function refundLabel(status: string): string {
  return ({ None: "İade yok", Pending: "İade bekleniyor", Succeeded: "İade tamamlandı", Failed: "İade tamamlanamadı" } as Record<string, string>)[status] ?? "Kontrol ediliyor";
}
export function refundMessage(status: string): string {
  return ({ None: "", Pending: "İadeniz bekleniyor.", Succeeded: "İadeniz tamamlandı.", Failed: "İade tamamlanamadı." } as Record<string, string>)[status] ?? "İade durumu kontrol ediliyor.";
}
export function statusTone(status: string): string {
  return ["Active", "Paid", "Succeeded"].includes(status) ? "success" : ["Cancelled", "Failed"].includes(status) ? "danger" : status === "Pending" ? "pending" : "neutral";
}
export function safeCheckoutPath(path: string | null): string | null {
  return path && /^\/(?:payment|test-payment|purchase)\/[a-f0-9-]{36}$/i.test(path) ? path : null;
}
export function safeProviderCheckout(value: string | null, provider = "IyzicoSandbox"): string | null {
  if (!value || !["IyzicoSandbox", "Iyzico"].includes(provider)) return null;
  const apiHost = provider === "Iyzico" ? "api.iyzipay.com" : "sandbox-api.iyzipay.com";
  const checkoutHost = provider === "Iyzico" ? "cpp.iyzipay.com" : "sandbox-cpp.iyzipay.com";
  try { const url = new URL(value); return url.protocol === "https:" && !url.username && !url.password && !url.hash && (!url.port || url.port === "443") &&
    ((url.hostname === apiHost && url.pathname.startsWith("/checkoutform/")) || (url.hostname === checkoutHost && url.pathname === "/" && url.searchParams.has("token"))) ? url.href : null; } catch { return null; }
}
