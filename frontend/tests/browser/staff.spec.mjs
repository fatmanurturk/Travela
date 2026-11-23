import { test, expect } from "@playwright/test";

const initialStaff = [
  { id:1,fullName:"Ali Şoför",duty:"Driver",isActive:true,phone:null },
  { id:2,fullName:"Ayşe Şoför",duty:"Driver",isActive:true,phone:null },
  { id:3,fullName:"Can Muavin",duty:"Attendant",isActive:true,phone:null },
  { id:4,fullName:"Deniz Muavin",duty:"Attendant",isActive:true,phone:null },
  { id:5,fullName:"Pasif Şoför",duty:"Driver",isActive:false,phone:null },
];
const trip = { id:12,companyId:1,companyName:"Test Firma",busId:4,busPlateNumber:"34 TEST 01",busCapacity:6,busIsActive:true,companyIsActive:true,
  routeId:3,departureTerminalId:1,arrivalTerminalId:2,departureCity:"İstanbul",arrivalCity:"Ankara",departureTime:"2099-01-05T07:00:00Z",arrivalTime:"2099-01-05T13:00:00Z",
  price:550,availableSeats:6,status:"Scheduled",createdAt:"2026-11-01T07:00:00Z",updatedAt:null,changeVersion:0 };
const conflictMessage="Seçtiğiniz personelin başka bir seferde görevi var. Sefer saatleri çakıştığı için atama yapılamaz. Lütfen farklı personel seçin veya sefer saatlerini düzenleyin.";
const warning = { code:"crew_conflict",message:conflictMessage,
  conflicts:[{staffId:1,fullName:"Ali Şoför",duty:"Driver",tripId:77,departureCity:"Ankara",arrivalCity:"İzmir",departureTime:"2099-01-05T06:00:00Z",arrivalTime:"2099-01-05T14:00:00Z"},
    {staffId:3,fullName:"Can Muavin",duty:"Attendant",tripId:78,departureCity:"İstanbul",arrivalCity:"Bursa",departureTime:"2099-01-05T06:00:00Z",arrivalTime:"2099-01-05T12:00:00Z"}] };
async function setup(page,{staff=initialStaff,assigned=[],conflict=false}={}) {
  const state={staff:structuredClone(staff),assigned:[...assigned],posts:[],saves:[],needsConflict:conflict,trip:{...trip},version:0};
  await page.addInitScript(()=>{sessionStorage.setItem("travela_token","crew-ui");sessionStorage.setItem("travela_user",JSON.stringify({role:"Company",name:"Firma Yetkilisi"}));});
  await page.route("**/api/**",async r=>{
    const url=new URL(r.request().url()),path=url.pathname,method=r.request().method();
    if(path==="/api/company/profile")return r.fulfill({json:{name:"Test Firma"}});
    if(path==="/api/company/staff"&&method==="GET") {
      const search=url.searchParams.get("search")?.toLocaleLowerCase("tr-TR"), duty=url.searchParams.get("duty"),active=url.searchParams.get("isActive");
      return r.fulfill({json:state.staff.filter(s=>(!search||s.fullName.toLocaleLowerCase("tr-TR").includes(search))&&(!duty||s.duty===duty)&&(!active||String(s.isActive)===active))});
    }
    if(path.startsWith("/api/company/staff")&&method!=="GET") {
      expect(method).not.toBe("DELETE"); const body=r.request().postDataJSON();
      expect(body).not.toHaveProperty("companyId");expect(body).not.toHaveProperty("password");expect(body).not.toHaveProperty("identityNumber");
      if(method==="POST")state.staff.push({...body,id:20});else Object.assign(state.staff.find(s=>s.id===Number(path.split("/").at(-1))),body);
      return r.fulfill({status:method==="POST"?201:200,json:{message:"Personel kaydedildi."}});
    }
    if(path==="/api/buses")return r.fulfill({json:{items:[{id:4,companyId:1,plateNumber:"34 TEST 01",seatCapacity:6,isActive:true}]}});
    if(path==="/api/routes")return r.fulfill({json:[{id:3,departureCity:"İstanbul",arrivalCity:"Ankara",isActive:true}]});
    if(path==="/api/terminals")return r.fulfill({json:[{id:1,city:"İstanbul",name:"İstanbul Terminali",address:"Test"},{id:2,city:"Ankara",name:"Ankara Terminali",address:"Test"}]});
    if(path==="/api/trips/summary")return r.fulfill({json:{totalTrips:1,scheduledTrips:1,inProgressTrips:0,completedTrips:0,cancelledTrips:0}});
    if(path==="/api/trips"&&method==="GET")return r.fulfill({json:{items:[state.trip],totalCount:1,totalPages:1}});
    if(path==="/api/trips/12"&&method==="GET")return r.fulfill({json:state.trip});
    if(path==="/api/trips/12/crew"&&method==="GET")return r.fulfill({json:{crew:state.assigned.map(item=>({...state.staff.find(s=>s.id===item.id),...item})),changeVersion:state.version}});
    if(path==="/api/trips/12/stops")return r.fulfill({json:{stops:[],fares:[],missingFares:[],changeVersion:state.version,topologyLocked:false}});
    if(path==="/api/trips/12/changes"&&method==="GET")return r.fulfill({json:{changes:[],changeVersion:state.version}});
    if((path==="/api/trips"||path==="/api/trips/12"||path==="/api/trips/12/crew"||path==="/api/trips/batch"||path==="/api/trips/12/changes")&&method!=="GET") {
      const body=r.request().postDataJSON();state.posts.push({path,body});
      const target=body.trips?.[0]??body;
      expect(target).not.toHaveProperty("crewConfirmation");
      if(state.needsConflict)return r.fulfill({status:409,json:{...warning,...(body.trips?{batchIndex:0}:{})}});
      state.saves.push({path,body});state.version++;
      if(path.endsWith("/crew"))state.assigned=[...body.crew.drivers.map(id=>({id,duty:"Driver"})),...body.crew.attendants.map(id=>({id,duty:"Attendant"}))];
      if(path==="/api/trips/12")state.trip={...state.trip,...body,updatedAt:"2026-11-23T10:00:00Z"};
      if(path.endsWith("/changes"))return r.fulfill({json:body.preview?{before:trip,after:{...trip,departureTime:body.departureTime,arrivalTime:body.arrivalTime},affectedTickets:0,releasedHolds:0,changeVersion:state.version,cancellationUntil:body.departureTime}:{message:"Değişiklik kaydedildi.",change:{id:"change1",version:state.version,createdAt:"2026-11-23T10:00:00Z",reason:body.reason,beforeJson:JSON.stringify(trip),afterJson:JSON.stringify(trip),affectedTickets:0,releasedHolds:0}}});
      return r.fulfill({status:path==="/api/trips"||body.trips?201:200,json:{message:"Sefer kaydedildi.",tripId:13}});
    }
    return r.fulfill({json:[]});
  });
  return state;
}
async function newTrip(page) {
  await page.goto("/company/trips");await page.getByRole("button",{name:"Kopyala",exact:true}).click();
  await page.getByLabel("Kalkış tarihi ve saati",{exact:false}).fill("2099-01-05T10:00");
  await page.getByLabel("Varış tarihi ve saati",{exact:false}).fill("2099-01-05T16:00");
}

