import { test } from "node:test";
import assert from "node:assert/strict";
import { passengerReturnTo, tripStatusLabel, reservationNotice, reservationStatusLabel } from "../src/reservations.ts";

test("Girişten sonra seçilen sefer veya yolcunun rezervasyonlarına dönülür", () => {
  assert.equal(passengerReturnTo("/trips/42"), "/trips/42");
  assert.equal(passengerReturnTo("/reservations"), "/reservations");
  assert.equal(passengerReturnTo(null), "/");
});

test("Dönüş adresiyle dış siteye veya yönetici sayfasına yönlendirme yapılamaz", () => {
  for (const path of ["https://example.com", "//example.com", "/admin", "/trips/42/../../admin", "/trips/42?returnTo=//example.com"]) {
    assert.equal(passengerReturnTo(path), "/");
  }
});

test("Eski ve yeni sefer durumları yolcuya Türkçe gösterilir", () => {
  for (const status of ["Cancelled", "canceled", "iptal edildi"]) assert.equal(tripStatusLabel(status), "İptal edildi");
  assert.equal(tripStatusLabel("Scheduled"), "Planlandı");
  assert.equal(tripStatusLabel("InProgress"), "Devam ediyor");
  assert.equal(tripStatusLabel("Completed"), "Tamamlandı");
  assert.equal(tripStatusLabel("unknown"), "Rezervasyona kapalı");
});

test("Rezervasyon bildirimi ödeme alınmadığını ve bilet düzenlenmediğini açıklar", () => {
  assert.match(reservationNotice, /Ödeme alınmaz/);
  assert.match(reservationNotice, /ödenmiş bilet düzenlenmez/);
});

test("Rezervasyon durumu sefer durumundan ayrı ve Türkçe gösterilir", () => {
  assert.equal(reservationStatusLabel("Active"), "Aktif");
  assert.equal(reservationStatusLabel("Cancelled"), "İptal Edildi");
});
