import { test, expect } from "@playwright/test";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat } from "./helpers/seat-selection.mjs";

const orderId = "12345678-1234-1234-1234-123456789012";
async function setup(context, member = false) {
  const server = { now: Date.now(), sessions: new Map(), writes: 0, orders: [], fail: false, gate: null, delayedRead: null, dropWrite: false };
  if (member) await context.addInitScript(() => { sessionStorage.setItem("travela_token", "hold-member"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Passenger", fullName: "Hesap Sahibi" })); });
  const live = () => [...server.sessions.values()].filter(s => s.expiresAt > server.now).flatMap(s => s.seats.map(seat => ({ ...seat, kind: "Hold", expiresAt: new Date(s.expiresAt).toISOString(), owner: s.id })));
  const view = s => ({ id: s.id, phase: s.phase === "Payment" ? "Payment" : s.expiresAt && s.expiresAt <= server.now ? "Expired" : "Selection",
    paymentStatus: s.paymentStatus ?? null, canStartNewSelection: ["Paid","Failed"].includes(s.paymentStatus),
    expiresAt: s.expiresAt ? new Date(s.expiresAt).toISOString() : null, serverTime: new Date(server.now).toISOString(), purchaseGroupId: s.orderId ?? null,
    seats: s.expiresAt && s.expiresAt <= server.now ? [] : s.seats });
  const group = s => ({ id: orderId, guest: !member, amount: s.seats.length * 300, originalAmount: s.seats.length * 300, discountAmount: 0,
    provider: "LocalTest", isTest: true, legalAccepted: true, paymentStatus: "Pending", checkoutState: "Ready", checkoutUrl: `/purchase/${orderId}`,
    serverTime: new Date(server.now).toISOString(), holdExpiresAt: new Date(s.expiresAt).toISOString(), refundRequested: 0, refunded: 0, tickets: [] });
  await context.route("**/api/**", async route => {
    const req = route.request(); const path = new URL(req.url()).pathname;
    if (server.fail && (path.startsWith("/api/seat-selections") || path.startsWith("/api/trips/public"))) return route.abort("internetdisconnected");
    if (path === "/api/trips/public/12") {
      const occupied = live(); return route.fulfill({ json: { id: 12, companyName: "Süre Test Firma", departureCity: "İzmir", arrivalCity: "Ankara",
        departureTime: "2099-12-17T07:00:00Z", arrivalTime: "2099-12-17T13:00:00Z", durationMinutes: 360, price: 300, availableSeats: 12 - occupied.length,
        seatCapacity: 12, seatLayout: "2+2", status: "Scheduled", canReserve: true, seatPlanAvailable: true, seats: buildSeats("2+2",12),
        occupiedSeats: occupied.map(s => s.seatNumber), seatOccupancies: occupied.map(({owner: _owner,...seat}) => seat) } });
    }
    if (path === "/api/seat-selections") {
      const body = req.postDataJSON(); if (!server.sessions.has(body.id)) server.sessions.set(body.id, { ...body, seats: [], expiresAt: null, phase: "Selection" });
      return route.fulfill({ json: view(server.sessions.get(body.id)) });
    }
    if (path.startsWith("/api/seat-selections/")) {
      const s = server.sessions.get(path.split("/")[3]);
      if (!s || s.token !== req.headers()["x-seat-token"]) return route.fulfill({ status: 404, json: { message: "Oturum bulunamadı." } });
      if (req.method() === "GET" && server.delayedRead) {
        const gate = server.delayedRead; server.delayedRead = null; const snapshot = structuredClone(view(s)); gate.started(); await gate.promise;
        return route.fulfill({ json: snapshot });
      }
      if (req.method() === "PUT") {
        server.writes++; const body = req.postDataJSON();
        if (live().some(seat => seat.owner !== s.id && seat.seatNumber === body.seatNumber)) return route.fulfill({ status: 409, json: { code: "seat_unavailable", message: "Bu koltuk başka bir kullanıcı tarafından ayrıldı. Lütfen farklı bir koltuk seçin." } });
        s.expiresAt ??= server.now + 300000; s.seats = [...s.seats.filter(seat => seat.seatNumber !== body.seatNumber), body];
        if (server.dropWrite) { server.dropWrite = false; return route.abort("connectionreset"); }
        if (server.gate) await server.gate;
      } else if (req.method() === "DELETE") s.seats = s.seats.filter(seat => seat.seatNumber !== Number(path.split("/").at(-1)));
      return route.fulfill({ json: view(s) });
    }
    if (path === "/api/purchases/options") return route.fulfill({ json: { provider: "LocalTest", requiresBuyer: false } });
    if (path.endsWith("/quote")) { const count = req.postDataJSON().passengerCount; return route.fulfill({ json: { amount: count * 300, originalAmount: count * 300, discountAmount: 0 } }); }
    if (path === "/api/profile") return route.fulfill({ json: { fullName: "Hesap Sahibi", identityNumber: "10000000146", email: "profile@example.test", phoneNumber: "+905321234567" } });
    if (path === "/api/guest/purchases/session") return route.fulfill({ json: { ready: true } });
    if (path.endsWith("/purchases/resume")) return route.fulfill({ json: { id: null } });
    if (path === "/api/purchases" || path === "/api/guest/purchases") {
      const body = req.postDataJSON(); const s = server.sessions.get(body.selectionSessionId); expect(req.headers()["x-seat-token"]).toBe(s.token);
      server.orders.push(body); s.phase = "Payment"; s.expiresAt = server.now + 600000; s.orderId = orderId; return route.fulfill({ json: group(s) });
    }
    if (path.endsWith(`/purchases/${orderId}`) || path.endsWith(`/purchases/${orderId}/refresh`)) return route.fulfill({ json: group([...server.sessions.values()].find(s => s.orderId)) });
    return route.fulfill({ json: [] });
  });
  return server;
}
test("Sunucu onayından önce seçili olmaz; cinsiyet/koltuk isteği sürerken ikinci istek gönderilmez", async ({ page, context }) => {
  const server = await setup(context); let release; server.gate = new Promise(resolve => { release = resolve; });
  await page.goto("/trips/12"); expect(server.sessions.size).toBe(0);
  const seat = page.getByRole("button",{ name:/^Koltuk 1,/ }); await seat.click();
  await page.getByRole("dialog").getByRole("button",{ name:"Kadın",exact:true }).click();
  await expect(page.getByText("Koltuk seçiminiz sunucuda doğrulanıyor...",{exact:true})).toBeVisible();
  await expect(seat).toHaveAttribute("aria-pressed","false"); await expect(page.getByRole("dialog").getByRole("button",{name:"Kadın",exact:true})).toBeDisabled();
  expect(server.writes).toBe(1); release(); await expect(seat).toHaveAttribute("aria-pressed","true");
  await expect(page.getByRole("timer")).toContainText("Koltuklarınız için kalan süre: 05:");
});
for (const member of [false,true]) test(`${member ? "Üye" : "Misafir"}: bağımsız sekmeler tutmayı paylaşmaz, yenileme ve ek koltuk süreyi uzatmaz`, async ({ page, context }) => {
  const server = await setup(context,member); await page.goto("/trips/12"); await chooseSeat(page,1); const first = [...server.sessions.values()][0]; const expiry = first.expiresAt;
  server.now += 60000; await chooseSeat(page,2); expect(first.expiresAt).toBe(expiry);
  await page.reload(); await expect(page.getByRole("button",{name:/^Koltuk 1,/})).toHaveAttribute("aria-pressed","true"); expect(first.expiresAt).toBe(expiry);
  const copy = await page.evaluate(() => ({...sessionStorage})); const second = await context.newPage();
  await second.addInitScript(data => { for (const [key,value] of Object.entries(data)) sessionStorage.setItem(key,value); },copy);
  await second.goto("/trips/12");
  await expect(second.getByRole("button",{name:/^Koltuk 1, Geçici olarak ayrıldı/})).toBeDisabled();
  await chooseSeat(second,5); expect(server.sessions.size).toBe(2); expect(first.expiresAt).toBe(expiry);
  await expect(page.getByRole("button",{name:/^Koltuk 5, Geçici olarak ayrıldı/})).toBeDisabled({timeout:8000});
  expect(await second.getByRole("button",{name:/^Koltuk 2,/}).getAttribute("aria-pressed")).toBe("false");
  // A clock change on the device does not alter the monotonic countdown or server deadline.
  await page.evaluate(() => { Date.now = () => 0; }); expect(first.expiresAt).toBe(expiry);
  await second.close();
});
test("Ağ kesintisinde süre değişmez; bağlantı geri gelince sunucudan doğrulanır ve eski yanıt seçimi geri alamaz", async ({ page, context }) => {
  const server = await setup(context); await page.goto("/trips/12"); await chooseSeat(page,1); const first = [...server.sessions.values()][0]; const expiry = first.expiresAt;
  server.fail = true; await page.evaluate(() => window.dispatchEvent(new Event("online")));
  await expect(page.getByRole("alert").filter({hasText:"Koltuklarınız sunucudan doğrulanamadı."})).toBeVisible();
  server.now += 60000; server.fail = false; await page.evaluate(() => window.dispatchEvent(new Event("online")));
  await expect(page.getByRole("button",{name:/^Koltuk 1,/})).toHaveAttribute("aria-pressed","true");
  let release; let started; const inFlight = new Promise(resolve => { started = resolve; });
  server.delayedRead = { started, promise: new Promise(resolve => { release = resolve; }) };
  await page.evaluate(() => window.dispatchEvent(new Event("focus"))); await inFlight;
  await chooseSeat(page,2); release();
  await expect(page.getByRole("button",{name:/^Koltuk 2,/})).toHaveAttribute("aria-pressed","true");
  expect(first.expiresAt).toBe(expiry); await expect(page.getByRole("timer")).toContainText("04:");
});
test("Ayırma kaydedildikten sonra yanıt kaybolursa sunucu sorgusuyla aynı tutma geri gelir", async ({ page, context }) => {
  const server = await setup(context); server.dropWrite = true; await page.goto("/trips/12");
  await chooseSeat(page,1); expect(server.writes).toBe(1); expect(server.sessions.size).toBe(1);
  const state = [...server.sessions.values()][0]; const expiry = state.expiresAt;
  await chooseSeat(page,1); expect(state.expiresAt).toBe(expiry); expect(state.seats).toHaveLength(1);
});
test("Mobil süre dolması seçimleri kaldırır; bilgiler aynı koltukta kalır ve başka koltuğa taşınmaz", async ({ page, context }) => {
  await page.setViewportSize({ width:390,height:900 }); const server = await setup(context); await page.clock.install();
  await page.goto("/trips/12"); await chooseSeat(page,1);
  await page.getByLabel("Ad soyad · Koltuk 1",{exact:true}).fill("Korunan Yolcu"); await page.getByLabel("T.C. Kimlik No · Koltuk 1",{exact:true}).fill("10000000146");
  await page.getByLabel("İletişim e-posta").fill("kept@example.test"); await page.getByLabel("İletişim telefon").fill("05321234567");
  server.now += 301000; await page.clock.fastForward(301000); await expect(page.getByRole("alert")).toContainText("süreniz doldu");
  await expect(page.getByRole("button",{name:/^Koltuk 1,/})).toHaveAttribute("aria-pressed","false");
  await chooseSeat(page,2); await expect(page.getByLabel("Ad soyad · Koltuk 2",{exact:true})).toHaveValue(""); await expect(page.getByLabel("İletişim e-posta")).toHaveValue("kept@example.test");
  await chooseSeat(page,1); await expect(page.getByLabel("Ad soyad · Koltuk 1",{exact:true})).toHaveValue("Korunan Yolcu");
  await expect(page.locator("body")).toHaveJSProperty("scrollWidth",390); await page.screenshot({path:"../tmp/seat-hold-qa/mobile-selection.png",fullPage:true});
});
for (const member of [false,true]) test(`${member ? "Üye" : "Misafir"}: sunucu tutması ödemede 10 dakikaya geçer ve geri dönüşte aynı sipariş sunulur`, async ({ page,context }) => {
  const server = await setup(context,member); await page.goto("/trips/12"); await chooseSeat(page,1);
  await page.getByLabel("Ad soyad · Koltuk 1",{exact:true}).fill("Ödeme Yolcusu"); await page.getByLabel("T.C. Kimlik No · Koltuk 1",{exact:true}).fill("10000000146");
  await page.getByLabel("İletişim e-posta").fill("buyer@example.test"); await page.getByLabel("İletişim telefon").fill("05321234567");
  await page.getByRole("checkbox",{name:/Mesafeli satış sözleşmesini ve ön bilgilendirme/}).check(); await page.getByRole("checkbox",{name:/Yolcu bilgilerini kontrol ettim/}).check();
  await page.getByRole("button",{name:"Ödemeye devam et",exact:true}).click(); await expect(page).toHaveURL(`/purchase/${orderId}`);
  await expect(page.getByRole("timer")).toContainText("Ödeme için kalan süre: 10:"); const expiry = [...server.sessions.values()][0].expiresAt;
  await page.reload(); await expect(page.getByRole("timer")).toContainText("Ödeme için kalan süre"); expect([...server.sessions.values()][0].expiresAt).toBe(expiry); expect(server.orders).toHaveLength(1);
  await page.goBack(); await expect(page.getByRole("link",{name:"Mevcut ödemeye devam et",exact:true})).toBeVisible();
  await expect(page.getByRole("button",{name:"Ödemeye devam et",exact:true})).toBeDisabled(); expect(server.orders).toHaveLength(1);
  await expect(page.getByRole("button",{name:"Yeni bilet için koltuk seç",exact:true})).toHaveCount(0);
  const previous = [...server.sessions.values()][0]; previous.paymentStatus = "Failed"; previous.seats = [];
  await page.evaluate(() => window.dispatchEvent(new Event("online")));
  await page.getByRole("button",{name:"Yeni bilet için koltuk seç",exact:true}).click(); await chooseSeat(page,2);
  expect(server.sessions.size).toBe(2); expect(previous.expiresAt).toBe(expiry); expect(server.orders).toHaveLength(1);
});
