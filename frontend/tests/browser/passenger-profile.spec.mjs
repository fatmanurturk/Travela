import {test,expect} from "@playwright/test";
import {mkdirSync} from "node:fs";
import { buildSeats } from "../../src/seatPlans.ts";
import { chooseSeat, installSeatSelectionMock } from "./helpers/seat-selection.mjs";
mkdirSync("../tmp/profile-qa/screenshots",{recursive:true});
const profile={id:7,fullName:"Fatma Nur Türk",email:"fatma@example.com",phoneNumber:"+905321234567",role:"Passenger",createdAt:"2026-09-01T09:00:00Z",lastLoginAt:"2026-11-03T07:00:00Z",notificationPreferences:{tripReminders:false,cancellations:false,refundUpdates:false}};
const journey={tripId:18,pnr:"A1234567890123456789012345678901B",companyName:"Travela Turizm",departureCity:"Diyarbakır",arrivalCity:"İstanbul",departureTime:"2099-11-04T07:30:00Z",arrivalTime:"2099-11-05T07:30:00Z",seatNumber:12,amount:2000,currency:"TRY"};
async function setup(page,width=1440,empty=false){
 await page.setViewportSize({width,height:1000});let saved=structuredClone(profile);
 await page.addInitScript(()=>{sessionStorage.setItem("travela_token","profile-token");sessionStorage.setItem("travela_user",JSON.stringify({id:7,fullName:"Fatma Nur Türk",role:"Passenger"}));});
 await page.route("**/api/profile",async route=>{
  if(route.request().method()==="PUT"){const body=route.request().postDataJSON();saved={...saved,fullName:body.fullName,phoneNumber:body.phoneNumber,identityNumber:body.identityNumber};await route.fulfill({json:{message:"Kişisel bilgilerin güncellendi.",fullName:saved.fullName,phoneNumber:saved.phoneNumber,identityNumber:saved.identityNumber}});}
  else await route.fulfill({json:saved});
 });
 await page.route("**/api/profile/travel-summary",route=>route.fulfill({json:{upcomingCount:empty?0:2,upcomingTrip:empty?null:journey,notifications:saved.notificationPreferences.tripReminders&&!empty?[{id:"trip-1",title:"Yolculuğun yaklaşıyor",description:"Biletli yolculuğunun kalkış bilgilerini inceleyebilirsin.",href:`/tickets/${journey.pnr}`,date:journey.departureTime}]:[]}}));
 await page.route("**/api/profile/notification-preferences",async route=>{saved.notificationPreferences=route.request().postDataJSON();await route.fulfill({json:{message:"Bildirim tercihlerin kaydedildi.",notificationPreferences:saved.notificationPreferences}});});
 return {saved:()=>saved};
}
for(const width of [390,1440])test(`Profil, telefon ve gerçek bilet özeti görünümü: ${width}px`,async({page})=>{
 await setup(page,width);await installSeatSelectionMock(page); await page.goto("/profile");
 await expect(page.getByRole("heading",{name:"Kişisel bilgiler",exact:true})).toBeVisible();
 await expect(page.getByLabel("Telefon numarası",{exact:false})).toHaveValue("+905321234567");
 await expect(page.getByRole("link",{name:"Bilet detayını gör",exact:true})).toHaveAttribute("href",`/tickets/${journey.pnr}`);
 await expect(page.locator(".account-journey-route")).toContainText("Diyarbakır");
 const widths=await page.evaluate(()=>({view:document.documentElement.clientWidth,width:document.documentElement.scrollWidth}));expect(widths.width).toBeLessThanOrEqual(widths.view+1);
 await page.screenshot({path:`../tmp/profile-qa/screenshots/profile-${width}.png`,fullPage:true});
});
test("Ad ve telefon kaydı oturumu korur; başlık güncellenir; tercihler yeniden okunur",async({page})=>{
 const mock=await setup(page);await installSeatSelectionMock(page); await page.goto("/profile");
 await page.getByLabel("Ad soyad",{exact:true}).fill("Ayşe Yılmaz");await page.getByLabel("Telefon numarası",{exact:false}).fill("+905551112233");
 await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();await expect(page.getByText("Kişisel bilgilerin güncellendi.",{exact:true})).toBeVisible();
 await expect(page.locator(".site-user-name")).toContainText("Ayşe");expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("profile-token");
 await page.getByRole("switch",{name:"Yolculuk hatırlatmaları",exact:true}).check();await page.getByRole("button",{name:"Tercihleri kaydet",exact:true}).click();
 await expect(page.getByText("Bildirim tercihlerin kaydedildi.",{exact:true})).toBeVisible();await expect(page.getByText("Yolculuğun yaklaşıyor",{exact:true})).toBeVisible();
 await page.reload();await expect(page.getByRole("switch",{name:"Yolculuk hatırlatmaları",exact:true})).toBeChecked();expect(mock.saved().phoneNumber).toBe("+905551112233");
});
test("Şifre gösterme, koşullar ve oturum yenileme çalışır",async({page})=>{
 await setup(page);await page.route("**/api/profile/change-password",route=>route.fulfill({json:{message:"Şifren değiştirildi ve diğer oturumlar kapatıldı.",token:"password-renewed-token"}}));
 await installSeatSelectionMock(page); await page.goto("/profile");await page.getByLabel("Mevcut şifre",{exact:true}).fill("CurrentPass123!");await page.getByRole("button",{name:"Mevcut şifre göster",exact:true}).click();await expect(page.getByLabel("Mevcut şifre",{exact:true})).toHaveAttribute("type","text");
 await page.getByLabel("Yeni şifre",{exact:true}).fill("BetterPass456!");await page.getByLabel("Yeni şifre tekrar",{exact:true}).fill("Mismatch456!");await expect(page.getByRole("button",{name:"Şifreyi değiştir",exact:true})).toBeDisabled();
 await page.getByLabel("Yeni şifre tekrar",{exact:true}).fill("BetterPass456!");await expect(page.locator(".account-password-checks .met")).toHaveCount(3);
 await page.getByRole("button",{name:"Şifreyi değiştir",exact:true}).click();await expect(page.getByText("Şifren değiştirildi ve diğer oturumlar kapatıldı.",{exact:true})).toBeVisible();
 expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("password-renewed-token");await expect(page.getByLabel("Yeni şifre",{exact:true})).toHaveValue("");
});
test("Diğer oturumları kapatma mevcut şifreyle onaylanır",async({page})=>{
 await setup(page);let payload;
 await page.route("**/api/profile/revoke-other-sessions",async route=>{payload=route.request().postDataJSON();await route.fulfill({json:{message:"Diğer oturumlar kapatıldı. Bu oturum açık kalacak.",token:"session-renewed-token"}});});
 await installSeatSelectionMock(page); await page.goto("/profile");await page.locator(".account-session-control summary").click();await page.getByLabel("Oturum kapatma onay şifresi",{exact:true}).fill("CurrentPass123!");await page.getByRole("button",{name:"Diğer oturumları kapat",exact:true}).click();
 await expect(page.getByText("Diğer oturumlar kapatıldı. Bu oturum açık kalacak.",{exact:true})).toBeVisible();expect(payload.currentPassword).toBe("CurrentPass123!");expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("session-renewed-token");
});
test("Boş yolculuk ve telefon hatası açıklanır; şifre hatası oturumu düşürmez",async({page})=>{
 await setup(page,390,true);await page.route("**/api/profile",route=>route.fulfill(route.request().method()==="PUT"?{status:400,json:{message:"Geçerli bir telefon numarası girin."}}:{json:profile}));
 await page.route("**/api/profile/change-password",route=>route.fulfill({status:400,json:{message:"Mevcut şifre yanlış."}}));
 await installSeatSelectionMock(page); await page.goto("/profile");await expect(page.getByText("Henüz yaklaşan bir biletli yolculuğun bulunmuyor.",{exact:true})).toBeVisible();
 await page.getByLabel("Telefon numarası",{exact:false}).fill("123");await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();await expect(page.getByText("Geçerli bir telefon numarası girin.",{exact:true})).toBeVisible();
 await page.getByLabel("Mevcut şifre",{exact:true}).fill("WrongPass123!");await page.getByLabel("Yeni şifre",{exact:true}).fill("BetterPass456!");await page.getByLabel("Yeni şifre tekrar",{exact:true}).fill("BetterPass456!");await page.getByRole("button",{name:"Şifreyi değiştir",exact:true}).click();
 await expect(page.getByText("Mevcut şifre yanlış.",{exact:true})).toBeVisible();await expect(page).toHaveURL(/\/profile$/);expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("profile-token");
});

