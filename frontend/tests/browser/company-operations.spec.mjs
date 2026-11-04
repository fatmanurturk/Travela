import { test, expect } from "@playwright/test";
const trip = { id: 12, companyId: 1, companyName: "Travela Turizm", busId: 4, busPlateNumber: "34 ABC 123", busCapacity: 40, routeId: 3, departureTerminalId:1, arrivalTerminalId:2, departureCity: "İstanbul", arrivalCity: "Ankara", departureTime: "2099-01-01T07:00:00Z", arrivalTime: "2099-01-01T13:00:00Z", price: 550, availableSeats: 35, status: "Scheduled" };
async function setup(page, width=1440) {
 await page.setViewportSize({ width, height: 1000 });
 await page.addInitScript(() => { sessionStorage.setItem("travela_token", "operations-token"); sessionStorage.setItem("travela_user", JSON.stringify({ role: "Company", name: "Firma Kullanıcısı", companyName: "Travela Turizm" })); });
 await page.route("**/api/company/profile", r => r.fulfill({json:{name:"Travela Turizm"}}));
 await page.route("**/api/buses?*", r => r.fulfill({json:{items:[{id:4,companyId:1,plateNumber:"34 ABC 123",seatCapacity:40,isActive:true}]}}));
 await page.route("**/api/routes", r=>r.fulfill({json:[{id:3,departureCity:"İstanbul",arrivalCity:"Ankara",estimatedDurationMinutes:360}]}));
 await page.route("**/api/terminals",r=>r.fulfill({json:[{id:1,city:"İstanbul",name:"İstanbul Terminali",address:"İstanbul"},{id:2,city:"Ankara",name:"Ankara Terminali",address:"Ankara"}]}));
 await page.route("**/api/trips?*", r=>r.fulfill({json:{items:[trip],totalCount:1,totalPages:1}}));
 await page.route("**/api/trips/summary",r=>r.fulfill({json:{totalTrips:1,scheduledTrips:1,inProgressTrips:0,completedTrips:0,cancelledTrips:0}}));
}
test("Sefer kopyalama yeni tarihler ister; haftalık tekrar önizlemesi ve tek toplu istek", async ({page}) => {
 await setup(page); let sent;
 await page.route("**/api/trips/batch",async r=>{sent=r.request().postDataJSON();await r.fulfill({status:201,json:{message:"2 sefer oluşturuldu."}});});
 await page.goto("/company/trips");await page.getByRole("button",{name:"Kopyala",exact:true}).click();
 await expect(page.getByText("Sefer #12 bilgileri kopyalandı.",{exact:false})).toBeVisible();
 await expect(page.getByLabel("Bilet fiyatı (TL)",{exact:false})).toHaveValue("550");
 await expect(page.getByLabel("Kalkış tarihi ve saati",{exact:false})).toHaveValue("");
 await page.getByLabel("Kalkış tarihi ve saati",{exact:false}).fill("2099-01-05T23:30");
 await page.getByLabel("Varış tarihi ve saati",{exact:false}).fill("2099-01-06T05:30");
 await page.getByLabel("Oluşturma biçimi").selectOption("weekly");
 await page.getByLabel("Tekrar bitiş tarihi").fill("2099-01-12");
 await page.getByLabel("Pazartesi",{exact:true}).check();
 await expect(page.getByText("2 sefer oluşturulacak.",{exact:false})).toBeVisible();
 await page.screenshot({path:"../tmp/report-qa/screenshots/trip-weekly.png",fullPage:true});
 await page.getByRole("button",{name:"2 Seferi Oluştur",exact:true}).click();
 await expect(page.getByText("2 sefer oluşturuldu.",{exact:true})).toBeVisible();
 expect(sent.trips).toHaveLength(2);expect(sent.trips[0].departureTime).toBe("2099-01-05T20:30:00.000Z");expect(sent.trips[0].arrivalTime).toBe("2099-01-06T02:30:00.000Z");expect(sent.trips[0].companyId).toBeNull();
});
test("Tarih seçerek toplu sefer; çakışma mesajından sonra seçimler korunur",async({page})=>{
 await setup(page,390);await page.route("**/api/trips/batch",r=>r.fulfill({status:409,json:{message:"Otobüsün seferleri çakışıyor. Hiçbir sefer oluşturulmadı."}}));
 await page.goto("/company/trips");await page.getByRole("button",{name:"Kopyala",exact:true}).click();
 await page.getByLabel("Kalkış tarihi ve saati",{exact:false}).fill("2099-01-05T10:00");await page.getByLabel("Varış tarihi ve saati",{exact:false}).fill("2099-01-05T16:00");
 await page.getByLabel("Oluşturma biçimi").selectOption("dates");
 for(const date of ["2099-01-05","2099-01-07"]) {await page.getByLabel("Sefer tarihi",{exact:true}).fill(date);await page.getByRole("button",{name:"Tarihi ekle",exact:true}).click();}
 await page.getByRole("button",{name:"2 Seferi Oluştur",exact:true}).click();
 await expect(page.getByRole("alert")).toContainText("Hiçbir sefer oluşturulmadı");await expect(page.getByRole("button",{name:"2 Seferi Oluştur",exact:true})).toBeEnabled();
 await expect(page.getByRole("table",{name:"Oluşturulacak seferler (Türkiye saati)"}).getByRole("row")).toHaveCount(3);
});
for(const width of [390,1440])test(`Ana sayfada güncel uyarılar ve işlem bağlantıları: ${width}px`,async({page})=>{
 await setup(page,width);
 await page.route("**/api/company/reports/options",r=>r.fulfill({json:[]}));
 await page.route(/\/api\/company\/reports\?/,r=>r.fulfill({status:500,json:{message:"Rapor verileri alınamadı."}}));
 await page.route("**/api/company/attention",r=>r.fulfill({json:{generatedAt:"2099-01-05T07:00:00Z",groups:[
  {id:"departures",title:"Önümüzdeki 24 saatte kalkacak seferler",count:1,items:[{id:"12",title:"#12 · İstanbul → Ankara",description:"Kalkış öncesi sefer bilgilerini kontrol edin.",href:"/company/trips?detail=12",date:"2099-01-05T10:00:00Z"}]},
  {id:"refunds",title:"Tamamlanmamış iadeler",count:1,items:[{id:"pay1",title:"PNR ABC123",description:"İade sonucu bekleniyor · 550 TL",href:"/company/reservations?orderId=pay1&tripId=12",date:null}]}
 ]}}));
 await page.goto("/company?period=custom&from=2000-01-01&to=2000-01-02");
 await expect(page.getByRole("heading",{name:"İşlem gerektiren durumlar",exact:true})).toBeVisible();
 await expect(page.getByRole("link",{name:"#12 · İstanbul → Ankara",exact:true})).toHaveAttribute("href","/company/trips?detail=12");
 await expect(page.getByRole("link",{name:"PNR ABC123",exact:true})).toHaveAttribute("href","/company/reservations?orderId=pay1&tripId=12");
 await expect(page.getByRole("button",{name:/Sefer.*oluştur|Yeni Sefer/i})).toHaveCount(0);
 const size=await page.evaluate(()=>({viewport:document.documentElement.clientWidth,width:document.documentElement.scrollWidth}));expect(size.width).toBeLessThanOrEqual(size.viewport+1);
 await page.screenshot({path:`../tmp/report-qa/screenshots/attention-${width}.png`,fullPage:true});
});

