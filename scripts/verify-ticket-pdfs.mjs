import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const executable = name => process.env.POPPLER_BIN
  ? path.join(process.env.POPPLER_BIN, name + (process.platform === "win32" ? ".exe" : "")) : name;
const run = (name, args) => execFileSync(executable(name), args, { encoding: "utf8" });
const normalize = value => value.replace(/\u00ad/g, "").replace(/-\s*\r?\n\s*/g, "").replace(/\s+/g, " ").trim();
const turkeyTime = value => {
  const parts = new Intl.DateTimeFormat("tr-TR", { timeZone: "Europe/Istanbul", day: "2-digit", month: "long", year: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).formatToParts(new Date(value));
  const part = name => parts.find(p => p.type === name)?.value;
  return `${part("day")} ${part("month")} ${part("year")} ${part("hour")}:${part("minute")}`;
};
const cases = [
  ["active", "output/pdf/ticket-active-test.pdf"],
  ["cancelled", "output/pdf/ticket-cancelled-test.pdf"],
  ["pending", "tmp/ticket-qa/ticket-refund-pending.pdf"],
  ["failed", "tmp/ticket-qa/ticket-refund-failed.pdf"],
];
for (const [state, relative] of cases) {
  const expected = JSON.parse(readFileSync(path.join(root, "tmp/ticket-qa", state + ".json"), "utf8"));
  const pdf = path.join(root, relative);
  const text = normalize(run("pdftotext", ["-layout", "-enc", "UTF-8", pdf, "-"]));
  for (const value of [expected.pnr, expected.passengerName, expected.companyName, expected.departureCity, expected.arrivalCity, expected.refundDescription]) {
    assert.ok(text.includes(normalize(value)), `${state}: PDF içeriği API ile eşleşmiyor: ${value}`);
  }
  assert.ok(!text.includes("TEST BİLETİ"));
  assert.equal(text.includes("İPTAL EDİLDİ"), state !== "active");
  assert.ok(text.includes(new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(expected.amount) + " " + expected.currency));
  assert.ok(text.includes("Koltuk " + expected.seatNumber));
  for (const value of [expected.departureTime, expected.arrivalTime, expected.retrievedAt, state === "active" ? expected.cancelDeadline : expected.cancelledAt])
    assert.ok(text.includes(turkeyTime(value)), `${state}: Türkiye saati API ile eşleşmiyor: ${value}`);
  assert.ok(text.includes("Bu belge üretildiği andaki durumu gösterir"));
  if (state !== "active") assert.ok(text.includes(normalize(expected.cancellationReason)));
  if (state === "cancelled") assert.ok(text.includes("Tam iade tamamlandı"));
  if (state === "pending") assert.ok(text.includes("İade talebi kaydedildi, sonuç bekleniyor"));
  if (state === "failed") assert.ok(text.includes(normalize(expected.lastError)));
  if (state !== "cancelled") assert.ok(!text.includes("Tam iade tamamlandı"));
  const bbox = run("pdftotext", ["-bbox", "-enc", "UTF-8", pdf, "-"]);
  const pages = [...bbox.matchAll(/<page width="([\d.]+)" height="([\d.]+)">([\s\S]*?)<\/page>/g)];
  assert.ok(pages.length > 0, `${state}: PDF sayfa ölçümleri okunamadı`);
  for (const page of pages) {
    const width = Number(page[1]), height = Number(page[2]);
    const words = [...page[3].matchAll(/<word xMin="([\d.-]+)" yMin="([\d.-]+)" xMax="([\d.-]+)" yMax="([\d.-]+)">([^<]*)<\/word>/g)];
    assert.ok(words.length > 0, `${state}: sayfada ölçülebilen metin bulunamadı`);
    for (const word of words) {
      assert.ok(Number(word[1]) >= 8 && Number(word[2]) >= 8 && Number(word[3]) <= width - 8 && Number(word[4]) <= height - 8, `${state}: sayfa dışında/kesilen metin: ${word[5]}`);
    }
  }
  console.log(`OK: ${state} PDF - API metinleri, Türkçe, PNR, test/iptal ibaresi, güncel iade ve sayfa sınırları`);
}
