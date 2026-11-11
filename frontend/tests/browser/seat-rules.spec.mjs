import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat, releaseSeat } from "./helpers/seat-selection.mjs";

async function setup(page, layout = "2+2", capacity = 40) {
  await page.addInitScript(() => { sessionStorage.setItem("travela_token", "seat-test"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger", fullName: "Hesap Yolcusu" })); });
  await page.route("**/api/profile", r => r.fulfill({ json: { fullName: "Hesap Yolcusu", email: "account@example.test", phoneNumber: "+905321234567" } }));
  await page.route("**/api/purchases/options", r => r.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } }));
  await page.route("**/api/campaigns/quote", r => { const count = r.request().postDataJSON().passengerCount; return r.fulfill({ json: { amount: 400 * count, originalAmount: 400 * count, discountAmount: 0 } }); });
  const seats = buildSeats(layout, capacity, [capacity]);
  // A verified custom plan can number its physical positions in a different order.
  if (layout === "Custom") [seats[1].number, seats[capacity - 2].number] = [seats[capacity - 2].number, seats[1].number];
  const data = { id: 21, companyName: "Travela Test", departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-06-21T06:00:00Z", arrivalTime: "2099-06-21T12:00:00Z", durationMinutes: 360, price: 400, availableSeats: capacity - 4, seatCapacity: capacity, seatLayout: layout, seats, occupiedSeats: [1, 5, 9], seatOccupancies: [{ seatNumber: 1, gender: "Female", kind: "Ticket", expiresAt: null }, { seatNumber: 5, gender: "Male", kind: "Ticket", expiresAt: null }, { seatNumber: 9, gender: "Female", kind: "Hold", expiresAt: "2099-06-21T05:00:00Z" }], canReserve: true, seatPlanAvailable: true, status: "Scheduled", sunExposure: { side: "Left", message: "Bu seferde güneşin çoğunlukla otobüsün sol tarafından gelmesi bekleniyor.", leftMinutes: 300, rightMinutes: 20, nightMinutes: 40 } };
  await page.route("**/api/trips/public/21", r => r.fulfill({ json: data }));
  await page.goto("/trips/21");
  return data;
}
for (const width of [390, 768, 1024, 1366, 1440, 1920]) for (const [layout, capacity] of [["2+1", 46], ["2+2", 54], ["Custom", 46]]) test(`${layout} ${capacity} koltuk duyarlı plan ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 }); const data = await setup(page, layout, capacity);
  await expect(page.getByRole("button", { name: /^Koltuk 1,/ })).toBeDisabled();
  await expect(page.getByRole("button", { name: new RegExp(`^Koltuk ${capacity},`) })).toBeDisabled();
  await expect(page.locator(".physical-seat")).toHaveCount(capacity);
  await expect(page.locator(".physical-seat.occupied.female")).toHaveCount(1);
  await expect(page.locator(".physical-seat.occupied.male")).toHaveCount(1);
  await expect(page.locator(".physical-seat.held")).toHaveCount(1);
  const size = await page.evaluate(() => {
    const buttons = [...document.querySelectorAll('.physical-seat')];
    const bounds = buttons.map(button => button.getBoundingClientRect());
    const s = bounds[0]; const aisle = document.querySelector('.seat-corridor').getBoundingClientRect();
    const scroll = document.querySelector('.bus-scroll'); const area = scroll.getBoundingClientRect();
    const wheel = document.querySelector('.driver-wheel').getBoundingClientRect();
    const summary = document.querySelector('.seat-booking > aside').getBoundingClientRect();
    const overlaps = bounds.some((a, i) => bounds.some((b, j) => i < j && a.left < b.right - 1 && a.right > b.left + 1 && a.top < b.bottom - 1 && a.bottom > b.top + 1));
    return { width: document.documentElement.scrollWidth, viewport: innerWidth, ratio: aisle.height / s.height,
      canScroll: scroll.scrollWidth > scroll.clientWidth, seatWidth: s.width, seatHeight: s.height, overlaps,
      allInside: [...bounds, wheel].every(b => b.left >= area.left && b.right <= area.right + 1),
      panelOnRight: summary.left >= area.right,
      positions: buttons.map(button => ({ number: Number(button.querySelector('.seat-number').textContent), row: Number(button.style.gridColumn) - 1, column: Number(button.style.gridRow) })),
    };
  });
  expect(size.width).toBeLessThanOrEqual(size.viewport + 1); expect(size.ratio).toBeGreaterThanOrEqual(1.5); expect(size.ratio).toBeLessThanOrEqual(2);
  expect(size.canScroll).toBe(width < 768);
  expect(size.overlaps).toBe(false);
  expect(size.seatWidth).toBeGreaterThanOrEqual(width < 768 ? 44 : 28);
  expect(size.seatHeight).toBeCloseTo(size.seatWidth, 0);
  expect(size.positions).toEqual(data.seats.map(({ number, row, column }) => ({ number, row, column })));
  if (width >= 768) expect(size.allInside).toBe(true);
  if (width >= 1366) expect(size.panelOnRight).toBe(true);
  await expect(page.locator('.bus-sun.sun-left')).toBeVisible();
  await page.screenshot({ path: `../tmp/ticket-qa/seat-rules-${layout}-${width}.png`, fullPage: true });
  const lastAvailable = page.getByRole("button", { name: new RegExp(`^Koltuk ${capacity - 1},`) });
  await chooseSeat(page, capacity - 1);
  await expect(lastAvailable).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByLabel(`Ad soyad · Koltuk ${capacity - 1}`)).toBeVisible();
  await releaseSeat(page, capacity - 1);
  await expect(lastAvailable).toHaveAttribute("aria-pressed", "false");
  await expect(page.getByLabel(`Ad soyad · Koltuk ${capacity - 1}`)).toHaveCount(0);
});
test("Komşuluk kuralı modalda açıklanır; uyumsuz cinsiyet seçilemez", async ({ page }) => {
  await setup(page);
  const seat = page.getByRole("button", { name: /^Koltuk 2,/ });
  await seat.click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("yalnızca kadın");
  await expect(dialog.getByRole("button", { name: "Erkek", exact: true })).toBeDisabled();
  await expect(seat).toHaveAttribute("aria-pressed", "false");
  await dialog.getByRole("button", { name: "Kadın", exact: true }).click();
  await expect(seat).toHaveClass(/selected female/);
  await seat.click();
  await expect(dialog.getByRole("button", { name: "Erkek", exact: true })).toBeDisabled();
  await dialog.getByRole("button", { name: "Kadın", exact: true }).click();
  await chooseSeat(page, 3, "Kadın");
  await page.getByRole("button", { name: /^Koltuk 4,/ }).click();
  await expect(dialog.getByRole("button", { name: "Erkek", exact: true })).toBeDisabled();
  await dialog.getByRole("button", { name: "Vazgeç", exact: true }).click();
  await releaseSeat(page, 3);
  await chooseSeat(page, 4, "Erkek");
});

test("Bağımsız cinsiyetler, tekrar tıklayarak düzenleme ve geçici bilgilerin temizlenmesi", async ({ page }) => {
  await setup(page);
  const first = await chooseSeat(page, 3, "Kadın");
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 3" }).click();
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveValue("Hesap Yolcusu");
  const second = await chooseSeat(page, 7, "Erkek");
  await expect(first).toHaveClass(/selected female/);
  await expect(second).toHaveClass(/selected male/);
  await expect(page.locator('.booking-summary input[type="radio"]')).toHaveCount(0);
  await expect(page.getByRole("combobox", { name: "Yeni yolcunun cinsiyeti" })).toHaveCount(0);
  await chooseSeat(page, 3, "Erkek");
  await expect(first).toHaveClass(/selected male/);
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveValue("Hesap Yolcusu");
  await expect(page.locator(".physical-seat.selected")).toHaveCount(2);
  await page.getByRole("button", { name: "Koltuğu çıkar · 3", exact: true }).click();
  await first.click();
  await expect(page.getByRole("dialog").getByRole("button", { name: "Erkek", exact: true }))
    .toHaveAttribute("aria-pressed", "false");
  await page.getByRole("dialog").getByRole("button", { name: "Kadın", exact: true }).click();
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveValue("");
  await expect(first).toHaveClass(/selected female/);
  await first.click();
  await page.keyboard.press("Escape");
  await expect(first).toHaveAttribute("aria-pressed", "false");
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveCount(0);
  await expect(second).toHaveClass(/selected male/);
});

for (const width of [390, 1440]) for (const gender of ["Kadın", "Erkek"]) {
  test(`Cinsiyet tamamlanmadan seçim yok; ${gender} rengi ve yeşil çerçeve ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await setup(page);
    const seat = page.getByRole("button", { name: /^Koltuk 3,/ });
    await expect(seat).toHaveCSS("background-color", "rgb(255, 255, 255)");
    await seat.click();
    const dialog = page.getByRole("dialog", { name: "Koltuk 3 – Yolcu Cinsiyeti" });
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText("Bu koltukta seyahat edecek yolcunun cinsiyetini seçiniz.");
    await expect(seat).toHaveAttribute("aria-pressed", "false");
    await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Ödemeye geç", exact: true })).toBeDisabled();
    const bounds = await dialog.boundingBox();
    expect(bounds.x).toBeGreaterThanOrEqual(0);
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(width);
    expect(Math.abs(bounds.x + bounds.width / 2 - width / 2)).toBeLessThan(2);
    expect(Math.abs(bounds.y + bounds.height / 2 - 450)).toBeLessThan(2);
    if (gender === "Kadın") {
      await page.screenshot({ path: `../tmp/ticket-qa/seat-gender-modal-${width}.png` });
    }
    await dialog.getByRole("button", { name: gender, exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await expect(seat).toHaveAttribute("aria-pressed", "true");
    await expect(seat).toHaveCSS("background-color",
      gender === "Kadın" ? "rgb(252, 226, 236)" : "rgb(220, 238, 255)");
    await expect(seat).toHaveCSS("border-top-color", "rgb(34, 166, 107)");
    await expect(seat.locator(".seat-number")).toHaveText("3");
    await expect(seat.locator(".seat-selected-mark")).toHaveText("✓");
    await expect(seat).toHaveAccessibleName(new RegExp("Seçili, " + gender));
    await expect(page.getByLabel("Yolcu cinsiyeti · Koltuk 3")).toContainText(gender);
    await expect(page.locator('.booking-summary input[type="radio"], .booking-summary select[name*="gender"]'))
      .toHaveCount(0);
    await seat.scrollIntoViewIfNeeded();
    await page.screenshot({ path: `../tmp/ticket-qa/seat-gender-${gender}-${width}.png` });
  });
}

test("Vazgeç, kapatma, Escape ve arka plan tıklaması geçici seçimi iptal eder", async ({ page }) => {
  await setup(page);
  const seat = page.getByRole("button", { name: /^Koltuk 3,/ });
  for (const method of ["button", "close", "escape", "backdrop"]) {
    await seat.click();
    const dialog = page.getByRole("dialog");
    await expect(dialog.getByRole("button", { name: "Kadın", exact: true }))
      .toHaveAttribute("aria-pressed", "false");
    if (method === "button") await dialog.getByRole("button", { name: "Vazgeç", exact: true }).click();
    if (method === "close") await dialog.getByRole("button", { name: "Cinsiyet seçimini iptal et" }).click();
    if (method === "escape") await page.keyboard.press("Escape");
    if (method === "backdrop") await page.mouse.click(5, 5);
    await expect(dialog).not.toBeVisible();
    await expect(seat).toHaveAttribute("aria-pressed", "false");
    await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveCount(0);
  }
});

test("Klavye odağı modal içinde tutulur ve kapanınca koltuğa döner", async ({ page }) => {
  await setup(page);
  const seat = page.getByRole("button", { name: /^Koltuk 3,/ });
  await seat.focus();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("button", { name: "Kadın", exact: true })).toBeFocused();
  for (let index = 0; index < 6; index++) {
    await page.keyboard.press("Tab");
    expect(await page.evaluate(() => document.querySelector("dialog").contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press("Escape");
  await expect(seat).toBeFocused();
  expect(await page.evaluate(() => document.body.style.overflow)).toBe("");
  await page.keyboard.press("Enter");
  await dialog.getByRole("button", { name: "Erkek", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(seat).toHaveClass(/selected male/);
  await expect(seat).toBeFocused();
});

test("Dolu, kapalı ve tutulmuş koltuklar açılmaz; bilinmeyen komşu cinsiyeti atlanamaz", async ({ page }) => {
  const data = await setup(page);
  for (const number of [1, 5, 9, 40]) {
    await expect(page.getByRole("button", { name: new RegExp(`^Koltuk ${number},`) })).toBeDisabled();
  }
  // Occupancy metadata alone must also protect a held seat.
  data.occupiedSeats = [1, 5];
  data.seatOccupancies[0].gender = null;
  await page.getByRole("button", { name: "Koltuk planını yenile" }).click();
  await expect(page.getByRole("button", { name: /^Koltuk 9,/ })).toBeDisabled();
  await page.getByRole("button", { name: /^Koltuk 2,/ }).click();
  await expect(page.getByRole("alert")).toContainText("cinsiyet bilgisi doğrulanamadığı");
  await expect(page.getByRole("dialog")).not.toBeVisible();
});

test("10 koltuk sınırı korunur; sınırdayken mevcut cinsiyet düzenlenebilir", async ({ page }) => {
  await setup(page, "2+1");
  for (const seat of [3, 6, 7, 8, 10, 11, 12, 13, 14, 15]) await chooseSeat(page, seat);
  await expect(page.locator(".physical-seat.selected")).toHaveCount(10);
  await page.getByRole("button", { name: /^Koltuk 16,/ }).click();
  await expect(page.getByRole("alert")).toContainText("En fazla 10 koltuk");
  await expect(page.getByRole("dialog")).not.toBeVisible();
  const edited = await chooseSeat(page, 15, "Erkek");
  await expect(edited).toHaveClass(/selected male/);
  await expect(page.locator(".physical-seat.selected")).toHaveCount(10);
  await page.getByRole("button", { name: "Koltuk planını yenile" }).click();
  await expect(page.locator(".physical-seat.selected")).toHaveCount(0);
  await page.getByRole("button", { name: /^Koltuk 15,/ }).click();
  await expect(page.getByRole("dialog").getByRole("button", { name: "Erkek", exact: true }))
    .toHaveAttribute("aria-pressed", "false");
});
