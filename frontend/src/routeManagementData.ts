export type RouteRecord = {
  id: number; departureCity: string; arrivalCity: string; distanceKm: number | null;
  estimatedDurationMinutes: number | null; isActive: boolean; tripCount: number;
  plannedTripCount: number; hasTrips: boolean;
};
export type RouteDetail = {
  route: RouteRecord; pageNumber: number; totalPages: number;
  trips: { id: number; departureTime: string; arrivalTime: string; busPlateNumber: string; price: number; status: string }[];
};

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("Güzergâh API yanıtı geçersiz. Lütfen tekrar deneyin.");
  return value as Record<string, unknown>;
}

function count(value: unknown, label: string): number {
  const parsed = typeof value === "number" ? value : typeof value === "string" && /^\d+$/.test(value.trim()) ? Number(value) : NaN;
  if (!Number.isSafeInteger(parsed) || parsed < 0)
    throw new Error(`${label} API yanıtında eksik veya geçersiz. Backend sürümünü kontrol edip listeyi yeniden yükleyin.`);
  return parsed;
}

function routeRecord(value: unknown): RouteRecord {
  const route = object(value);
  const id = count(route.id, "Güzergâh ID");
  if (id === 0 || typeof route.departureCity !== "string" || typeof route.arrivalCity !== "string" ||
      typeof route.isActive !== "boolean" || typeof route.hasTrips !== "boolean")
    throw new Error("Güzergâh API yanıtı güncel veri sözleşmesiyle uyumlu değil. Backend sürümünü kontrol edin.");
  return { ...route, id, tripCount: count(route.tripCount, `Güzergâh #${id} sefer sayısı (tripCount)`),
    plannedTripCount: count(route.plannedTripCount, `Güzergâh #${id} planlanmış sefer sayısı`) } as RouteRecord;
}

export function parseRouteList(value: unknown): RouteRecord[] {
  if (!Array.isArray(value)) throw new Error("Güzergâh listesi API yanıtında bulunamadı.");
  const routes = value.map(routeRecord);
  if (new Set(routes.map(route => route.id)).size !== routes.length)
    throw new Error("API aynı güzergâhı birden fazla kez döndürdü. Liste yeniden yüklenmelidir.");
  totalRouteTrips(routes);
  return routes;
}

export function totalRouteTrips(routes: RouteRecord[]): number {
  const total = routes.reduce((sum, route) => sum + count(route.tripCount, "Sefer sayısı (tripCount)"), 0);
  return count(total, "Toplam sefer sayısı");
}

export function parseRouteDetail(value: unknown, expectedId: number): RouteDetail {
  const detail = object(value);
  const route = routeRecord(detail.route);
  if (route.id !== expectedId || !Array.isArray(detail.trips)) throw new Error("Güzergâh detay yanıtı istenen kayıtla eşleşmiyor.");
  const trips = detail.trips.map(value => {
    const trip = object(value);
    if (typeof trip.departureTime !== "string" || typeof trip.arrivalTime !== "string" ||
        !Number.isFinite(Date.parse(trip.departureTime)) || !Number.isFinite(Date.parse(trip.arrivalTime)) ||
        typeof trip.busPlateNumber !== "string" || typeof trip.price !== "number" || !Number.isFinite(trip.price) || typeof trip.status !== "string")
      throw new Error("Güzergâhın sefer detayları eksik veya geçersiz. Listeyi yeniden yükleyin.");
    return { ...trip, id: count(trip.id, "Sefer ID") } as RouteDetail["trips"][number];
  });
  if (new Set(trips.map(trip => trip.id)).size !== trips.length || trips.length > route.tripCount)
    throw new Error("Güzergâh sefer listesi ve sefer sayısı tutarsız. Listeyi yeniden yükleyin.");
  const pageNumber = count(detail.pageNumber, "Sefer sayfa numarası"), totalPages = count(detail.totalPages, "Sefer sayfa sayısı");
  if (pageNumber < 1 || totalPages < pageNumber) throw new Error("Güzergâh sefer sayfalaması geçersiz.");
  return { route, trips, pageNumber, totalPages };
}

export function routeHttpError(status: number, body: { message?: string; code?: string } | null): string {
  if (status === 401) return "Oturumunuz sona erdi veya geçersiz. Lütfen tekrar giriş yapın.";
  if (status === 403) return "Bu güzergâhı veya sefer bilgilerini görüntüleme yetkiniz bulunmuyor.";
  if (status === 404) return body?.code === "route_not_found" ? "Güzergâh bulunamadı. Listeyi yenileyip tekrar deneyin."
    : "Güzergâh API adresi bulunamadı. Backend'in güncel sürümle çalıştığını kontrol edin.";
  return body?.message || `Güzergâh işlemi tamamlanamadı (${status}). Lütfen tekrar deneyin.`;
}

export function formatRouteDate(value: string): string {
  return new Date(value.endsWith("Z") || /[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`)
    .toLocaleString("tr-TR", { timeZone: "Europe/Istanbul" });
}
