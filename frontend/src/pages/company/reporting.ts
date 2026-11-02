export type Metrics = { sold: number; active: number; cancelled: number; collected: number; refunded: number; pendingRefund: number; failedRefundCount: number; failedRefundAmount: number; collectedMinusRefunded: number };
export type ReportTrip = Metrics & { id: number; routeId: number; departureCity: string; arrivalCity: string; departureTime: string; plate: string; capacity: number; activeSoldSeats: number; heldSeats: number; legacyBlockedSeats: number; otherEnvironmentBlocks: number; expiredHolds: number; availableForSale: number | null; occupancyPercent: number | null; dataWarning: string | null; status: string };
export type ReportDay = { date: string; sold: number; cancelled: number; collected: number; refunded: number };
export type Report = {
 scope: { from: string; to: string; environment: string; basis: string; generatedAt: string; dateExplanation: string };
 metrics: Metrics; occupancy: { activeSoldSeats: number; capacity: number; tripCount: number; invalidTrips: number; percent: number | null };
 daily: ReportDay[]; routes: (Metrics & { routeId: number; departureCity: string; arrivalCity: string })[]; routeCount: number;
 trips: ReportTrip[]; totalTrips: number; upcoming: ReportTrip[];
 recent: { orderId: string; tripId: number; pnr: string | null; type: string; date: string; amount: number; refundStatus: string }[];
 failedRefunds: { orderId: string; tripId: number; pnr: string | null; amount: number; error: string | null; statusDate: string | null }[];
 unknownRefundDateCount: number; unknownRefundDateAmount: number;
};
export const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
export const dateTime = new Intl.DateTimeFormat("tr-TR", { dateStyle: "short", timeStyle: "short", timeZone: "Europe/Istanbul" });
export const count = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 2 });
export const dateLabel = (date: string) => date.split("-").reverse().join(".");
export const reportHeaders = () => ({ Authorization: `Bearer ${sessionStorage.getItem("travela_token") ?? ""}` });
export const orderLink = (id: string, tripId: number) => `/company/reservations?orderId=${encodeURIComponent(id)}&tripId=${tripId}`;
export const tripStatus = (status: string) => ({ scheduled: "Planlandı", cancelled: "İptal Edildi", completed: "Tamamlandı", inprogress: "Devam Ediyor" }[status.toLowerCase()] ?? status);
