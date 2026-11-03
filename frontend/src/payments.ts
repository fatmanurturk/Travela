export type Purchase = {
  id: string; pnr: string | null; kind: string; tripId: number; seatNumber: number;
  companyName: string; departureCity: string; arrivalCity: string; departureTime: string;
  passengerName: string; arrivalTime: string; paidAt: string | null; retrievedAt: string;
  cancelDeadline: string; holdExpiresAt: string | null; amount: number; currency: string;
  isTest: boolean; paymentStatus: string; ticketStatus: string; refundStatus: string;
  refundAmount: number; canCancel: boolean; cancellationReason: string | null;
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
  return path && /^\/(?:payment|test-payment)\/[a-f0-9-]{36}$/i.test(path) ? path : null;
}
