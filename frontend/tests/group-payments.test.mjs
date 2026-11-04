import { test } from "node:test";
import assert from "node:assert/strict";
import { safeProviderCheckout, safeCheckoutPath } from "../src/payments.ts";
import { passengerReturnTo } from "../src/reservations.ts";
test("Ortak sipariş bağlantısı yerel ve giriş dönüşü izinli kalır", () => {
  const path = "/purchase/12345678-1234-1234-1234-123456789012";
  assert.equal(safeCheckoutPath(path), path); assert.equal(passengerReturnTo(path), path);
  assert.equal(safeCheckoutPath("https://evil.test"), null);
});
test("Yalnızca HTTPS iyzico sandbox ödeme adresleri açılır", () => {
  const url = "https://sandbox-cpp.iyzipay.com/?token=sandbox";
  assert.equal(safeProviderCheckout(url), url);
  for (const value of ["http://sandbox-cpp.iyzipay.com/?token=x", "https://sandbox-cpp.iyzipay.com.evil.test/?token=x", "https://user:pass@sandbox-cpp.iyzipay.com/?token=x", "javascript:alert(1)", "https://sandbox-cpp.iyzipay.com/", "https://api.iyzipay.com/checkoutform/x", "//sandbox-cpp.iyzipay.com/?token=x"]) assert.equal(safeProviderCheckout(value), null);
});
