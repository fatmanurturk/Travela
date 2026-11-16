import { test } from "node:test";
import assert from "node:assert/strict";
import { isValidIdentityNumber } from "../src/identityNumber.ts";

test("T.C. kimlik numarası biçimi ve iki kontrol basamağı doğrulanır", () => {
  assert.equal(isValidIdentityNumber("10000000146"), true);
  for (const value of ["", "1000000014", "100000001460", "00000000000", "10000000136", "10000000147", "11111111111", "1000000014a", "１０００００００１４６", " 10000000146"]) {
    assert.equal(isValidIdentityNumber(value), false, value);
  }
});
