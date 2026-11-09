import { test } from "node:test";
import assert from "node:assert/strict";
import { parseRouteList, parseRouteDetail, totalRouteTrips, routeHttpError } from "../src/routeManagementData.ts";

const route = { id: 9, departureCity: "İstanbul", arrivalCity: "Ankara", distanceKm: 450, estimatedDurationMinutes: 360,
  isActive: true, hasTrips: true, tripCount: 2, plannedTripCount: 1 };
test("Sefer sayıları sayısal sözleşmeyle toplanır; rakam içeren string sayıya çevrilir", () => {
  const list = parseRouteList([route, { ...route, id: 10, tripCount: "3", plannedTripCount: "0" }]);
  assert.equal(totalRouteTrips(list), 5); assert.equal(typeof list[1].tripCount, "number");
  assert.equal(totalRouteTrips(parseRouteList([])), 0);
  assert.equal(totalRouteTrips(parseRouteList([{ ...route, tripCount: 0, plannedTripCount: 0 }])), 0);
});
test("Eksik ve bozuk sefer sayıları sessizce 0 olmaz; NaN üretilmeden sözleşme hatası bildirilir", () => {
  for (const tripCount of [undefined, null, "", "geçersiz", "2.5", -1, NaN, Infinity, true, {}, 1.5])
    assert.throws(() => parseRouteList([{ ...route, tripCount }]), /tripCount/);
  assert.throws(() => parseRouteList([route, route]), /birden fazla/);
});
test("Detay yanlış güzergâhı veya aynı seferi iki kez kabul etmez; varış tarihi zorunludur", () => {
  const trip = { id: 12, departureTime: "2099-01-01T07:00:00Z", arrivalTime: "2099-01-01T13:00:00Z", busPlateNumber: "34 TEST", price: 500, status: "Scheduled" };
  const detail = { route, trips: [trip], pageNumber: 1, totalPages: 1 };
  assert.equal(parseRouteDetail(detail, 9).route.id, 9);
  assert.throws(() => parseRouteDetail(detail, 10), /eşleşmiyor/);
  assert.throws(() => parseRouteDetail({ ...detail, trips: [trip, trip] }, 9), /tutarsız/);
  assert.throws(() => parseRouteDetail({ ...detail, trips: [{ ...trip, arrivalTime: undefined }] }, 9), /eksik/);
});
test("401, 403, eksik endpoint ve bulunamayan kayıt ayrı mesajlarla bildirilir", () => {
  assert.match(routeHttpError(401, null), /Oturum/);
  assert.match(routeHttpError(403, null), /yetkiniz/);
  assert.match(routeHttpError(404, null), /Backend/);
  assert.match(routeHttpError(404, { code: "route_not_found" }), /Listeyi yenileyip/);
  assert.match(routeHttpError(500, { message: "Veri alınamadı." }), /Veri alınamadı/);
});
