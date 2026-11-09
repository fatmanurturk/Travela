import { test, expect } from "@playwright/test";

const token = "A".repeat(64);
const message = "Bilgileriniz uygun bir hesapla eşleşiyorsa e-posta bağlantısı gönderilecektir. Gelen kutunuzu ve gereksiz e-posta klasörünü kontrol edin.";

test("Giriş ekranından şifremi unuttum ve aktivasyon isteğine gidilir", async ({ page }) => {
  let body;
  await page.route("**/api/auth/forgot-password", async route => {
    body = route.request().postDataJSON(); await route.fulfill({ json: { message } });
  });
  await page.goto("/login");
  await page.getByRole("link", { name: "Şifremi unuttum", exact: true }).click();
  await page.getByLabel("E-posta", { exact: true }).fill("test@example.com");
  await page.getByRole("button", { name: "Bağlantı gönder", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText(message);
  expect(body).toEqual({ email: "test@example.com" });
  await page.getByRole("link", { name: "Aktivasyon bağlantısı iste", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Hesap aktivasyonu", exact: true })).toBeVisible();
});

test("Kayıt sonrası aktivasyon açıklaması ve e-posta görünür; yeniden istek kullanıcı tarafından başlatılır", async ({ page }) => {
  let resends = 0;
  await page.route("**/api/auth/register", route => route.fulfill({ status: 201, json: { requiresActivation: true } }));
  await page.route("**/api/auth/resend-activation", route => { resends++; return route.fulfill({ json: { message } }); });
  await page.goto("/register");
  await page.getByLabel("Ad soyad", { exact: true }).fill("Ayşe Yılmaz");
  await page.getByLabel("E-posta", { exact: true }).fill("ayse@example.com");
  await page.getByLabel("Şifre", { exact: true }).fill("RecoveryPass123!");
  await page.getByLabel("Şifre Tekrar", { exact: true }).fill("RecoveryPass123!");
  await page.getByRole("checkbox", { name: "Gizlilik ve KVKK aydınlatma metnini okudum" }).check();
  await page.getByRole("checkbox", { name: "Kullanım koşullarını kabul ediyorum" }).check();
  await page.getByRole("button", { name: "Kayıt Ol", exact: true }).click();
  await expect(page).toHaveURL(/\/resend-activation$/);
  await expect(page.getByRole("status")).toContainText("Hesabınız oluşturuldu");
  await expect(page.getByLabel("E-posta", { exact: true })).toHaveValue("ayse@example.com");
  expect(resends).toBe(0);
  await page.getByRole("button", { name: "Bağlantı gönder", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText(message);
  expect(resends).toBe(1);
});

for (const width of [360, 1440]) test(`Şifre yenileme bağlantısı korunur ve yeni şifre gönderilir: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 });
  await page.addInitScript(() => {
    sessionStorage.setItem("travela_token", "old-session"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger" }));
  });
  let completeBody;
  let inspected;
  await page.route("**/api/auth/account-link", async route => {
    inspected = route.request().postDataJSON(); await route.fulfill({ json: { purpose: "reset", requiresPassword: true } });
  });
  await page.route("**/api/auth/reset-password", async route => {
    completeBody = route.request().postDataJSON(); await route.fulfill({ json: { message: "Şifre başarıyla güncellendi." } });
  });
  await page.goto(`/reset-password#token=${token}`);
  await expect(page.getByLabel("Yeni şifre", { exact: true })).toBeVisible();
  expect(inspected).toEqual({ token }); expect(completeBody).toBeUndefined();
  await expect(page).toHaveURL(/\/reset-password$/);
  expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBe("old-session");
  await page.getByLabel("Yeni şifre", { exact: true }).fill("RecoveryPass456!");
  await page.getByLabel("Yeni şifre tekrar", { exact: true }).fill("Mismatch456!");
  await page.getByRole("button", { name: "Şifremi yenile", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("eşleşmelidir"); expect(completeBody).toBeUndefined();
  await page.getByLabel("Yeni şifre tekrar", { exact: true }).fill("RecoveryPass456!");
  await page.getByRole("button", { name: "Şifremi yenile", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Şifre başarıyla güncellendi.");
  expect(completeBody).toEqual({ token, newPassword: "RecoveryPass456!", confirmPassword: "RecoveryPass456!" });
  expect(await page.evaluate(() => sessionStorage.getItem("travela_token"))).toBeNull();
  expect(await page.evaluate(() => JSON.stringify(sessionStorage))).not.toContain(token);
  const size = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth }));
  expect(size.width).toBeLessThanOrEqual(size.viewport + 1);
  await page.getByRole("link", { name: "Giriş yap", exact: true }).click(); await expect(page).toHaveURL(/\/login$/);
});

test("Kayıt aktivasyonu şifreyi tekrar istemez ve kullanıcı onayı olmadan tamamlanmaz", async ({ page }) => {
  let activated;
  await page.route("**/api/auth/account-link", route => route.fulfill({ json: { purpose: "activation", requiresPassword: false } }));
  await page.route("**/api/auth/activate-account", async route => {
    activated = route.request().postDataJSON(); await route.fulfill({ json: { message: "Hesabınız aktifleştirildi. Giriş yapabilirsiniz." } });
  });
  await page.goto(`/activate-account#token=${token}`);
  await expect(page.getByRole("button", { name: "Hesabımı aktifleştir", exact: true })).toBeVisible();
  expect(activated).toBeUndefined(); await expect(page.getByLabel("Yeni şifre", { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Hesabımı aktifleştir", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Hesabınız aktifleştirildi"); expect(activated).toEqual({ token });
});

test("Şifresiz davette yeni şifre belirlenir", async ({ page }) => {
  let activated;
  await page.route("**/api/auth/account-link", route => route.fulfill({ json: { purpose: "activation", requiresPassword: true } }));
  await page.route("**/api/auth/activate-account", async route => {
    activated = route.request().postDataJSON(); await route.fulfill({ json: { message: "Hesabınız aktifleştirildi." } });
  });
  await page.goto(`/activate-account#token=${token}`);
  await page.getByLabel("Yeni şifre", { exact: true }).fill("InvitePass123!");
  await page.getByLabel("Yeni şifre tekrar", { exact: true }).fill("InvitePass123!");
  await page.getByRole("button", { name: "Hesabımı aktifleştir", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Hesabınız aktifleştirildi.");
  expect(activated.newPassword).toBe("InvitePass123!");
});

test("Süresi dolan ve eksik bağlantı yeni bağlantı isteğine yönlendirir", async ({ page }) => {
  await page.route("**/api/auth/account-link", route => route.fulfill({ status: 400, json: { message: "Bağlantının süresi dolmuş. Yeni bir bağlantı isteyin." } }));
  await page.goto(`/activate-account#token=${token}`);
  await expect(page.getByRole("alert")).toContainText("süresi dolmuş");
  await page.getByRole("link", { name: "Yeni bağlantı iste", exact: true }).click();
  await expect(page).toHaveURL(/\/resend-activation$/);
  await page.goto("/reset-password");
  await expect(page.getByRole("alert")).toContainText("Bağlantı eksik");
  await expect(page.getByRole("button", { name: "Şifremi yenile", exact: true })).toHaveCount(0);
});

test("İstek sınırı ve ağ hatasından sonra yeniden deneme mümkündür", async ({ page }) => {
  let count = 0;
  await page.route("**/api/auth/forgot-password", route => {
    count++;
    return count === 1 ? route.abort("failed") : count === 2 ? route.fulfill({ status: 429, json: { message: "Bir dakika sonra tekrar deneyin." } }) : route.fulfill({ json: { message } });
  });
  await page.goto("/forgot-password");
  await page.getByLabel("E-posta", { exact: true }).fill("ayse@example.com");
  const button = page.getByRole("button", { name: "Bağlantı gönder", exact: true });
  await button.click(); await expect(page.getByRole("alert")).toContainText("Sunucuya bağlanılamadı");
  await button.click(); await expect(page.getByRole("alert")).toContainText("Bir dakika");
  await button.click(); await expect(page.getByRole("status")).toHaveText(message); await expect(button).toBeEnabled();
});