for(const width of [390,1440])test(`Personel ekleme, düzenleme, pasife alma ve filtreleme: ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:1000});const state=await setup(page);await page.goto("/company/staff");
  await expect(page.getByRole("navigation",{name:"Firma menüsü"}).getByRole("link",{name:"Personeller",exact:true})).toHaveAttribute("href","/company/staff");
  await page.getByRole("button",{name:"Personel ekle",exact:true}).click();
  const form=page.getByRole("form",{name:"Personel kaydı"});await form.getByLabel("Ad soyad",{exact:true}).fill("Yeni Personel");
  await form.getByLabel("Görev",{exact:true}).selectOption("Attendant");await form.getByLabel("Telefon",{exact:false}).fill("05321234567");
  await form.getByRole("button",{name:"Personeli kaydet"}).click();
  const card=page.locator(".staff-card").filter({has:page.getByRole("heading",{name:"Yeni Personel",exact:true})});await expect(card).toContainText("Muavin · Aktif");
  await card.getByRole("button",{name:"Düzenle",exact:true}).click();await form.getByLabel("Ad soyad",{exact:true}).fill("Yeni Personel Güncel");
  await form.getByRole("button",{name:"Personeli kaydet"}).click();
  const edited=page.locator(".staff-card").filter({hasText:"Yeni Personel Güncel"});await edited.getByRole("button",{name:"Pasife al",exact:true}).click();await expect(edited).toContainText("Muavin · Pasif");
  await page.getByLabel("Ad soyada göre ara").fill("Güncel");await page.getByLabel("Göreve göre filtrele").selectOption("Attendant");await page.getByLabel("Duruma göre filtrele").selectOption("false");
  await expect(page.locator(".staff-card")).toHaveCount(1);expect(state.staff.find(s=>s.id===20).isActive).toBe(false);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width+1);
  await page.screenshot({path:`../tmp/staff/personnel-${width}.png`,fullPage:true});
});

for(const width of [390,1440])test(`Şoför ve muavin çakışması engellenir; yalnız düzenlemeye dönülür: ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:1000});const state=await setup(page,{conflict:true});await newTrip(page);
  const fields=page.getByRole("region",{name:"Görevli ekip seçimi"});
  for(const name of ["Ali Şoför","Ayşe Şoför","Can Muavin","Deniz Muavin"])await fields.getByLabel(name,{exact:true}).check();
  await expect(fields.getByLabel("Pasif Şoför",{exact:true})).toHaveCount(0);
  await page.getByRole("button",{name:"Kaydet",exact:true}).click();
  const dialog=page.getByRole("dialog",{name:"Personel atanamıyor"});await expect(dialog).toContainText("Sefer #77 · Ankara → İzmir");await expect(dialog).toContainText("09:00");
  await expect(dialog).toContainText(conflictMessage);await expect(dialog).toContainText("Ali Şoför · Şoför");await expect(dialog).toContainText("Can Muavin · Muavin");
  await expect(dialog.getByRole("button")).toHaveCount(1);await expect(dialog.getByRole("button",{name:"Çakışmayı onayla ve devam et"})).toHaveCount(0);
  const box=await dialog.boundingBox();expect(box.x).toBeGreaterThanOrEqual(0);expect(box.x+box.width).toBeLessThanOrEqual(width);expect(box.y+box.height).toBeLessThanOrEqual(1000);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width+1);
  await dialog.screenshot({path:`../tmp/staff/crew-warning-${width}.png`});
  expect(state.saves).toHaveLength(0);await dialog.getByRole("button",{name:"Atamayı düzenle",exact:true}).click();
  await expect(fields.getByLabel("Ali Şoför",{exact:true})).toBeChecked();
  await expect(page.getByLabel("Bilet fiyatı (TL)",{exact:false})).toHaveValue("550");
  for(const name of ["Ayşe Şoför","Can Muavin","Deniz Muavin"])await expect(fields.getByLabel(name,{exact:true})).toBeChecked();
  await expect(page.getByRole("button",{name:"Kaydet",exact:true})).toBeEnabled();expect(state.posts).toHaveLength(1);expect(state.saves).toHaveLength(0);
  await fields.getByLabel("Ali Şoför",{exact:true}).uncheck();await fields.getByLabel("Can Muavin",{exact:true}).uncheck();state.needsConflict=false;
  await page.getByRole("button",{name:"Kaydet",exact:true}).click();
  await expect(page.getByText("Sefer kaydedildi.",{exact:true})).toBeVisible();
  expect(state.saves[0].body).toMatchObject({crew:{drivers:[2],attendants:[4]}});expect(state.posts).toHaveLength(2);
});

