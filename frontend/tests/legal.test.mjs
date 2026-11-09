import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { LEGAL_VERSION, REFUND_NOTICE, legalAcceptances } from "../src/legal.ts";

const catalog = JSON.parse(readFileSync(new URL("../src/pages/legal/documents.v1.json", import.meta.url), "utf8"));

test("Hukuki taslaklar sürümlü, tarihli ve açık yer tutuculudur", () => {
  assert.equal(catalog.notice, "Hukuki inceleme gerektirir");
  assert.equal(catalog.version, LEGAL_VERSION);
  assert.equal(new Set(catalog.documents.map(document => document.type)).size, 8);
  assert.match(JSON.stringify(catalog), /\[ŞİRKET ADI\]/);
  assert.match(JSON.stringify(catalog), /\[MERSİS NUMARASI\]/);
});

test("Kayıt ve satış payloadları tam belge türünü ve güncel sürümü içerir", () => {
  assert.deepEqual(legalAcceptances("PrivacyNotice", "TermsOfUse"), [
    { documentType: "PrivacyNotice", version: LEGAL_VERSION, accepted: true },
    { documentType: "TermsOfUse", version: LEGAL_VERSION, accepted: true },
  ]);
  assert.equal(legalAcceptances("DistanceSales")[0].version, catalog.version);
});

test("İade metinleri CanCancel'ın tam iki saat dahil kuralını açıklar", () => {
  assert.match(REFUND_NOTICE, /en az 2 saat/);
  assert.match(REFUND_NOTICE, /tam 2 saat dahil/);
  for (const type of ["DistanceSales", "RefundPolicy"]) {
    const text = JSON.stringify(catalog.documents.find(document => document.type === type));
    assert.match(text, /tam 2 saat dahil/);
    assert.match(text, /2 saatten az/);
    assert.match(text, /sefer değişikliği/i);
  }
});
