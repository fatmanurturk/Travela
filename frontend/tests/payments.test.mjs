import { test } from "node:test";
import assert from "node:assert/strict";
import { paymentLabel, refundLabel, safeCheckoutPath } from "../src/payments.ts";
import { passengerReturnTo } from "../src/reservations.ts";

test("İade talebi ile tamamlanmış iade birbirinden ayrılır", () => {
  assert.equal(refundLabel("Pending"), "İade Bekleniyor");
  assert.equal(refundLabel("Succeeded"), "İade Tamamlandı");
  assert.equal(refundLabel("Failed"), "İade Başarısız");
  assert.equal(paymentLabel("Failed"), "Ödeme başarısız");
});
test("Ödeme ekranı ve giriş dönüşü yalnızca izin verilen yerel adrese gider", () => {
  const path = "/test-payment/12345678-1234-1234-1234-123456789012";
  assert.equal(safeCheckoutPath(path), path);
  assert.equal(passengerReturnTo(path), path);
  assert.equal(passengerReturnTo("/tickets"), "/tickets");
  for (const value of ["//example.com", "https://example.com", "/test-payment/../admin", "javascript:alert(1)"]) assert.equal(safeCheckoutPath(value), null);
});