test("Personel yoksa açıklama ve yeni sekme bağlantısı var; sefer taslağı korunur",async({page})=>{
  await setup(page,{staff:[]});await newTrip(page);
  await expect(page.getByText("Aktif şoför kaydı bulunmuyor.")).toBeVisible();await expect(page.getByText("Aktif muavin kaydı bulunmuyor.")).toBeVisible();
  const link=page.getByRole("link",{name:"Personel ekle (yeni sekme)"}).first();await expect(link).toHaveAttribute("target","_blank");await expect(link).toHaveAttribute("href","/company/staff");
  await page.getByRole("button",{name:"Personel listesini yenile"}).click();await expect(page.getByLabel("Kalkış tarihi ve saati",{exact:false})).toHaveValue("2099-01-05T10:00");
  await page.getByRole("button",{name:"Kaydet",exact:true}).click();await expect(page.getByText("Sefer kaydedildi.",{exact:true})).toBeVisible();
});

test("Detayda pasif ekip görünür; ekip ve atamalar seferin diğer alanlarından bağımsız düzenlenir",async({page})=>{
  const state=await setup(page,{assigned:[{id:5,duty:"Driver"},{id:3,duty:"Attendant"}]});await page.goto("/company/trips?detail=12");
  const panel=page.getByRole("region",{name:"Seferin görevli ekibi"});await expect(panel).toContainText("Pasif Şoför · Şoför · Pasif");
  await panel.getByRole("button",{name:"Ekibi düzenle"}).click();const fields=panel.getByRole("region",{name:"Görevli ekip seçimi"});
  await expect(fields.getByLabel(/Pasif Şoför/)).toBeChecked();await fields.getByLabel(/Pasif Şoför/).uncheck();
  await fields.getByLabel("Ali Şoför",{exact:true}).check();await fields.getByLabel("Can Muavin",{exact:true}).uncheck();
  await panel.getByRole("button",{name:"Ekibi kaydet",exact:true}).click();await expect(panel).toContainText("Ali Şoför · Şoför");
  expect(state.saves[0].body.crew).toEqual({drivers:[1],attendants:[]});
  await panel.getByRole("button",{name:"Ekibi düzenle"}).click();await fields.getByLabel("Ali Şoför",{exact:true}).uncheck();
  await panel.getByRole("button",{name:"Ekibi kaydet",exact:true}).click();await expect(panel).toContainText("Henüz personel atanmadı.");
});

