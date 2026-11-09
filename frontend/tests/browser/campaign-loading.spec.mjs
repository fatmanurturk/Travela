import { test, expect } from "@playwright/test";

// Isolated UI fixtures only; these routes never change the application's API or database.
const options = { companies: [{ id: 1, name: "Test Firma" }], routes: [] };
const campaign = { id: 1, companyId: 1, companyName: "Test Firma", routeId: null, routeName: null,
  name: "Yolculuk fırsatı", description: "", code: "YOL20", discountType: "Percentage", value: 20,
  minimumSubtotal: 0, maximumDiscount: null, usageLimit: 50, perUserLimit: 1,
  startsAt: "2020-01-01T00:00:00Z", endsAt: "2099-01-01T00:00:00Z", departureFrom: null, departureUntil: null,
  isActive: true, isPublic: true, version: "AAAAAAAAAAA=", used: 3, reserved: 0, totalDiscount: 150 };

async function session(page) {
  await page.addInitScript(() => {
    sessionStorage.setItem("travela_token", "campaign-ui-test-token");
    sessionStorage.setItem("travela_user", JSON.stringify({ role: "Company", fullName: "Firma Kullanıcısı" }));
  });
  await page.route("**/api/company/profile", r => r.fulfill({ json: { name: "Test Firma" } }));
}

test("Yükleme sırasında istatistik ve boş sonuç gösterilmez; StrictMode iptalleri hata olmaz", async ({ page }) => {
  await session(page);
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const failures = [];
  page.on("requestfailed", request => { if (request.url().includes("/api/campaigns")) failures.push(request.failure()?.errorText); });
  await page.route("**/api/campaigns", async r => { await gate; await r.fulfill({ json: [] }).catch(() => {}); });
  await page.route("**/api/campaigns/options", async r => { await gate; await r.fulfill({ json: options }).catch(() => {}); });
  await page.goto("/company/campaigns");
  await expect(page.getByRole("status")).toHaveText("Kampanyalar yükleniyor...");
  await expect(page.getByRole("region", { name: "Kampanya istatistikleri" })).toHaveCount(0);
  await expect(page.getByRole("table")).toHaveCount(0);
  await expect(page.getByRole("alert")).toHaveCount(0);
  release();
  await expect(page.getByRole("table")).toContainText("Henüz kampanya oluşturulmamış");
  await expect(page.getByRole("region", { name: "Kampanya istatistikleri" })).toContainText("Aktif kampanya0");
  await expect(page.getByRole("alert")).toHaveCount(0);
  // The app remains inside React StrictMode. Its development replay aborts the obsolete pair.
  expect(failures.some(error => error?.includes("ABORTED"))).toBe(true);
});

for (const failingPath of ["/api/campaigns", "/api/campaigns/options"]) {
  test(`${failingPath} 404: hata boş sonuç değildir; tekrar dene iki kaynağı da yeniden yükler`, async ({ page }) => {
    await session(page); let failing = true; const requests = [];
    for (const path of ["/api/campaigns", "/api/campaigns/options"]) {
      await page.route(`**${path}`, r => {
        requests.push({ path, method: r.request().method() });
        return failing && path === failingPath ? r.fulfill({ status: 404, body: "" })
          : r.fulfill({ json: path.endsWith("options") ? options : [] });
      });
    }
    await page.goto("/company/campaigns");
    await expect(page.getByRole("alert")).toContainText("HTTP 404");
    await expect(page.getByRole("alert")).toContainText(failingPath.endsWith("options") ? "seçeneklerinin" : "listesinin");
    await expect(page.getByRole("table")).toHaveCount(0);
    await expect(page.getByRole("region", { name: "Kampanya istatistikleri" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "+ Yeni kampanya" })).toBeDisabled();
    const before = requests.length; failing = false;
    await page.getByRole("button", { name: "Tekrar dene", exact: true }).click();
    await expect(page.getByRole("table")).toContainText("Henüz kampanya oluşturulmamış");
    await expect(page.getByRole("alert")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "+ Yeni kampanya" })).toBeEnabled();
    expect(requests.slice(before).map(r => r.path).sort()).toEqual(["/api/campaigns", "/api/campaigns/options"]);
    expect(requests.every(r => r.method === "GET")).toBe(true);
  });
}

test("Başarılı veri yenilenirken hata alınırsa eski istatistikler ve kayıtlar gerçek sonuç gibi gösterilmez", async ({ page }) => {
  await session(page); let failing = false;
  await page.route("**/api/campaigns/options", r => r.fulfill({ json: options }));
  await page.route("**/api/campaigns", r => failing ? r.fulfill({ status: 503, body: "" }) : r.fulfill({ json: [campaign] }));
  await page.goto("/company/campaigns");
  await expect(page.getByRole("table")).toContainText(campaign.name);
  await expect(page.getByRole("region", { name: "Kampanya istatistikleri" })).toContainText("Tamamlanan kullanım3");
  failing = true; await page.getByRole("button", { name: "Yenile", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("HTTP 503");
  await expect(page.getByRole("table")).toHaveCount(0);
  await expect(page.getByRole("region", { name: "Kampanya istatistikleri" })).toHaveCount(0);
  failing = false; await page.getByRole("button", { name: "Tekrar dene", exact: true }).click();
  await expect(page.getByRole("table")).toContainText(campaign.name);
});
