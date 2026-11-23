import { test, expect } from "@playwright/test";
import { cities } from "../helpers/cities.mjs";

async function setup(page) {
  const searches = [];
  await page.route("**/api/**", r => {
    const url = new URL(r.request().url());
    if (url.pathname === "/api/routes/city-options") return r.fulfill({ json: cities });
    if (url.pathname === "/api/trips/search") {
      searches.push(Object.fromEntries(url.searchParams));
      return r.fulfill({ json: { items: [], companies: [], totalCount: 0, totalPages: 0 } });
    }
    return r.fulfill({ json: [] });
  });
  return searches;
}
async function choose(page, label, query, name) {
  const input = page.getByRole("combobox", { name: label, exact: true });
  await input.fill(query);
  await page.getByRole("option", { name, exact: true }).click();
  await expect(input).toHaveValue(name);
}

test("İki alan da seferlerden bağımsız 81 ili Türkçe sıralar; liste kayar ve klavye seçer", async ({ page }) => {
  await setup(page); await page.goto("/");
  for (const label of ["Nereden", "Nereye"]) {
    const input = page.getByRole("combobox", { name: label, exact: true });
    await input.click();
    await expect(page.getByRole("option")).toHaveCount(81);
    expect(await page.getByRole("option").allTextContents()).toEqual(cities.map(c => c.name).sort((a,b) => a.localeCompare(b,"tr-TR")));
    const metrics = await page.locator(".city-popup").evaluate(el => ({ height: el.clientHeight, content: el.scrollHeight }));
    expect(metrics.content).toBeGreaterThan(metrics.height);
    await input.press("ArrowUp");
    const activeId = await input.getAttribute("aria-activedescendant");
    await expect(page.locator(`[id="${activeId}"]`)).toHaveText("Zonguldak");
    await expect(page.locator(`[id="${activeId}"]`)).toBeInViewport();
    await input.press("ArrowDown"); await input.press("Enter");
    await expect(input).toHaveValue("Adana"); await expect(input).toBeFocused();
    await expect(input).toHaveAttribute("aria-expanded", "false");
    await input.press("ArrowDown"); await input.press("Escape");
    await expect(page.getByRole("listbox")).toHaveCount(0);
  }
  await expect(page.getByRole("alert")).toContainText("Kalkış ve varış şehirleri aynı olamaz.");
});

for (const [query, expected, fuzzy] of [["istanbul","İstanbul",false], ["sanliurfa","Şanlıurfa",false], ["igdir","Iğdır",false], ["adina","Adana",true], ["istnabul","İstanbul",true], ["ankra","Ankara",true]]) {
  test(`Öneri açıkça seçilir: ${query} → ${expected}`, async ({ page }) => {
    await setup(page); await page.goto("/");
    const input = page.getByRole("combobox", { name: "Nereden", exact: true });
    await input.fill(query);
    await expect(page.getByRole("option").first()).toHaveText(expected);
    await expect(input).toHaveValue(query);
    if (fuzzy) await expect(page.getByText("Bunu mu demek istediniz?", { exact: true })).toBeVisible();
    await input.press("ArrowDown"); await input.press("Enter");
    await expect(input).toHaveValue(expected);
  });
}

test("İlgisiz giriş seçilmez; metin değişikliği ve aynı il araması engellenir", async ({ page }) => {
  const searches = await setup(page); await page.goto("/");
  await choose(page,"Nereden","ankra","Ankara"); await choose(page,"Nereye","igdir","Iğdır");
  const input = page.getByRole("combobox", { name: "Nereden", exact: true });
  await input.fill("Ankara "); await input.press("Escape");
  await page.getByRole("button", { name: "Sefer Ara" }).click();
  await expect(page.getByRole("alert")).toContainText("Öneri listesinden bir kalkış ili seçin.");
  expect(searches).toHaveLength(0);
  for (const query of ["x", "zz", "londra", "12345"]) {
    await input.fill(query); await expect(page.getByRole("option")).toHaveCount(0);
    await expect(page.getByText("Eşleşen il bulunamadı. Yazımı kontrol edin.")).toBeVisible();
    await input.press("Enter"); await expect(page).toHaveURL(/\/$/);
  }
  await choose(page,"Nereden","igdir","Iğdır");
  await page.getByRole("button", { name: "Sefer Ara" }).click();
  await expect(page.getByRole("alert")).toContainText("Kalkış ve varış şehirleri aynı olamaz.");
  expect(searches).toHaveLength(0);
});