test("Sefer listesi ve özet kendiliğinden yenilenir; manuel durum düğmesi yoktur",async({page})=>{
 await setup(page);let status="Scheduled";
 await page.clock.install({time:new Date("2099-01-01T06:59:50Z")});
 await page.route("**/api/trips?*",r=>r.fulfill({json:{items:[{...trip,status}],totalCount:1,totalPages:1}}));
 await page.route("**/api/trips/summary",r=>r.fulfill({json:{totalTrips:1,scheduledTrips:status==="Scheduled"?1:0,inProgressTrips:status==="InProgress"?1:0,completedTrips:status==="Completed"?1:0,cancelledTrips:0}}));
 await page.goto("/company/trips");await expect(page.locator(".trip-status")).toHaveText("Planlandı");
 status="InProgress";await page.clock.fastForward(15000);await expect(page.locator(".trip-status")).toHaveText("Devam Ediyor");
 status="Completed";await page.clock.fastForward(15000);await expect(page.locator(".trip-status")).toHaveText("Tamamlandı");
 await expect(page.getByRole("button",{name:/^(Başlat|Tamamla)$/})).toHaveCount(0);
 await expect(page.locator(".trip-stats .stat-card").filter({has:page.getByText("Tamamlanan Seferler",{exact:true})}).getByRole("heading")).toHaveText("1");
});
