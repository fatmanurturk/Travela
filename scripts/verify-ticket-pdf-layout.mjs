import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tool = name => process.env.POPPLER_BIN
  ? path.join(process.env.POPPLER_BIN, name + (process.platform === "win32" ? ".exe" : "")) : name;
const run = (name, ...args) => execFileSync(tool(name), args, { encoding: "utf8", maxBuffer: 16 * 1024 * 1024 });
const compact = text => text.replace(/\s/g, "");
const manifest = JSON.parse(readFileSync(path.join(root, "tmp/pdfs/layout-qa/manifest.json"), "utf8"));
for (const { name, path: pdf, pages: count, ticket } of manifest) {
  const text = run("pdftotext", "-raw", "-enc", "UTF-8", pdf, "-");
  const flat = compact(text);
  // Exclude running headers/footers and read each column separately when a field spans pages.
  const crop = (x, width) => compact(run("pdftotext", "-raw", "-enc", "UTF-8", "-x", String(x), "-y", "70", "-W", String(width), "-H", "710", pdf, "-"));
  const regions = [flat, crop(40, 515), crop(40, 216), crop(257, 169), crop(40, 244), crop(312, 244)];
  for (const key of ["Pnr", "CompanyName", "PassengerName", "DepartureCity", "ArrivalCity", "DepartureTerminalName", "ArrivalTerminalName", "DepartureTerminalAddress", "ArrivalTerminalAddress", "BoardingInstructions", "RefundDescription", "CancellationReason"])
    if (ticket[key]) assert.ok(regions.some(region => region.includes(compact(ticket[key]))), `${name}: veri eksik/değişmiş: ${key}`);
  assert.ok(flat.includes(compact("Bu belge üretildiği andaki durumu gösterir. Güncel durum için Biletlerim sayfasını kontrol ediniz.")));
  assert.ok(flat.includes("Europe/Istanbul"));
  const amount = new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(ticket.Amount) + ticket.Currency;
  assert.ok(flat.includes("Ödenentutar" + amount), `${name}: kartta satın alma tutarı gösterilmiyor`);
  const fonts = run("pdffonts", pdf).split(/\r?\n/).slice(2).filter(Boolean);
  assert.ok(fonts.length > 0 && fonts.every(row => /yes\s+yes\s+yes/.test(row)), `${name}: font gömme/Unicode hatası`);
  const images = run("pdfimages", "-list", pdf);
  assert.equal((images.match(/\bimage\s+1726\s+911\b/g) ?? []).length, 1, `${name}: panorama bir kez ve orijinal ölçüleriyle gömülmeli`);
  assert.ok(/\bsmask\s+1726\s+911\b/.test(images), `${name}: şeffaflık maskesi eksik`);
  const bbox = run("pdftotext", "-bbox", "-enc", "UTF-8", pdf, "-");
  const pages = [...bbox.matchAll(/<page width="([\d.]+)" height="([\d.]+)">([\s\S]*?)<\/page>/g)];
  assert.equal(pages.length, count);
  for (const [index, page] of pages.entries()) {
    const width = Number(page[1]), height = Number(page[2]);
    assert.ok(Math.abs(width - 595.276) < 1 && Math.abs(height - 841.89) < 1);
    const words = [...page[3].matchAll(/<word xMin="([\d.-]+)" yMin="([\d.-]+)" xMax="([\d.-]+)" yMax="([\d.-]+)">([^<]*)<\/word>/g)]
      .map(w => ({ x: +w[1], y: +w[2], right: +w[3], bottom: +w[4], text: w[5] }));
    assert.ok(words.length > 0);
    for (const w of words)
      assert.ok(w.x >= 39 && w.y >= 30 && w.right <= width - 39 && w.bottom <= height - 20, `${name} s.${index + 1}: sayfa sınırını aşan metin: ${w.text}`);
    for (let i = 0; i < words.length; i++) for (let j = i + 1; j < words.length; j++) {
      const a = words[i], b = words[j];
      const overlapX = Math.min(a.right, b.right) - Math.max(a.x, b.x);
      const overlapY = Math.min(a.bottom, b.bottom) - Math.max(a.y, b.y);
      assert.ok(overlapX < .5 || overlapY < .5, `${name} s.${index + 1}: üst üste metin: ${a.text} / ${b.text}`);
    }
    assert.ok(compact(run("pdftotext", "-f", String(index + 1), "-l", String(index + 1), pdf, "-")).includes(`${index + 1}/${count}`));
  }
  console.log(`OK: ${name} (${count} sayfa): tüm metinler, A4, gömülü Türkçe fontlar, panorama/şeffaflık, taşma/çakışma ve sayfa numaraları`);
}
