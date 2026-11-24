import { test, expect } from "@playwright/test";
const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/1cAAAAASUVORK5CYII=", "base64");
const original = { id: 1, fullName: "Ali Yılmaz", duty: "Driver", phone: "05321234567", isActive: true, hasPhoto: false, careerStartYear: 2015, experienceYears: 11, employmentStartDate: "2020-01-01", languages: ["Türkçe"], licenseClasses: ["B", "D"], vehicleTypes: ["Otobüs"], documents: [] };
const docs = [
  { id: 1, name: "Ehliyet", expiresOn: "2026-12-24", validity: { status: "Expiring", label: "Süresi yaklaşıyor", days: 30, message: "30 gün kaldı" } },
  { id: 2, name: "İlk yardım", expiresOn: "2026-11-24", validity: { status: "Expiring", label: "Süresi yaklaşıyor", days: 0, message: "Bugün sona eriyor" } },
  { id: 3, name: "Eğitim", expiresOn: "2026-11-23", validity: { status: "Expired", label: "Süresi dolmuş", days: -1, message: "1 gün önce süresi doldu" } },
  { id: 4, name: "Mesleki belge", expiresOn: null, isIndefinite: true, validity: { status: "Indefinite", label: "Süresiz", days: null, message: "Süresiz belge" } },
].map(d => ({ issuedOn: "2026-01-01", isIndefinite: false, documentType: "Professional", fileName: "sertifika.png", contentType: "image/png", size: png.length, ...d }));
function field(request, name) { return request.postDataBuffer().toString().match(new RegExp(`name="${name}"\\r\\n\\r\\n([\\s\\S]*?)\\r\\n--`))?.[1]; }
async function setup(page, withDocs = false) {
  await page.clock.install({ time: new Date("2026-11-24T12:00:00+03:00") });
  const state = { person: { ...structuredClone(original), documents: withDocs ? structuredClone(docs) : [] }, creates: [], filters: [], fail: false, deletes: 0 };
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "profile-ui"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Company", name: "Yetkili" })); });
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname, method = request.method();
    if (path === "/api/company/profile") return route.fulfill({ json: { name: "Test Firma" } });
    if (path === "/api/company/staff" && method === "GET") { state.filters.push(url.search); const filter = url.searchParams.get("documentStatus"); return route.fulfill({ json: !filter || state.person.documents.some(d => d.validity.status === filter) ? [state.person] : [] }); }
    if (path === "/api/company/staff/1" && method === "GET") return route.fulfill({ json: state.person });
    if (path.endsWith("/profile") && ["POST", "PUT"].includes(method)) {
      if (state.fail) return route.fulfill({ status: 400, json: { message: "Fotoğraf dosyası doğrulanamadı. Tekrar deneyin." } });
      const profile = JSON.parse(field(request, "profile")); state.creates.push(profile); const raw = request.postDataBuffer().toString();
      Object.assign(state.person, profile); state.person.experienceYears = profile.careerStartYear ? 2026 - profile.careerStartYear : null; if (raw.includes('name="photo"; filename=')) state.person.hasPhoto = true; else if (field(request, "removePhoto") === "true") state.person.hasPhoto = false;
      return route.fulfill({ status: method === "POST" ? 201 : 200, json: { id: 1 } });
    }
    if (path === "/api/company/staff/1/photo" || path.endsWith("/file")) { expect(request.headers().authorization).toBe("Bearer profile-ui"); return route.fulfill({ body: png, contentType: "image/png" }); }
    if (/\/documents/.test(path)) {
      if (state.fail) return route.fulfill({ status: 400, json: { message: "Belge yüklenemedi. Lütfen yeniden deneyin." } });
      if (method === "DELETE") { expect(url.searchParams.get("confirm")).toBe("true"); state.deletes++; state.person.documents = state.person.documents.filter(d => d.id !== Number(path.split("/").at(-1))); return route.fulfill({ json: {} }); }
      const doc = { ...docs[0], id: method === "POST" ? 10 : Number(path.split("/").at(-1)), name: field(request, "name"), documentType: field(request, "documentType"), issuedOn: field(request, "issuedOn"), expiresOn: field(request, "expiresOn") ?? null, isIndefinite: field(request, "isIndefinite") === "true" };
      if (doc.isIndefinite) doc.validity = docs[3].validity;
      state.person.documents = [...state.person.documents.filter(d => d.id !== doc.id), doc]; return route.fulfill({ status: method === "POST" ? 201 : 200, json: { id: doc.id } });
    }
    return route.fulfill({ json: [] });
  });
  return state;
}
for (const width of [390, 1440]) test(`Profil alanları, fotoğraf ve muavin deneyimleri: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); const state = await setup(page); await page.goto("/company/staff");
  await page.getByRole("button", { name: "Personel ekle", exact: true }).click(); const form = page.getByRole("form", { name: "Personel kaydı" });
  await form.getByLabel("Ad soyad", { exact: true }).fill("Ayşe Muavin"); await form.getByLabel("Görev", { exact: true }).selectOption("Attendant");
  await form.getByLabel("Mesleğe başlama yılı").fill("2018"); await expect(form.getByText("Deneyim: 8 yıl")).toBeVisible(); await expect(form.getByLabel("Mesleğe başlama yılı")).toHaveAttribute("max", "2026");
  await form.getByLabel("İşe başlama tarihi").fill("2022-03-01"); await expect(form.getByLabel("İşe başlama tarihi")).toHaveAttribute("max", "2026-11-24");
  await form.getByLabel("Acil durumda aranacak kişi").fill("Can Yılmaz"); await form.getByLabel("Yakınlık derecesi").fill("Kardeşi"); await form.getByLabel("Acil durum telefonu").fill("05321112233");
  await form.getByLabel("Türkçe", { exact: true }).check(); await form.getByLabel("İngilizce", { exact: true }).check(); await form.getByLabel("Servis deneyimi", { exact: true }).check(); await form.getByLabel("Yolcu iletişimi deneyimi", { exact: true }).check(); await form.getByLabel("Servis deneyimi açıklaması (isteğe bağlı)").fill("İki yıl servis");
  await form.getByLabel("Profil fotoğrafı", { exact: true }).setInputFiles({ name: "photo.png", mimeType: "image/png", buffer: png }); await expect(form.getByAltText("Personel profil fotoğrafı")).toBeVisible();
  state.fail = true; await form.getByRole("button", { name: "Personeli kaydet" }).click(); await expect(form.getByRole("alert")).toContainText("doğrulanamadı"); await expect(form.getByLabel("Ad soyad", { exact: true })).toHaveValue("Ayşe Muavin");
  state.fail = false; await form.getByRole("button", { name: "Personeli kaydet" }).click(); await expect(form).toHaveCount(0);
  expect(state.creates[0]).toMatchObject({ duty: "Attendant", careerStartYear: 2018, languages: ["Türkçe", "İngilizce"], serviceExperience: true, baggageExperience: false, passengerCommunicationExperience: true });
  const profile = page.getByRole("region", { name: "Personel profili" }); await expect(profile).toContainText("Can Yılmaz"); await expect(profile.getByAltText("Personel profil fotoğrafı")).toBeVisible();
  await profile.getByRole("button", { name: "Profili düzenle" }).click(); await form.getByLabel("Profil fotoğrafı", { exact: true }).setInputFiles({ name: "replacement.png", mimeType: "image/png", buffer: png }); await form.getByRole("button", { name: "Personeli kaydet" }).click(); await expect(form).toHaveCount(0);
  await profile.getByRole("button", { name: "Profili düzenle" }).click(); await form.getByRole("button", { name: "Fotoğrafı kaldır" }).click(); await expect(form.getByText("Fotoğraf yok", { exact: true })).toBeVisible(); await form.getByRole("button", { name: "Personeli kaydet" }).click(); await expect(form).toHaveCount(0); expect(state.person.hasPhoto).toBe(false);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1); await page.screenshot({ path: `../tmp/staff/profile-${width}.png`, fullPage: true });
});
for (const width of [390, 1440]) test(`Belgeler, uyarılar, filtreler ve onaylı silme: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); const state = await setup(page, true); await page.goto("/company/staff?detail=1");
  const section = page.getByRole("region", { name: "Belgeler", exact: true }); await expect(section).toContainText("30 gün kaldı"); await expect(section).toContainText("Bugün sona eriyor"); await expect(section).toContainText("1 gün önce süresi doldu"); await expect(section).toContainText("Süresiz");
  await page.getByLabel("Belge durumuna göre filtrele").selectOption("Expired"); await expect(page.locator(".staff-card")).toHaveCount(1); expect(state.filters.at(-1)).toContain("documentStatus=Expired");
  const doc = section.locator(".staff-document").filter({ has: page.getByRole("heading", { name: "Ehliyet", exact: true }) });
  await doc.getByRole("button", { name: "Görüntüle", exact: true }).click(); const preview = page.getByRole("dialog", { name: "Ehliyet" }); await expect(preview.getByAltText("Ehliyet")).toBeVisible(); await preview.getByRole("button", { name: "Önizlemeyi kapat" }).click();
  const download = page.waitForEvent("download"); await doc.getByRole("button", { name: "İndir", exact: true }).click(); expect((await download).suggestedFilename()).toBe("sertifika.png");
  await section.getByRole("button", { name: "Belge ekle", exact: true }).click(); const form = page.getByRole("form", { name: "Belge kaydı" });
  await form.getByLabel("Belge adı", { exact: true }).fill("Yeni sertifika"); await form.getByLabel("Düzenlenme tarihi", { exact: true }).fill("2026-01-01"); await form.getByLabel("Süresiz belge", { exact: true }).check(); await expect(form.getByLabel("Son geçerlilik tarihi")).toHaveCount(0);
  await form.getByLabel("Belge dosyası", { exact: true }).setInputFiles({ name: "certificate.png", mimeType: "image/png", buffer: png }); state.fail = true;
  await form.getByRole("button", { name: "Belgeyi kaydet" }).click(); await expect(section.getByRole("alert")).toContainText("Belge yüklenemedi"); await expect(form.getByLabel("Belge adı", { exact: true })).toHaveValue("Yeni sertifika");
  state.fail = false; await form.getByRole("button", { name: "Belgeyi kaydet" }).click(); await expect(form).toHaveCount(0); const added = section.locator(".staff-document").filter({ hasText: "Yeni sertifika" }); await expect(added).toContainText("Süresiz");
  await added.getByRole("button", { name: "Belgeyi düzenle" }).click(); await form.getByLabel("Belge adı", { exact: true }).fill("Güncel sertifika"); await form.getByRole("button", { name: "Belgeyi kaydet" }).click(); await expect(form).toHaveCount(0);
  page.once("dialog", dialog => dialog.dismiss()); await section.locator(".staff-document").filter({ hasText: "Güncel sertifika" }).getByRole("button", { name: "Belgeyi sil" }).click(); expect(state.deletes).toBe(0);
  page.once("dialog", dialog => dialog.accept()); await section.locator(".staff-document").filter({ hasText: "Güncel sertifika" }).getByRole("button", { name: "Belgeyi sil" }).click(); await expect(section.getByText("Belge silindi.", { exact: true })).toBeVisible(); expect(state.deletes).toBe(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1); await page.screenshot({ path: `../tmp/staff/documents-${width}.png`, fullPage: true });
});
