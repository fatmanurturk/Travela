import { test } from "node:test";
import assert from "node:assert/strict";
import { cities } from "./helpers/cities.mjs";
import { normalizeCityQuery, suggestCities, compareCityNames } from "../src/citySearch.ts";

test("Katalog 81 ilin tamamını tekil ve mevcut kimlikleriyle içerir", () => {
  // Independent completeness oracle in license plate order, not the application's ID order.
  const expected = "Adana|Adıyaman|Afyonkarahisar|Ağrı|Amasya|Ankara|Antalya|Artvin|Aydın|Balıkesir|Bilecik|Bingöl|Bitlis|Bolu|Burdur|Bursa|Çanakkale|Çankırı|Çorum|Denizli|Diyarbakır|Edirne|Elazığ|Erzincan|Erzurum|Eskişehir|Gaziantep|Giresun|Gümüşhane|Hakkari|Hatay|Isparta|Mersin|İstanbul|İzmir|Kars|Kastamonu|Kayseri|Kırklareli|Kırşehir|Kocaeli|Konya|Kütahya|Malatya|Manisa|Kahramanmaraş|Mardin|Muğla|Muş|Nevşehir|Niğde|Ordu|Rize|Sakarya|Samsun|Siirt|Sinop|Sivas|Tekirdağ|Tokat|Trabzon|Tunceli|Şanlıurfa|Uşak|Van|Yozgat|Zonguldak|Aksaray|Bayburt|Karaman|Kırıkkale|Batman|Şırnak|Bartın|Ardahan|Iğdır|Yalova|Karabük|Kilis|Osmaniye|Düzce".split("|");
  assert.equal(cities.length, 81);
  assert.equal(new Set(cities.map(c => c.id)).size, 81);
  assert.deepEqual(new Set(cities.map(c => c.name)), new Set(expected));
  assert.equal(cities.find(c => c.name === "İstanbul").id, 40);
  const sorted = [...cities].sort(compareCityNames);
  assert.deepEqual(suggestCities([...cities].reverse(), "").map(s => s.city), sorted);
  assert.ok(sorted.findIndex(c => c.name === "Iğdır") < sorted.findIndex(c => c.name === "İstanbul"));
  assert.ok(sorted.findIndex(c => c.name === "Kırıkkale") < sorted.findIndex(c => c.name === "Kilis"));
});

for (const [query, expected] of [["istanbul", "İstanbul"], ["ISTANBUL", "İstanbul"], ["sanliurfa", "Şanlıurfa"], ["ŞANLIURFA", "Şanlıurfa"], ["igdir", "Iğdır"], ["IĞDIR", "Iğdır"], ["izmir", "İzmir"], ["cankiri", "Çankırı"], ["guMuSHane", "Gümüşhane"], [" İSTANBUL ", "İstanbul"]]) {
  test(`Türkçe eşleşme: ${query} → ${expected}`, () => {
    const result = suggestCities(cities, query);
    assert.equal(result[0].city.name, expected);
    assert.equal(result[0].kind, "exact");
  });
}
for (const [query, expected] of [["adina", "Adana"], ["istnabul", "İstanbul"], ["ankra", "Ankara"]]) {
  test(`Yazım önerisi: ${query} → ${expected}`, () => {
    const result = suggestCities(cities, query);
    assert.equal(result[0].city.name, expected);
    assert.equal(result[0].kind, "similar");
  });
}
test("Tam, başlangıç, içerik, benzerlik önceliği ve Türkçe sıralama", () => {
  const sample = ["Ankara", "Kara", "Karaman", "Karabük", "Kars"].map((name, id) => ({ id, name }));
  assert.deepEqual(suggestCities(sample, "kara").map(s => [s.city.name, s.kind]), [
    ["Kara", "exact"], ["Karabük", "prefix"], ["Karaman", "prefix"], ["Ankara", "contains"], ["Kars", "similar"],
  ]);
  assert.equal(normalizeCityQuery("İIıi"), "iiii");
});
test("Kısa ve ilgisiz girdilerde benzerlik önerisi üretilmez", () => {
  for (const query of ["x", "zz", "zzz", "zzzz", "londra", "new york", "12345", "xyzabcdef", "ankaraxxxxx"]) {
    assert.deepEqual(suggestCities(cities, query), [], query);
  }
  for (const query of ["a", "an", "ist"]) assert.ok(suggestCities(cities, query).every(s => s.kind !== "similar"));
});