test("Seferi olmayan iller aranır; takas, tarih, yolcu, gidiş-dönüş ve sonuç formu korunur", async ({ page }) => {
  const searches = await setup(page); await page.goto("/");
  await choose(page,"Nereden","igdir","Iğdır"); await choose(page,"Nereye","sanliurfa","Şanlıurfa");
  await page.getByRole("button", { name: "Kalkış ve varış şehirlerini değiştir" }).click();
  await expect(page.getByRole("combobox", { name: "Nereden", exact: true })).toHaveValue("Şanlıurfa");
  await expect(page.getByRole("combobox", { name: "Nereye", exact: true })).toHaveValue("Iğdır");
  await page.getByRole("button", { name: "Gidiş-dönüş", exact: true }).click();
  await page.getByLabel("Gidiş tarihi").fill("2099-12-17");
  await page.getByLabel("Dönüş tarihi").fill("2099-12-16");
  await page.getByLabel("Yolcu sayısı").fill("3");
  await page.getByRole("button", { name: "Sefer Ara" }).click();
  await expect(page.getByText("Dönüş tarihi gidiş tarihinden önce olamaz.")).toBeVisible();
  await page.getByLabel("Dönüş tarihi").fill("2099-12-20");
  await page.getByRole("button", { name: "Sefer Ara" }).click();
  await expect(page.getByRole("heading", { name: "Seçtiğiniz güzergâh ve tarih için sefer bulunamadı." })).toHaveCount(2);
  await expect(page.locator(".result-card")).toHaveCount(0);
  expect(searches).toEqual(expect.arrayContaining([
    expect.objectContaining({ departureCity:"Şanlıurfa", arrivalCity:"Iğdır", departureCityId:"71", arrivalCityId:"38", date:"2099-12-17", passengers:"3" }),
    expect.objectContaining({ departureCity:"Iğdır", arrivalCity:"Şanlıurfa", departureCityId:"38", arrivalCityId:"71", date:"2099-12-20", passengers:"3" }),
  ]));
  await page.locator(".change-search-panel summary").click();
  await expect(page.getByRole("combobox", { name:"Nereden", exact:true })).toHaveValue("Şanlıurfa");
  await choose(page,"Nereye","ankra","Ankara");
  await page.getByRole("button", { name:"Sefer Ara" }).click();
  await expect(page).toHaveURL(/to=Ankara/);
  await expect(page.getByRole("heading", { name: "Seçtiğiniz güzergâh ve tarih için sefer bulunamadı." })).toHaveCount(2);
});

for (const width of [320,390,768,1440]) test(`Liste alanın altında ve ekran içinde açılır: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height:800 }); await setup(page); await page.goto("/");
  for (const label of ["Nereden","Nereye"]) {
    const input = page.getByRole("combobox", { name:label, exact:true });
    await input.click(); await expect(page.getByRole("option")).toHaveCount(81);
    const rect = await input.boundingBox(); const popup = await page.locator(".city-popup").boundingBox();
    expect(popup.y).toBeGreaterThanOrEqual(rect.y + rect.height);
    expect(popup.x).toBeGreaterThanOrEqual(0);
    expect(popup.x + popup.width).toBeLessThanOrEqual(width);
    expect(popup.y + popup.height).toBeLessThanOrEqual(800);
    expect(popup.height).toBeGreaterThan(44);
    await page.screenshot({ path:`../tmp/city-search/${width}-${label}.png` });
    await input.press("Escape");
  }
  await expect(page.getByRole("button", { name:"Kalkış ve varış şehirlerini değiştir" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
});

test("Dokunarak seçim yapılır", async ({ browser }) => {
  const context = await browser.newContext({ hasTouch:true, viewport:{ width:390,height:844 } });
  const page = await context.newPage(); await setup(page); await page.goto("http://127.0.0.1:5178/");
  await page.getByRole("combobox", { name:"Nereden",exact:true }).fill("sanliurfa");
  await page.getByRole("option", { name:"Şanlıurfa",exact:true }).tap();
  await expect(page.getByRole("combobox", { name:"Nereden",exact:true })).toHaveValue("Şanlıurfa");
  await expect(page.getByRole("listbox")).toHaveCount(0); await context.close();
});

test("Katalog yükleme hatası ve eksik veri görünür, yeniden deneme çalışır", async ({ page }) => {
  await setup(page); let phase = "error";
  await page.route("**/api/routes/city-options", r => r.fulfill(phase === "error"
    ? { status:503,json:{} } : phase === "incomplete" ? { json:cities.slice(0,80) } : { json:cities }));
  await page.goto("/");
  await expect(page.getByRole("alert")).toContainText("İl listesi yüklenemedi.");
  phase = "incomplete";
  await page.getByRole("button", { name:"İlleri yeniden yükle" }).click();
  await expect(page.getByRole("alert")).toContainText("İl listesi eksik veya geçersiz.");
  phase = "complete";
  await page.getByRole("button", { name:"İlleri yeniden yükle" }).click();
  await page.getByRole("combobox", { name:"Nereden",exact:true }).click();
  await expect(page.getByRole("option")).toHaveCount(81);
  await expect(page.getByRole("alert")).toHaveCount(0);
});
