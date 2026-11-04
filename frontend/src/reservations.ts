export type Reservation = {
  reservationNumber: string;
  tripId: number;
  seatNumber: number;
  price: number;
  createdAt: string;
  companyName: string;
  departureCity: string;
  arrivalCity: string;
  departureTime: string;
  arrivalTime: string;
  tripStatus: string;
  status: string;
  cancelledAt: string | null;
  cancellationReason: string | null;
  canCancel: boolean;
  isPaid: boolean;
};

export function reservationStatusLabel(status: string): string {
  return status === "Cancelled" ? "İptal Edildi" : "Aktif";
}

export const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
export const dateTime = new Intl.DateTimeFormat("tr-TR", {
  dateStyle: "long", timeStyle: "short", timeZone: "Europe/Istanbul",
});
export const reservationNotice = "Bu işlem yalnızca koltuk rezervasyonudur. Ödeme alınmaz ve ödenmiş bilet düzenlenmez.";

export function currentRole(): string | null {
  try { return JSON.parse(sessionStorage.getItem("travela_user") ?? "null")?.role ?? null; }
  catch { return null; }
}

// Limit login destinations to known local passenger routes (no external redirect).
export function passengerReturnTo(value: string | null): string {
  return value && (/^\/trips\/\d+$/.test(value) || /^\/tickets\/[a-f0-9]{32}$/i.test(value) || (value === "/tickets" || value === "/reservations" || /^\/(?:payment|test-payment|purchase)\/[a-f0-9-]{36}$/i.test(value))) ? value : "/";
}

export function tripStatusLabel(value: string): string {
  switch (value.toLowerCase()) {
    case "scheduled": case "planlandı": case "planlandi": return "Planlandı";
    case "cancelled": case "canceled": case "iptal": case "iptal edildi": return "İptal edildi";
    case "completed": case "tamamlandı": return "Tamamlandı";
    case "inprogress": case "in progress": case "devam ediyor": return "Devam ediyor";
    default: return "Rezervasyona kapalı";
  }
}
