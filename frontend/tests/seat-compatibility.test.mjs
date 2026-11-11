import { test } from "node:test";
import assert from "node:assert/strict";
import { adjacentSeat, seatConflict } from "../src/seatCompatibility.ts";
import { buildSeats } from "../src/seatPlans.ts";
const plan = buildSeats("2+2", 8);
test("Kadın ve erkek eş koltuk uyumu iki yönde denetlenir", () => {
  for (const gender of ["Female", "Male"]) {
    const occupied = [{ seatNumber: 1, gender, kind: "Ticket", expiresAt: null }];
    assert.equal(seatConflict(plan, 2, gender, occupied), "");
    assert.match(seatConflict(plan, 2, gender === "Female" ? "Male" : "Female", occupied), /yalnızca/);
    assert.equal(seatConflict(plan, 3, gender === "Female" ? "Male" : "Female", occupied), "");
  }
});
test("Aynı sepet, tekli koltuk ve bilinmeyen eski cinsiyet", () => {
  assert.match(seatConflict(plan, 2, "Male", [], { 1: "Female" }), /kadın/);
  assert.equal(seatConflict(buildSeats("2+1", 6), 3, "Male", [{ seatNumber: 2, gender: "Female" }]), "");
  assert.match(seatConflict(plan, 2, "Female", [{ seatNumber: 1, gender: null }]), /doğrulanamadığı/);
  assert.equal(seatConflict(plan, 1, "Female", []), "");
});
test("Komşuluk numaradan bağımsız gerçek konumdan gelir", () => {
  const custom = plan.map((s, i) => ({ ...s, number: [8, 4, 1, 7, 2, 6, 3, 5][i] }));
  assert.equal(adjacentSeat(custom, custom[0]).number, 4);
  assert.match(seatConflict(custom, 8, "Male", [{ seatNumber: 4, gender: "Female" }]), /kadın/);
});
