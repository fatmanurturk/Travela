export type Purchase = {
  id: string; pnr: string | null; kind: string; tripId: number; seatNumber: number;
  companyName: string; departureCity: string; arrivalCity: string; departureTime: string;
  cancelDeadline: string; holdExpiresAt: string | null; amount: number; currency: string;
  isTest: boolean; paymentStatus: string; ticketStatus: string; refundStatus: string;
  refundAmount: number; canCancel: boolean; cancellationReason: string | null;
  cancelledAt: string | null; lastError: string | null; needsAttention: boolean; checkoutUrl: string | null;
};
export function paymentLabel(status: string): string {
  return ({ Pending: "Ödeme bekleniyor", Paid: "Ödeme doğrulandı", Failed: "Ödeme başarısız", Unpaid: "Ödeme alınmadı" } as Record<string, string>)[status] ?? "Kontrol ediliyor";
}
export function refundLabel(status: string): string {
  return ({ None: "İade yok", Pending: "İade Bekleniyor", Succeeded: "İade Tamamlandı", Failed: "İade Başarısız" } as Record<string, string>)[status] ?? "Kontrol ediliyor";
}
export function safeCheckoutPath(path: string | null): string | null {
  return path && /^\/test-payment\/[a-f0-9-]{36}$/i.test(path) ? path : null;
}
