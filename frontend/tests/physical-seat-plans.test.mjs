import { test } from "node:test";
import assert from "node:assert/strict";
import { buildSeats } from "../src/seatPlans.ts";
import { planError, seatPositionFlags } from "../src/physicalSeatPlans.ts";
import { adjacentSeat } from "../src/seatCompatibility.ts";

test("46 koltuk için iki düzen de kapasiteyi korur, düzen seçimi zorunludur", () => {
  assert.match(planError([], "", 46), /düzenini seçiniz/);
  for (const layout of ["2+1", "2+2"]) { const seats = buildSeats(layout, 46); assert.equal(seats.length, 46); assert.equal(planError(seats, layout, 46), ""); assert.ok(seats.every(s => s.column !== 3)); }
});
test("Numara, fiziksel konum, koridor ve kapasite hataları görünür", () => {
  const seats = buildSeats("2+2", 46);
  assert.match(planError(seats.slice(1), "2+2", 46), /46 koltuk/);
  assert.match(planError(seats.map((s, i) => i === 1 ? { ...s, number: 1 } : s), "2+2", 46), /numaraları benzersiz/);
  assert.match(planError(seats.map((s, i) => i === 1 ? { ...s, column: 1 } : s), "2+2", 46), /fiziksel konuma/);
  assert.match(planError(seats.map((s, i) => i === 1 ? { ...s, column: 3 } : s), "2+2", 46), /Koridor/);
  assert.match(planError(seats, "2+1", 46), /geçersiz/);
});
test("Koltuk taşınırsa fiziksel eş ve pencere bilgisi yeniden hesaplanır", () => {
  const seats = seatPositionFlags(buildSeats("2+2", 4).map(s => s.number === 2 ? { ...s, row: 2 } : s));
  assert.equal(adjacentSeat(seats, seats[0]), undefined);
  assert.equal(seats[0].isAisle, true);
  assert.equal(adjacentSeat(seats, seats[2]).number, 4);
});
