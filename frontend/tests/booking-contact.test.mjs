import test from "node:test";
import assert from "node:assert/strict";
import { normalizeBookingPhone } from "../src/bookingContact.ts";

test("Türkiye telefon biçimleri satın alma ve sorgu sözleşmesinde aynı numaraya dönüşür", () => {
  for (const value of ["05321234567", "+905321234567", "532 123 45 67", "905321234567", "0090 (532) 123-4567"])
    assert.equal(normalizeBookingPhone(value), "+905321234567");
  for (const value of ["", "05321", "abc", "++905321234567", "555555555555555555555"])
    assert.equal(normalizeBookingPhone(value), null);
});
