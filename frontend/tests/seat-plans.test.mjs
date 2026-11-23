import { test } from "node:test";
import assert from "node:assert/strict";
import { buildSeats, storedSeats, isCoach41Plan, normalizeSeatPlan } from "../src/seatPlans.ts";
import { planError } from "../src/physicalSeatPlans.ts";
import { adjacentSeat } from "../src/seatCompatibility.ts";

test("41-seat 2+1 coach: exact numbering, door gap, rear bench and unchanged seat IDs", () => {
  const seats = buildSeats("2+1", 41, [22, 39]);
  assert.equal(seats.length, 41);
  assert.deepEqual(seats.map(s => s.number), Array.from({ length: 41 }, (_, i) => i + 1));
  const matrix = [1, 2, 3, 4].map(column => Array.from({ length: 14 }, (_, i) =>
    seats.find(s => s.row === i + 1 && s.column === column)?.number ?? 0));
  assert.deepEqual(matrix, [
    [3, 6, 9, 12, 15, 18, 21, 0, 25, 28, 31, 34, 37, 41],
    [2, 5, 8, 11, 14, 17, 20, 0, 24, 27, 30, 33, 36, 40],
    [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39],
    [1, 4, 7, 10, 13, 16, 19, 22, 23, 26, 29, 32, 35, 38],
  ]);
  assert.equal(isCoach41Plan(seats, "2+1"), true);
  assert.equal(planError(seats, "2+1", 41), "");
  assert.deepEqual(seats.filter(s => s.isBlocked).map(s => s.number), [22, 39]);
  assert.equal(adjacentSeat(seats, seats.find(s => s.number === 20)).number, 21);
  assert.equal(adjacentSeat(seats, seats.find(s => s.number === 24)).number, 25);
  for (const number of [1, 22, 23, 38, 39]) assert.equal(adjacentSeat(seats, seats.find(s => s.number === number)), undefined);
  // Old generated snapshots adopt the corrected geometry while keeping seat IDs and blocking.
  const old = seats.map(s => ({ ...s, row: Math.floor((s.number - 1) / 3) + 1, column: [1, 2, 4][(s.number - 1) % 3] }));
  const saved = JSON.stringify(old);
  assert.deepEqual(storedSeats(saved, "2+1", 41), seats);
  assert.equal(JSON.stringify(old), saved);
  assert.deepEqual(normalizeSeatPlan([...old].reverse(), "2+1"), [...seats].reverse());
  assert.equal(normalizeSeatPlan(seats, "2+1"), seats);
  assert.equal(normalizeSeatPlan(old, "Custom"), old);
  const custom = old.map(s => s.number === 1 ? { ...s, row: 15 } : s);
  assert.equal(normalizeSeatPlan(custom, "2+1"), custom);
  assert.equal(isCoach41Plan(old, "2+1"), false);
  assert.match(planError(seats.map(s => s.number === 22 ? { ...s, column: 1 } : s), "2+1", 41), /Koridor/);
});

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