for (const width of [390,1440]) test(`Kimlik isteğe bağlı kaydedilir, yenilemede gelir, değiştirilir ve kaldırılır: ${width}px`, async ({ page }) => {
 const mock = await setup(page,width); await installSeatSelectionMock(page); await page.goto("/profile");
 const input = page.getByLabel("T.C. Kimlik No",{exact:false});
 await expect(input).toHaveValue(""); await expect(input).not.toHaveAttribute("required");
 await expect(page.getByText("Bilet alırken yolcu bilgilerini otomatik doldurmak için kullanılır.",{exact:true})).toBeVisible();
 await input.fill("10000000146"); await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();
 await expect(page.getByText("Kişisel bilgilerin güncellendi.",{exact:true})).toBeVisible();
 await page.reload(); await expect(input).toHaveValue("10000000146");
 expect(mock.saved().identityNumber).toBe("10000000146");
 await input.fill("12345678950"); await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();
 await expect(page.getByText("Kişisel bilgilerin güncellendi.",{exact:true})).toBeVisible();
 await page.reload(); await expect(input).toHaveValue("12345678950");
 expect(await page.evaluate(()=>[...Object.values(localStorage),...Object.values(sessionStorage)].some(v=>v.includes("10000000146")||v.includes("12345678950")))).toBe(false);
 expect(page.url()).not.toMatch(/10000000146|12345678950/);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width+1);
 await page.screenshot({path:`../tmp/profile-qa/screenshots/identity-${width}.png`,fullPage:true});
 await input.fill(""); await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();
 await expect(page.getByText("Kişisel bilgilerin güncellendi.",{exact:true})).toBeVisible();
 await page.reload(); await expect(input).toHaveValue(""); expect(mock.saved().identityNumber).toBeNull();
 await page.getByLabel("Ad soyad",{exact:true}).fill("Kimliksiz Güncelleme");
 await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();
 await expect(page.getByText("Kişisel bilgilerin güncellendi.",{exact:true})).toBeVisible();
 expect(mock.saved().fullName).toBe("Kimliksiz Güncelleme"); expect(mock.saved().identityNumber).toBeNull();
});

