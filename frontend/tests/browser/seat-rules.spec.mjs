import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";

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
  await lastAvailable.click(); await expect(lastAvailable).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByLabel(`Ad soyad · Koltuk ${capacity - 1}`)).toBeVisible();
  await lastAvailable.click(); await expect(lastAvailable).toHaveAttribute("aria-pressed", "false");
  await expect(page.getByLabel(`Ad soyad · Koltuk ${capacity - 1}`)).toHaveCount(0);
});
test("Cinsiyet değişince uygunluk anında değişir ve uyumsuz koltuk açıklanır", async ({ page }) => {
  await setup(page);
  await page.getByLabel("Yeni yolcunun cinsiyeti").selectOption("Male");
  await expect(page.getByRole("button", { name: /^Koltuk 2,/ })).toHaveClass(/incompatible/);
  await page.getByRole("button", { name: /^Koltuk 2,/ }).click();
  await expect(page.getByRole("alert")).toContainText("yalnızca kadın");
  await expect(page.getByRole("button", { name: /^Koltuk 2,/ })).toHaveAttribute("aria-pressed", "false");
  await page.getByLabel("Yeni yolcunun cinsiyeti").selectOption("Female");
  await page.getByRole("button", { name: /^Koltuk 2,/ }).click();
  await expect(page.getByRole("group", { name: "Cinsiyet seçiniz · Koltuk 2" }).getByLabel("Kadın", { exact: true })).toBeChecked();
  await page.getByRole("group", { name: "Cinsiyet seçiniz · Koltuk 2" }).getByLabel("Erkek", { exact: true }).check();
  await expect(page.locator(".seat-compatibility-error")).toContainText("yalnızca kadın");
  await expect(page.getByRole("button", { name: "Ödemeye geç", exact: true })).toBeDisabled();
});
test("Hesap doldurma cinsiyet türetmez; her koltuğun seçimi ayrı ve kaldırılması kalıcıdır", async ({ page }) => {
  await setup(page); await page.getByRole("button", { name: /^Koltuk 3,/ }).click();
  await page.getByRole("button", { name: "Hesap bilgilerimi kullan · Koltuk 3" }).click();
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveValue("Hesap Yolcusu");
  const first = page.getByRole("group", { name: "Cinsiyet seçiniz · Koltuk 3" });
  await expect(first.getByLabel("Kadın", { exact: true })).not.toBeChecked();
  await first.getByLabel("Kadın", { exact: true }).check();
  await page.getByRole("button", { name: /^Koltuk 4,/ }).click();
  await page.getByRole("group", { name: "Cinsiyet seçiniz · Koltuk 4" }).getByLabel("Erkek", { exact: true }).check();
  await expect(page.locator(".seat-compatibility-error")).toHaveCount(2);
  await page.getByRole("button", { name: "Koltuğu çıkar · 4", exact: true }).click();
  await expect(page.locator(".seat-compatibility-error")).toHaveCount(0);
  await page.getByRole("button", { name: "Koltuğu çıkar · 3", exact: true }).click();
  await page.getByRole("button", { name: /^Koltuk 3,/ }).click();
  await expect(page.getByLabel("Ad soyad · Koltuk 3")).toHaveValue("");
  await expect(page.getByRole("group", { name: "Cinsiyet seçiniz · Koltuk 3" }).getByLabel("Kadın", { exact: true })).not.toBeChecked();
});
