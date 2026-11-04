import { test } from "node:test";
import assert from "node:assert/strict";
import { buildSeats, storedSeats } from "../src/seatPlans.ts";

test("2+1 düzeninde koridor boşluğu ve tekli pencere koltuğu korunur", () => {
  const seats = buildSeats("2+1", 8, [3, 7]);
  assert.deepEqual(seats.map(s => s.column), [1, 2, 4, 1, 2, 4, 1, 2]);
  assert.equal(seats[2].isWindow, true); assert.equal(seats[2].isAisle, true);
  assert.deepEqual(seats.filter(s => s.isBlocked).map(s => s.number), [3, 7]);
  assert.equal(seats[7].row, 3);
});
test("2+2 düzeninde iki pencere ve iki koridor yanı bulunur", () => {
  const seats = buildSeats("2+2", 4);
  assert.deepEqual(seats.map(s => s.column), [1, 2, 4, 5]);
  assert.deepEqual(seats.map(s => s.isWindow), [true, false, false, true]);
  assert.deepEqual(seats.map(s => s.isAisle), [false, true, true, false]);
});
test("Eski kayda fiziksel konum yakıştırılmaz; bozuk veya aşırı kapasite çizilmez", () => {
  assert.ok(buildSeats("Unspecified", 6).every(s => !s.isWindow && !s.isAisle));
  for (const capacity of [0, -1, 1.5, 1001, NaN]) assert.deepEqual(buildSeats("2+2", capacity), []);
  assert.deepEqual(storedSeats("{broken", "2+1", 6), []);
});