test("Kimlik biçimi ve kontrol basamağı hataları alanda görünür; geçersiz değer gönderilmez", async ({ page }) => {
 const mock = await setup(page); await installSeatSelectionMock(page); await page.goto("/profile");
 const input = page.getByLabel("T.C. Kimlik No",{exact:false});
 for (const value of ["1000000014","00000000000","10000000136","10000000147","11111111111","1000000014a","１０００００００１４６"]) {
  await input.fill(value); await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();
  await expect(input).toHaveAttribute("aria-invalid","true");
  await expect(page.locator("#profile-identity-error")).toContainText("kontrol basamakları");
  expect(mock.saved().identityNumber).toBeUndefined();
 }
 await input.fill("10000000146"); await expect(page.locator("#profile-identity-error")).toHaveCount(0);
});

test("Sunucudan gelen kimlik alanı hatası alanın yanında gösterilir", async ({ page }) => {
 await setup(page); await page.route("**/api/profile",route=>route.fulfill(route.request().method()==="PUT"
  ? {status:400,json:{errors:{IdentityNumber:["Geçerli bir T.C. kimlik numarası giriniz."]}}} : {json:profile}));
 await installSeatSelectionMock(page); await page.goto("/profile"); const input = page.getByLabel("T.C. Kimlik No",{exact:false});
 await input.fill("10000000146"); await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();
 await expect(page.locator("#profile-identity-error")).toHaveText("Geçerli bir T.C. kimlik numarası giriniz.");
 await expect(input).toBeFocused(); await input.fill(""); await expect(page.locator("#profile-identity-error")).toHaveCount(0);
});

test("Profilde kaydedilen kimlik ilk koltuğa gelir; bilet düzenlemeleri profili değiştirmez", async ({ page }) => {
 const mock = await setup(page); await installSeatSelectionMock(page); await page.goto("/profile");
 await page.getByLabel("T.C. Kimlik No",{exact:false}).fill("10000000146");
 await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();
 await expect(page.getByText("Kişisel bilgilerin güncellendi.",{exact:true})).toBeVisible();
 await page.route("**/api/trips/public/12",r=>r.fulfill({json:{id:12,companyName:"Test Firma",departureCity:"İzmir",arrivalCity:"Ankara",departureTime:"2099-12-17T07:00:00Z",arrivalTime:"2099-12-17T13:00:00Z",durationMinutes:360,price:300,availableSeats:9,seatCapacity:9,status:"Scheduled",canReserve:true,seatPlanAvailable:true,seatLayout:"2+1",seats:buildSeats("2+1",9),occupiedSeats:[],seatOccupancies:[]}}));
 await page.route("**/api/purchases/options",r=>r.fulfill({json:{provider:"LocalTest",requiresBuyer:false}}));
 await page.route("**/api/campaigns/quote",r=>r.fulfill({json:{amount:300,originalAmount:300,discountAmount:0}}));
 await installSeatSelectionMock(page); await page.goto("/trips/12"); await chooseSeat(page,8);
 await expect(page.getByLabel("Ad soyad · Koltuk 8",{exact:true})).toHaveValue(profile.fullName);
 await expect(page.getByLabel("T.C. Kimlik No · Koltuk 8",{exact:true})).toHaveValue("10000000146");
 await chooseSeat(page,2); await expect(page.getByLabel("T.C. Kimlik No · Koltuk 2",{exact:true})).toHaveValue("");
 await page.getByLabel("T.C. Kimlik No · Koltuk 8",{exact:true}).fill("12345678950");
 await installSeatSelectionMock(page); await page.goto("/profile"); await expect(page.getByLabel("T.C. Kimlik No",{exact:false})).toHaveValue("10000000146");
 expect(mock.saved().identityNumber).toBe("10000000146");
});