test("Sefer saati değişikliğinde çakışma engellenir ve düzenlenen saat ile pasif atama formda korunur",async({page})=>{
  const state=await setup(page,{assigned:[{id:5,duty:"Driver"}],conflict:true});await page.goto("/company/trips");
  await page.getByRole("button",{name:"Düzenle",exact:true}).click();await expect(page.getByLabel(/Pasif Şoför/)).toBeChecked();
  await page.getByLabel("Kalkış tarihi ve saati",{exact:false}).fill("2099-01-05T11:00");await page.getByRole("button",{name:"Kaydet",exact:true}).click();
  await page.getByRole("dialog",{name:"Personel atanamıyor"}).getByRole("button",{name:"Atamayı düzenle"}).click();
  await expect(page.getByLabel(/Pasif Şoför/)).toBeChecked();await expect(page.getByLabel("Kalkış tarihi ve saati",{exact:false})).toHaveValue("2099-01-05T11:00");
  expect(state.posts).toHaveLength(1);expect(state.saves).toHaveLength(0);expect(state.posts[0].body).toMatchObject({crew:{drivers:[5],attendants:[]},departureTime:"2099-01-05T08:00:00.000Z"});
});

test("Toplu sefer çakışmasında tekrar istek gönderilmez ve tarih seçimleri korunur",async({page})=>{
  const state=await setup(page,{conflict:true});await newTrip(page);await page.getByLabel("Ali Şoför",{exact:true}).check();
  await page.getByLabel("Oluşturma biçimi").selectOption("dates");
  for(const date of ["2099-01-05","2099-01-07"]) {await page.getByLabel("Sefer tarihi",{exact:true}).fill(date);await page.getByRole("button",{name:"Tarihi ekle",exact:true}).click();}
  await page.getByRole("button",{name:"2 Seferi Oluştur",exact:true}).click();await page.getByRole("dialog",{name:"Personel atanamıyor"}).getByRole("button",{name:"Atamayı düzenle"}).click();
  await expect(page.getByRole("button",{name:"2 Seferi Oluştur",exact:true})).toBeEnabled();await expect(page.getByLabel("Ali Şoför",{exact:true})).toBeChecked();
  await expect(page.getByRole("table",{name:"Oluşturulacak seferler (Türkiye saati)"}).getByRole("row")).toHaveCount(3);
  expect(state.posts).toHaveLength(1);expect(state.saves).toHaveLength(0);
});

test("Detaydan ekip kaydı çakışırsa seçili ekip korunur ve hiçbir ek istek yapılmaz",async({page})=>{
  const state=await setup(page,{conflict:true});await page.goto("/company/trips?detail=12");
  const panel=page.getByRole("region",{name:"Seferin görevli ekibi"});await panel.getByRole("button",{name:"Ekibi düzenle"}).click();
  await panel.getByLabel("Ali Şoför",{exact:true}).check();await panel.getByLabel("Can Muavin",{exact:true}).check();
  await panel.getByRole("button",{name:"Ekibi kaydet"}).click();await page.getByRole("dialog",{name:"Personel atanamıyor"}).getByRole("button",{name:"Atamayı düzenle"}).click();
  await expect(panel.getByLabel("Ali Şoför",{exact:true})).toBeChecked();await expect(panel.getByLabel("Can Muavin",{exact:true})).toBeChecked();
  expect(state.posts).toHaveLength(1);expect(state.saves).toHaveLength(0);expect(state.assigned).toEqual([]);
});

test("Kontrollü saat değişikliği çakışınca uygulanamaz; tarih ve gerekçe kaybolmaz",async({page})=>{
  const state=await setup(page,{conflict:true});await page.goto("/company/trips?detail=12");
  const editor=page.getByRole("region",{name:"Kontrollü sefer değişikliği"});
  await editor.getByLabel("Yeni kalkış (Türkiye)",{exact:true}).fill("2099-01-05T11:00");
  await editor.getByLabel("Değişiklik gerekçesi").fill("Sefer saatini değiştirme denemesi");
  await editor.getByRole("button",{name:"Değişikliği önizle"}).click();await page.getByRole("dialog",{name:"Personel atanamıyor"}).getByRole("button",{name:"Atamayı düzenle"}).click();
  await expect(editor.getByLabel("Yeni kalkış (Türkiye)",{exact:true})).toHaveValue("2099-01-05T11:00");
  await expect(editor.getByLabel("Değişiklik gerekçesi")).toHaveValue("Sefer saatini değiştirme denemesi");
  await expect(editor.getByRole("button",{name:"Değişikliği uygula ve bildir"})).toHaveCount(0);expect(state.posts).toHaveLength(1);expect(state.saves).toHaveLength(0);
});
