import { test, expect } from "@playwright/test";

const base = { role: "Passenger", companyId: null, companyName: null, isActive: true, activationPending: false, passwordResetRequired: false, mustChangePassword: false, createdAt: "2026-11-01T08:00:00Z" };
const fixtures = [
  { ...base, id: 101, fullName: "Davetli Kullanıcı", email: "invited@example.com", activationPending: true },
  { ...base, id: 102, fullName: "Sıfırlama Bekleyen", email: "reset@example.com", passwordResetRequired: true },
  { ...base, id: 103, fullName: "İlk Giriş", email: "first@example.com", mustChangePassword: true },
  { ...base, id: 104, fullName: "Kapalı Kullanıcı", email: "disabled@example.com", isActive: false },
  { ...base, id: 105, fullName: "Aktif Kullanıcı", email: "active@example.com" },
];

async function setup(page) {
  await page.addInitScript(() => {
    sessionStorage.setItem("travela_token", "account-admin-token");
    sessionStorage.setItem("travela_user", JSON.stringify({ id: 1, role: "Admin", fullName: "Test Yöneticisi" }));
  });
  let users = structuredClone(fixtures);
  await page.route("**/api/admin/companies", route => route.fulfill({ json: [] }));
  await page.route("**/api/admin/dashboard", route => route.fulfill({ json: { totalUsers: 5, activeUsers: 4, passiveUsers: 1, companyUsers: 0 } }));
  await page.route("**/api/admin/users?*", route => {
    const status = new URL(route.request().url()).searchParams.get("status");
    const items = users.filter(user => status === "ActivationPending" ? user.activationPending : status === "PasswordResetRequired" ? user.passwordResetRequired : status === "MustChangePassword" ? user.mustChangePassword : true);
    return route.fulfill({ json: { items, totalCount: items.length, pageNumber: 1, pageSize: 10 } });
  });
  await page.route("**/api/admin/users/105/password-reset", route => {
    users = users.map(user => user.id === 105 ? { ...user, passwordResetRequired: true } : user);
    return route.fulfill({ json: { message: "Şifre yenileme e-postası gönderim kuyruğuna alındı." } });
  });
}

test("Yönetici listesi hesap engeli ve şifre/aktivasyon gereksinimlerini ayrı gösterir", async ({ page }) => {
  await setup(page);
  await page.goto("/admin/users");
  for (const label of ["Aktivasyon bekliyor", "Şifre sıfırlama bekliyor", "İlk şifre değişimi bekliyor", "Yönetici tarafından kapalı"])
    await expect(page.getByRole("table").getByText(label, { exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "Hesap Durumu", exact: true }).selectOption("ActivationPending");
  await expect(page.getByRole("table").getByRole("row")).toHaveCount(2);
  await expect(page.getByRole("table")).toContainText("Davetli Kullanıcı");
  await page.getByRole("button", { name: "Detay", exact: true }).click();
  await expect(page.locator(".modal-card")).toContainText("Aktivasyon bekliyor");
  await expect(page.locator(".modal-card .detail-grid > div").filter({ hasText: "Yönetici İzni" })).toContainText("Açık");
});

test("Sıfırlama başlatılınca liste güncellenir ve hesap yönetici izni açık kalır", async ({ page }) => {
  await setup(page);
  await page.goto("/admin/users");
  const row = page.getByRole("row").filter({ has: page.getByText("active@example.com", { exact: true }) });
  await row.getByRole("button", { name: "Detay", exact: true }).click();
  await page.getByRole("button", { name: "Şifre Sıfırlama", exact: true }).click();
  await expect(page.getByText("Şifre yenileme e-postası gönderim kuyruğuna alındı.", { exact: true })).toBeVisible();
  await expect(row).toContainText("Şifre sıfırlama bekliyor");
  await expect(row.getByRole("button", { name: "Pasifleştir", exact: true })).toBeVisible();
  await expect(page.locator(".modal-card")).toHaveCount(0);
});

test("Yeni davet yönetici engeli koymadan aktivasyon beklemesi oluşturur", async ({ page }) => {
  await setup(page);
  let body;
  await page.route("**/api/admin/users", async route => {
    body = route.request().postDataJSON();
    await route.fulfill({ status: 201, json: { message: "Kullanıcı oluşturuldu. Aktivasyon e-postası gönderim kuyruğuna alındı." } });
  });
  await page.goto("/admin/users");
  await page.getByRole("button", { name: "Yeni Kullanıcı Ekle", exact: true }).click();
  await page.getByLabel("Ad Soyad", { exact: false }).fill("Yeni Davetli");
  await page.getByLabel("E-posta", { exact: false }).fill("new@example.com");
  await page.getByRole("button", { name: "Kullanıcı Oluştur", exact: true }).click();
  await expect(page.getByText("Kullanıcı oluşturuldu. Aktivasyon e-postası gönderim kuyruğuna alındı.", { exact: true })).toBeVisible();
  expect(body.isActive).toBe(true);
  expect(body.password).toBeUndefined();
});
