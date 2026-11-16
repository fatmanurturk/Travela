import { test } from "node:test";
import assert from "node:assert/strict";
import { journeyQuery, tripPath } from "../src/journeySelection.ts";
import { passengerReturnTo } from "../src/reservations.ts";
import { seatConflict } from "../src/seatCompatibility.ts";

test("Biniş/iniş seçimi sefer bağlantısında ve güvenli giriş dönüşünde korunur", () => {
  const path = tripPath(12, { boardingStopOrder: 2, alightingStopOrder: 4 });
  assert.equal(path, "/trips/12?boardingStopOrder=2&alightingStopOrder=4");
  assert.equal(passengerReturnTo(path), path);
  for (const value of ["/trips/12?boardingStopOrder=4&alightingStopOrder=2", path + "&next=https://evil.test", "//evil.test" + path]) assert.equal(passengerReturnTo(value), "/");
  assert.equal(journeyQuery({ boardingStopOrder: 2 }), "");
});

test("Komşunun kesim boyunca değişen cinsiyetlerinin tümü doğrulanır", () => {
  const plan = [{ number: 1, row: 1, column: 1 }, { number: 2, row: 1, column: 2 }];
  const occupants = [{ seatNumber: 1, gender: "Female" }, { seatNumber: 1, gender: "Male" }];
  assert.ok(seatConflict(plan, 2, "Female", occupants));
  assert.ok(seatConflict(plan, 2, "Male", occupants));
  assert.equal(seatConflict(plan, 2, "Female", occupants.slice(0,1)), "");
});
