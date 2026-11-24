import { test, expect } from "@playwright/test";
import { cities } from "../helpers/cities.mjs";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat, installSeatSelectionMock } from "./helpers/seat-selection.mjs";

const id = "12345678-1234-1234-1234-123456789012";
const base = { id: 12, companyId: 1, companyName: "Kesim Firması", departureCity: "Ankara", arrivalCity: "İstanbul", mainDepartureCity: "Diyarbakır", mainArrivalCity: "İstanbul", departureTime: "2099-12-17T06:00:00Z", arrivalTime: "2099-12-17T12:00:00Z", departureTerminalName: "Ankara AŞTİ", arrivalTerminalName: "İstanbul Esenler", price: 200, availableSeats: 6, durationMinutes: 360, boardingStopOrder: 3, alightingStopOrder: 5 };
const alternative = { ...base, boardingStopOrder: 4, departureTerminalName: "Ankara ikinci terminal", departureTime: "2099-12-17T07:00:00Z", price: 150, durationMinutes: 300 };
async function setup(page, signedIn = true) {
  await page.route("**/api/routes/city-options", r => r.fulfill({ json: cities }));
  if (signedIn) await page.addInitScript(() => { sessionStorage.setItem("travela_token","segment-ui"); sessionStorage.setItem("travela_user",JSON.stringify({ role:"Passenger", fullName:"Kesim Yolcusu" })); });
  await page.route("**/api/profile", r => r.fulfill({ json: { fullName:"Kesim Yolcusu", identityNumber:"10000000146", email:"shared@example.test", phoneNumber:"+905321234567" } }));
  await page.route("**/api/routes/public", r => r.fulfill({ json:[{ departureCity:"Ankara", arrivalCity:"İstanbul", departureCityId:7, arrivalCityId:42 }] }));
  await page.route("**/api/trips/search?*", r => r.fulfill({ json: { items:[{ ...base, journeyOptions:[alternative] }], companies:[{ id:1,name:base.companyName }], totalCount:1,totalPages:1 } }));
  await page.route("**/api/trips/public/12?*", r => {
    const from = Number(new URL(r.request().url()).searchParams.get("boardingStopOrder"));
    return r.fulfill({ json:{ ...(from === 4 ? alternative : base), seatCapacity:6, sellableSeatCount:6, seats:buildSeats("2+1",6), seatLayout:"2+1", occupiedSeats:[],seatOccupancies:[], canReserve:true,seatPlanAvailable:true,status:"Scheduled" } });
  });
  await page.route("**/api/purchases/options", r => r.fulfill({ json:{ provider:"LocalTest",requiresBuyer:false } }));
}
for (const width of [390,1440]) test(`Terminal seçimi aramadan koltuk ve ödemeye korunur: ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width,height:1000 }); await setup(page); let sent; let quoted;
  await page.route("**/api/campaigns/quote", r => { quoted=r.request().postDataJSON(); return r.fulfill({ json:{ amount:150,originalAmount:150,discountAmount:0 } }); });
  const group = { id, amount:150, originalAmount:150,discountAmount:0, provider:"LocalTest",isTest:true,legalAccepted:true,paymentStatus:"Pending",checkoutState:"Ready",checkoutUrl:`/purchase/${id}`,holdExpiresAt:"2099-12-17T06:59:00Z",refundRequested:0,refunded:0,tickets:[] };
  await page.route("**/api/purchases",r => { sent=r.request().postDataJSON(); return r.fulfill({ json:group }); });
  await page.route(`**/api/purchases/${id}`,r => r.fulfill({ json:group }));
  await installSeatSelectionMock(page); await page.goto("/search?from=Ankara&to=İstanbul&fromId=7&toId=42&date=2099-12-17&passengers=1");
  await expect(page.locator(".result-card")).toHaveCount(1);
  await page.getByLabel("Biniş ve iniş noktaları · Sefer 12").selectOption("?boardingStopOrder=4&alightingStopOrder=5");
  await expect(page.locator(".result-card-price")).toContainText("150");
  await expect(page.getByRole("link",{ name:"Sefer detayı",exact:true })).toHaveAttribute("href","/trips/12?boardingStopOrder=4&alightingStopOrder=5");
  await page.getByRole("button",{ name:"Seferi incele / koltuk seç" }).click();
  await expect(page).toHaveURL("/trips/12?boardingStopOrder=4&alightingStopOrder=5");
  await expect(page.getByText("Ankara ikinci terminal",{ exact:true })).toBeVisible();
  await chooseSeat(page,3);
  await page.getByRole("button",{ name:"Hesap bilgilerimi kullan · Koltuk 3" }).click();
  await page.getByRole("checkbox",{ name:/Yolcu bilgilerini kontrol ettim/ }).check();
  await page.getByRole("checkbox",{ name:/Mesafeli satış sözleşmesini ve ön bilgilendirme/ }).check();
  await page.screenshot({ path:`../tmp/segment-sales/checkout-${width}.png`,fullPage:true });
  const size = await page.evaluate(() => ({ width:document.documentElement.scrollWidth,viewport:innerWidth })); expect(size.width).toBeLessThanOrEqual(size.viewport+1);
  await page.getByRole("button",{ name:"Ödemeye devam et",exact:true }).click();
  await expect(page).toHaveURL(`/purchase/${id}`);
  expect(quoted.boardingStopOrder).toBe(4); expect(quoted.alightingStopOrder).toBe(5);
  expect(sent.boardingStopOrder).toBe(4); expect(sent.alightingStopOrder).toBe(5); expect(sent.expectedAmount).toBe(150);
});

test("İsteğe bağlı giriş kapatılınca gerçek durak ve koltuk seçimi saklanır", async ({ page }) => {
  await setup(page,false);
  await installSeatSelectionMock(page); await page.goto("/trips/12?boardingStopOrder=4&alightingStopOrder=5");
  await chooseSeat(page,3);
  await page.getByRole("button",{ name:"Hesabınızla giriş yapın (isteğe bağlı)",exact:true }).click();
  await expect(page.getByRole("dialog",{ name:"İsteğe bağlı hesap girişi" })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page).toHaveURL(/\/trips\/12\?boardingStopOrder=4&alightingStopOrder=5/);
  await expect(page.getByRole("button",{ name:/^Koltuk 3,/ })).toHaveAttribute("aria-pressed","true");
});

test("Firma durak ve kesim fiyatlarını mobil sefer yönetiminden kaydeder", async ({ page }) => {
  await page.route("**/api/trips/*/crew", r => r.fulfill({ json: { crew: [], changeVersion: 0 } }));
  await page.setViewportSize({ width:390,height:1000 });
  await page.addInitScript(() => { sessionStorage.setItem("travela_token","manager-segment"); sessionStorage.setItem("travela_user",JSON.stringify({ role:"Company",fullName:"Firma Test" })); });
  const trip = { id:21,companyId:1,companyName:"Test Firma",busId:2,busPlateNumber:"34 TEST",busCapacity:6,routeId:1,departureCity:"İstanbul",arrivalCity:"Ankara",departureTime:"2099-12-17T06:00:00Z",arrivalTime:"2099-12-17T18:00:00Z",price:500,availableSeats:6,status:"Scheduled",companyIsActive:true,busIsActive:true,createdAt:"2026-01-01T00:00:00Z",changeVersion:0 };
  const bus = { id:2,companyId:1,plateNumber:"34 TEST",seatCapacity:6,isActive:true };
  await page.route("**/api/company/profile",r => r.fulfill({ json:{ name:"Test Firma" } }));
  await page.route("**/api/trips/summary",r => r.fulfill({ json:{ totalTrips:1,scheduledTrips:1,inProgressTrips:0,completedTrips:0,cancelledTrips:0 } }));
  await page.route("**/api/trips?*",r => r.fulfill({ json:{ items:[trip],totalCount:1,totalPages:1 } }));
  await page.route("**/api/trips/21",r => r.fulfill({ json:trip }));
  await page.route("**/api/buses?*",r => r.fulfill({ json:[bus] }));
  await page.route("**/api/routes",r => r.fulfill({ json:[{ id:1,departureCity:"İstanbul",arrivalCity:"Ankara",isActive:true }] }));
  await page.route("**/api/routes/city-options",r => r.fulfill({ json:[{ id:1,name:"İstanbul" },{ id:2,name:"Bolu" },{ id:3,name:"Ankara" }] }));
  await page.route("**/api/terminals",r => r.fulfill({ json:[{ id:1,name:"Esenler",city:"İstanbul",isActive:true },{ id:2,name:"Bolu terminali",city:"Bolu",isActive:true },{ id:3,name:"AŞTİ",city:"Ankara",isActive:true }] }));
  await page.route("**/api/trips/21/seat-blocks",r => r.fulfill({ json:{ blockedSeats:[] } }));
  await page.route("**/api/trips/21/seat-plan",r => r.fulfill({ json:{ layout:"2+1",seats:buildSeats("2+1",6) } }));
  const data = { stops:[{ cityId:1,terminalId:1,arrivalTime:trip.departureTime,departureTime:trip.departureTime,canBoard:true,canAlight:false },{ cityId:3,terminalId:3,arrivalTime:trip.arrivalTime,departureTime:trip.arrivalTime,canBoard:false,canAlight:true }],fares:[{ fromStopOrder:1,toStopOrder:2,amount:500,isApproved:true }],changeVersion:0,topologyLocked:false };
  let sent;
  await page.route("**/api/trips/21/stops",r => { if (r.request().method() === "PUT") { sent=r.request().postDataJSON(); data.stops=sent.stops; data.fares=sent.fares; data.changeVersion++; return r.fulfill({ json:{ message:"Duraklar kaydedildi." } }); } return r.fulfill({ json:data }); });
  await installSeatSelectionMock(page); await page.goto("/company/trips?detail=21");
  const panel = page.locator(".trip-stops-panel").filter({ hasText:"Duraklar ve kesim fiyatları" });
  await panel.locator("summary").click();
  await panel.getByRole("button",{ name:"+ Ara durak ekle",exact:true }).click();
  await panel.getByLabel("Şehir · Durak 2").selectOption("2");
  await panel.getByLabel("Terminal · Durak 2").selectOption("2");
  await panel.getByLabel("Varış · Durak 2").fill("2099-12-17T15:00");
  await panel.getByLabel("Kalkış · Durak 2").fill("2099-12-17T15:15");
  await panel.getByLabel("Fiyat · 1 → 2",{ exact:true }).fill("200");
  await panel.getByLabel("Fiyat onayı · 1 → 2",{ exact:true }).check();
  await panel.getByLabel("Fiyat · 2 → 3",{ exact:true }).fill("300");
  await panel.getByLabel("Fiyat onayı · 2 → 3",{ exact:true }).check();
  const bounds = await panel.boundingBox(); expect(bounds.width).toBeLessThanOrEqual(390);
  await page.screenshot({ path:"../tmp/segment-sales/manager-mobile.png",fullPage:true });
  await panel.getByRole("button",{ name:"Durak ve fiyatları kaydet",exact:true }).click();
  await expect.poll(() => sent?.stops.length).toBe(3);
  expect(sent.stops[1].departureTime).toBe("2099-12-17T12:15:00.000Z");
  expect(sent.fares).toContainEqual({ fromStopOrder:1,toStopOrder:2,amount:200,isApproved:true });
  expect(sent.expectedVersion).toBe(0);
});
