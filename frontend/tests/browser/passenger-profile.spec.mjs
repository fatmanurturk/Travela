import {test,expect} from "@playwright/test";
import {mkdirSync} from "node:fs";
mkdirSync("../tmp/profile-qa/screenshots",{recursive:true});
const profile={id:7,fullName:"Fatma Nur Türk",email:"fatma@example.com",phoneNumber:"+905321234567",role:"Passenger",createdAt:"2026-09-01T09:00:00Z",lastLoginAt:"2026-11-03T07:00:00Z",notificationPreferences:{tripReminders:false,cancellations:false,refundUpdates:false}};
const journey={tripId:18,pnr:"A1234567890123456789012345678901B",companyName:"Travela Turizm",departureCity:"Diyarbakır",arrivalCity:"İstanbul",departureTime:"2099-11-04T07:30:00Z",arrivalTime:"2099-11-05T07:30:00Z",seatNumber:12,amount:2000,currency:"TRY"};
async function setup(page,width=1440,empty=false){
 await page.setViewportSize({width,height:1000});let saved=structuredClone(profile);
 await page.addInitScript(()=>{sessionStorage.setItem("travela_token","profile-token");sessionStorage.setItem("travela_user",JSON.stringify({id:7,fullName:"Fatma Nur Türk",role:"Passenger"}));});
 await page.route("**/api/profile",async route=>{
  if(route.request().method()==="PUT"){const body=route.request().postDataJSON();saved={...saved,fullName:body.fullName,phoneNumber:body.phoneNumber};await route.fulfill({json:{message:"Kişisel bilgilerin güncellendi.",fullName:saved.fullName,phoneNumber:saved.phoneNumber}});}
  else await route.fulfill({json:saved});
 });
 await page.route("**/api/profile/travel-summary",route=>route.fulfill({json:{upcomingCount:empty?0:2,upcomingTrip:empty?null:journey,notifications:saved.notificationPreferences.tripReminders&&!empty?[{id:"trip-1",title:"Yolculuğun yaklaşıyor",description:"Biletli yolculuğunun kalkış bilgilerini inceleyebilirsin.",href:`/tickets/${journey.pnr}`,date:journey.departureTime}]:[]}}));
 await page.route("**/api/profile/notification-preferences",async route=>{saved.notificationPreferences=route.request().postDataJSON();await route.fulfill({json:{message:"Bildirim tercihlerin kaydedildi.",notificationPreferences:saved.notificationPreferences}});});
 return {saved:()=>saved};
}
for(const width of [390,1440])test(`Profil, telefon ve gerçek bilet özeti görünümü: ${width}px`,async({page})=>{
 await setup(page,width);await page.goto("/profile");
 await expect(page.getByRole("heading",{name:"Kişisel bilgiler",exact:true})).toBeVisible();
 await expect(page.getByLabel("Telefon numarası",{exact:false})).toHaveValue("+905321234567");
 await expect(page.getByRole("link",{name:"Bilet detayını gör",exact:true})).toHaveAttribute("href",`/tickets/${journey.pnr}`);
 await expect(page.locator(".account-journey-route")).toContainText("Diyarbakır");
 const widths=await page.evaluate(()=>({view:document.documentElement.clientWidth,width:document.documentElement.scrollWidth}));expect(widths.width).toBeLessThanOrEqual(widths.view+1);
 await page.screenshot({path:`../tmp/profile-qa/screenshots/profile-${width}.png`,fullPage:true});
});
test("Ad ve telefon kaydı oturumu korur; başlık güncellenir; tercihler yeniden okunur",async({page})=>{
 const mock=await setup(page);await page.goto("/profile");
 await page.getByLabel("Ad soyad",{exact:true}).fill("Ayşe Yılmaz");await page.getByLabel("Telefon numarası",{exact:false}).fill("+905551112233");
 await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();await expect(page.getByText("Kişisel bilgilerin güncellendi.",{exact:true})).toBeVisible();
 await expect(page.locator(".site-user-name")).toContainText("Ayşe");expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("profile-token");
 await page.getByRole("switch",{name:"Yolculuk hatırlatmaları",exact:true}).check();await page.getByRole("button",{name:"Tercihleri kaydet",exact:true}).click();
 await expect(page.getByText("Bildirim tercihlerin kaydedildi.",{exact:true})).toBeVisible();await expect(page.getByText("Yolculuğun yaklaşıyor",{exact:true})).toBeVisible();
 await page.reload();await expect(page.getByRole("switch",{name:"Yolculuk hatırlatmaları",exact:true})).toBeChecked();expect(mock.saved().phoneNumber).toBe("+905551112233");
});
test("Şifre gösterme, koşullar ve oturum yenileme çalışır",async({page})=>{
 await setup(page);await page.route("**/api/profile/change-password",route=>route.fulfill({json:{message:"Şifren değiştirildi ve diğer oturumlar kapatıldı.",token:"password-renewed-token"}}));
 await page.goto("/profile");await page.getByLabel("Mevcut şifre",{exact:true}).fill("CurrentPass123!");await page.getByRole("button",{name:"Mevcut şifre göster",exact:true}).click();await expect(page.getByLabel("Mevcut şifre",{exact:true})).toHaveAttribute("type","text");
 await page.getByLabel("Yeni şifre",{exact:true}).fill("BetterPass456!");await page.getByLabel("Yeni şifre tekrar",{exact:true}).fill("Mismatch456!");await expect(page.getByRole("button",{name:"Şifreyi değiştir",exact:true})).toBeDisabled();
 await page.getByLabel("Yeni şifre tekrar",{exact:true}).fill("BetterPass456!");await expect(page.locator(".account-password-checks .met")).toHaveCount(3);
 await page.getByRole("button",{name:"Şifreyi değiştir",exact:true}).click();await expect(page.getByText("Şifren değiştirildi ve diğer oturumlar kapatıldı.",{exact:true})).toBeVisible();
 expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("password-renewed-token");await expect(page.getByLabel("Yeni şifre",{exact:true})).toHaveValue("");
});
test("Diğer oturumları kapatma mevcut şifreyle onaylanır",async({page})=>{
 await setup(page);let payload;
 await page.route("**/api/profile/revoke-other-sessions",async route=>{payload=route.request().postDataJSON();await route.fulfill({json:{message:"Diğer oturumlar kapatıldı. Bu oturum açık kalacak.",token:"session-renewed-token"}});});
 await page.goto("/profile");await page.locator(".account-session-control summary").click();await page.getByLabel("Oturum kapatma onay şifresi",{exact:true}).fill("CurrentPass123!");await page.getByRole("button",{name:"Diğer oturumları kapat",exact:true}).click();
 await expect(page.getByText("Diğer oturumlar kapatıldı. Bu oturum açık kalacak.",{exact:true})).toBeVisible();expect(payload.currentPassword).toBe("CurrentPass123!");expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("session-renewed-token");
});
test("Boş yolculuk ve telefon hatası açıklanır; şifre hatası oturumu düşürmez",async({page})=>{
 await setup(page,390,true);await page.route("**/api/profile",route=>route.fulfill(route.request().method()==="PUT"?{status:400,json:{message:"Geçerli bir telefon numarası girin."}}:{json:profile}));
 await page.route("**/api/profile/change-password",route=>route.fulfill({status:400,json:{message:"Mevcut şifre yanlış."}}));
 await page.goto("/profile");await expect(page.getByText("Henüz yaklaşan bir biletli yolculuğun bulunmuyor.",{exact:true})).toBeVisible();
 await page.getByLabel("Telefon numarası",{exact:false}).fill("123");await page.getByRole("button",{name:"Bilgileri kaydet",exact:true}).click();await expect(page.getByText("Geçerli bir telefon numarası girin.",{exact:true})).toBeVisible();
 await page.getByLabel("Mevcut şifre",{exact:true}).fill("WrongPass123!");await page.getByLabel("Yeni şifre",{exact:true}).fill("BetterPass456!");await page.getByLabel("Yeni şifre tekrar",{exact:true}).fill("BetterPass456!");await page.getByRole("button",{name:"Şifreyi değiştir",exact:true}).click();
 await expect(page.getByText("Mevcut şifre yanlış.",{exact:true})).toBeVisible();await expect(page).toHaveURL(/\/profile$/);expect(await page.evaluate(()=>sessionStorage.getItem("travela_token"))).toBe("profile-token");
});
