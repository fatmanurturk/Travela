using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Api.Models;
using Travela.Data;
using Travela.Models;

var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));Directory.SetCurrentDirectory(root);
using var source=new TravelaDbContextFactory().CreateDbContext([]);
if(args.Contains("--snapshot-existing")||args.Contains("--verify-existing")){
 var counts=new {Users=await source.Users.CountAsync(),Trips=await source.Trips.CountAsync(),Reservations=await source.Reservations.CountAsync(),Payments=await source.PaymentOrders.CountAsync()};
 var directory=Path.Combine(root,"tmp","profile-qa");Directory.CreateDirectory(directory);var snapshot=Path.Combine(directory,"live-before.json");
 if(args.Contains("--snapshot-existing")){await File.WriteAllTextAsync(snapshot,JsonSerializer.Serialize(counts));Console.WriteLine("Mevcut kayıt sayıları değişiklik öncesi saklandı.");return;}
 if((await source.Database.GetPendingMigrationsAsync()).Any())throw new Exception("Bekleyen migration var.");
 if(await File.ReadAllTextAsync(snapshot)!=JsonSerializer.Serialize(counts))throw new Exception("Mevcut kayıt sayıları değişti.");
 Console.WriteLine("Profil alanları eklendi; mevcut kullanıcı, sefer, bilet ve ödeme sayıları korundu.");return;
}
var database="TravelaProfileTests_"+Guid.NewGuid().ToString("N");
var cs=new SqlConnectionStringBuilder(source.Database.GetConnectionString()){InitialCatalog=database};
await using var db=new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(cs.ConnectionString).Options);
Process? server=null;var log=new StringBuilder();var passed=0;
try{
 await db.GetService<IMigrator>().MigrateAsync("20261102131249_AddReportingDatesAndIndexes");
 var legacyCreated=DateTime.UtcNow.AddDays(-60);
 await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (FullName, Email, PasswordHash, Role, IsActive, MustChangePassword, SecurityVersion, CreatedAt) VALUES (N'Eski Yolcu', N'legacy@profile.example', N'old-hash', N'Passenger', 1, 0, 1, {legacyCreated})");
 await db.Database.MigrateAsync();
 var legacy=await db.Users.AsNoTracking().SingleAsync(u=>u.Email=="legacy@profile.example");
 Check(legacy.PhoneNumber==null&&!legacy.TripRemindersEnabled&&!legacy.CancellationNotificationsEnabled&&!legacy.RefundNotificationsEnabled&&legacy.PasswordHash=="old-hash"&&legacy.CreatedAt==legacyCreated,"Ek alanlar mevcut kullanıcıyı ve geçmişini korur; bildirimler kapalı başlar");
 var user=NewUser("owner");var other=NewUser("other");var admin=NewUser("admin","Admin");var limited=NewUser("limited");
 var company=new Company{Name="Yolculuk Firması"};var bus=new Bus{Company=company,PlateNumber="34 PRF 1",NormalizedPlateNumber="34PRF1",SeatCapacity=40};
 var route=new Travela.Api.Models.Route{DepartureCity="İstanbul",ArrivalCity="Ankara",EstimatedDurationMinutes=360};
 db.AddRange(user,other,admin,limited,company,bus,route);await db.SaveChangesAsync();
 var now=DateTime.UtcNow;
 Trip Trip(DateTime at)=>new(){Company=company,Bus=bus,Route=route,DepartureTime=at,ArrivalTime=at.AddHours(6),Price=550,AvailableSeats=30};
 var near=Trip(now.AddHours(3));var later=Trip(now.AddDays(3));var past=Trip(now.AddDays(-2));db.AddRange(near,later,past);await db.SaveChangesAsync();
 PaymentOrder Order(User passenger,Trip trip,int seat,bool cancelled=false,string status="Paid"){
  var reservation=new Reservation{Passenger=passenger,Trip=trip,SeatNumber=seat,Price=550,Kind=status=="Paid"?"Ticket":"Hold",Pnr=status=="Paid"?Guid.NewGuid().ToString("N").ToUpperInvariant():null,
   IssuedAt=status=="Paid"?now.AddDays(-3):null,HoldExpiresAt=status=="Paid"?null:now.AddMinutes(5),Status=cancelled?"Cancelled":"Active",CancelledAt=cancelled?now.AddHours(-1):null};
  var order=new PaymentOrder{PassengerId=passenger.Id,Reservation=reservation,RequestKey=Guid.NewGuid(),Amount=550,PaymentStatus=status,PaidAt=status=="Paid"?now.AddDays(-3):null,ProviderReference=status=="Paid"?Guid.NewGuid().ToString("N"):null,
   RefundStatus=cancelled?"Pending":"None",RefundAmount=cancelled?550:0,RefundStatusChangedAt=cancelled?now.AddMinutes(-30):null,RefundRequestedAt=cancelled?now.AddHours(-1):null};db.Add(order);return order;
 }
 var nearest=Order(user,near,1);Order(user,later,1);Order(user,past,1);var cancelled=Order(user,near,2,true);Order(user,near,3,false,"Pending");var foreign=Order(other,near,4);
 db.Reservations.Add(new Reservation{Passenger=user,Trip=near,SeatNumber=5,Price=550,Kind="Legacy"});await db.SaveChangesAsync();
 var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
 var config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
 var start=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.Combine(root,"backend"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 start.ArgumentList.Add(Path.Combine(root,"backend","bin",config,"net10.0","Travela.API.dll"));
 start.Environment["ASPNETCORE_URLS"]=$"http://127.0.0.1:{port}";start.Environment["ASPNETCORE_ENVIRONMENT"]="Testing";start.Environment["ConnectionStrings__DefaultConnection"]=cs.ConnectionString;
 start.Environment["Jwt__Key"]=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");start.Environment["Jwt__Issuer"]="ProfileTests";start.Environment["Jwt__Audience"]="ProfileTests";start.Environment["Payments__RecoveryEnabled"]="false";start.Environment["Logging__LogLevel__Default"]="Warning";
 server=Process.Start(start)!;server.OutputDataReceived+=(_,e)=>{lock(log)log.AppendLine(e.Data);};server.ErrorDataReceived+=(_,e)=>{lock(log)log.AppendLine(e.Data);};server.BeginOutputReadLine();server.BeginErrorReadLine();
 using var http=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(30)};
 for(var i=0;i<100;i++){try{if((await http.GetAsync("/api/test")).IsSuccessStatusCode)break;}catch(HttpRequestException){}if(server.HasExited)throw new Exception(log.ToString());await Task.Delay(100);}
 async Task<HttpResponseMessage> Send(string method,string path,string? token,object? body=null){using var request=new HttpRequestMessage(new HttpMethod(method),path);if(token!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);if(body!=null)request.Content=JsonContent.Create(body);return await http.SendAsync(request);}
 async Task<string> Login(User account,string password="TestPass123!"){var response=await http.PostAsJsonAsync("/api/auth/login",new{email=account.Email,password});if(!response.IsSuccessStatusCode)throw new Exception(await response.Content.ReadAsStringAsync()+log);return(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;}
 async Task<JsonElement> Json(HttpResponseMessage response){if(!response.IsSuccessStatusCode)throw new Exception(await response.Content.ReadAsStringAsync()+log);return await response.Content.ReadFromJsonAsync<JsonElement>();}
 var token=await Login(user);var olderSession=await Login(user);var otherToken=await Login(other);var adminToken=await Login(admin);
 Check(token!=olderSession,"İki giriş ayrı oturum belirteci üretir");
 foreach(var endpoint in new[]{"/api/profile","/api/profile/travel-summary"}){
  Check((await Send("GET",endpoint,null)).StatusCode==HttpStatusCode.Unauthorized,"Profil verisi giriş gerektirir");
  Check((await Send("GET",endpoint,adminToken)).StatusCode==HttpStatusCode.Forbidden,"Yolcu profiline yönetici rolü erişemez");
 }
 var initialResponse=await Send("GET","/api/profile?userId="+other.Id,token);var initial=await Json(initialResponse);
 Check(initial.GetProperty("id").GetInt32()==user.Id&&initial.GetProperty("phoneNumber").ValueKind==JsonValueKind.Null,"Profil sahibini istemci parametresi değiştiremez");
 Check(initialResponse.Headers.CacheControl?.NoStore==true&&initial.GetProperty("createdAt").GetString()!.EndsWith('Z'),"Kişisel veriler önbelleğe alınmaz; tarih UTC olarak iletilir");
 var update=await Send("PUT","/api/profile",token,new{fullName="Güncel Yolcu",phoneNumber="0532 123 45 67",userId=other.Id});var updated=await Json(update);
 Check(updated.GetProperty("phoneNumber").GetString()=="+905321234567","Telefon numarası normalize edilerek saklanır");
 Check((await Send("GET","/api/profile",token)).IsSuccessStatusCode&&await db.Users.AsNoTracking().AnyAsync(u=>u.Id==other.Id&&u.PhoneNumber==null),"Profil kaydı oturumu açık tutar ve başka kullanıcıyı değiştirmez");
 Check((await Send("PUT","/api/profile",token,new{fullName="Güncel Yolcu",phoneNumber="telefon123"})).StatusCode==HttpStatusCode.BadRequest,"Geçersiz telefon kaydedilmez");
 Check((await Send("PUT","/api/profile",token,new{fullName="  ",phoneNumber=""})).StatusCode==HttpStatusCode.BadRequest,"Boş ad soyad kaydedilmez");
 Check((await Send("PUT","/api/profile",token,new{fullName=new string('A',121)})).StatusCode==HttpStatusCode.BadRequest,"Ad soyad uzunluğu denetlenir");
 Check((await Send("PUT","/api/profile",token,new{fullName="Güncel Yolcu",phoneNumber="+44 20 7946 0958"})).IsSuccessStatusCode,"Uluslararası telefon numarası desteklenir");
 await Json(await Send("PUT","/api/profile",token,new{fullName="Güncel Yolcu",phoneNumber=(string?)null}));
 Check((await Json(await Send("GET","/api/profile",token))).GetProperty("phoneNumber").ValueKind==JsonValueKind.Null,"Telefon numarası kaldırılabilir");
 var summary=await Json(await Send("GET","/api/profile/travel-summary?passengerId="+other.Id,token));
 Check(summary.GetProperty("upcomingCount").GetInt32()==2&&summary.GetProperty("upcomingTrip").GetProperty("pnr").GetString()==nearest.Reservation.Pnr,"Özet en yakın ödenmiş aktif bileti seçer; geçmiş, iptal, tutma ve ücretsiz kayıtları dışlar");
 Check(summary.GetProperty("notifications").GetArrayLength()==0,"Tercihler kapalıyken uygulama içi bildirim yoktur");
 var preferenceResponse=await Json(await Send("PUT","/api/profile/notification-preferences",token,new{tripReminders=true,cancellations=true,refundUpdates=true}));
 var reread=await Json(await Send("GET","/api/profile",token));
 Check(reread.GetProperty("notificationPreferences").GetProperty("tripReminders").GetBoolean()&&await db.Users.AsNoTracking().AnyAsync(u=>u.Id==other.Id&&!u.TripRemindersEnabled),"Tercihler kalıcıdır ve kullanıcıya özeldir");
 Check((await Send("PUT","/api/profile/notification-preferences",token,new{tripReminders=false})).StatusCode==HttpStatusCode.BadRequest,"Eksik tercih isteği diğer seçimleri sessizce kapatmaz");
 var notices=await Json(await Send("GET","/api/profile/travel-summary",token));var noticeText=notices.GetRawText();
 Check(notices.GetProperty("notifications").GetArrayLength()==3&&!noticeText.Contains(foreign.Reservation.Pnr!),"Hatırlatma, iptal ve iade bildirimleri yalnızca kendi kayıtlarından üretilir");
 await Json(await Send("PUT","/api/profile/notification-preferences",token,new{tripReminders=false,cancellations=false,refundUpdates=true}));
 Check((await Json(await Send("GET","/api/profile/travel-summary",token))).GetProperty("notifications").GetArrayLength()==1,"Kapatılan bildirim türleri özette gösterilmez");
 var empty=await Json(await Send("GET","/api/profile/travel-summary",await Login(limited)));
 Check(empty.GetProperty("upcomingTrip").ValueKind==JsonValueKind.Null&&empty.GetProperty("upcomingCount").GetInt32()==0,"Biletsiz yolcu için doğru boş özet döner");
 Check((await Send("POST","/api/profile/revoke-other-sessions",token,new{currentPassword="WrongPassword!"})).StatusCode==HttpStatusCode.BadRequest&&(await Send("GET","/api/profile",token)).IsSuccessStatusCode,"Yanlış onay şifresi oturumu düşürmez veya diğer oturumları kapatmaz");
 var oldExpiry=new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo;
 var revoke=await Json(await Send("POST","/api/profile/revoke-other-sessions",token,new{currentPassword="TestPass123!"}));var renewed=revoke.GetProperty("token").GetString()!;
 Check((await Send("GET","/api/profile",olderSession)).StatusCode==HttpStatusCode.Unauthorized&&(await Send("GET","/api/profile",token)).StatusCode==HttpStatusCode.Unauthorized&&(await Send("GET","/api/profile",renewed)).IsSuccessStatusCode,"Diğer oturumlar sunucuda geçersizleşir; yenilenen mevcut oturum çalışır");
 Check(new JwtSecurityTokenHandler().ReadJwtToken(renewed).ValidTo==oldExpiry&&(await Send("GET","/api/profile",otherToken)).IsSuccessStatusCode,"Oturum kapatma süreyi uzatmaz ve başka hesapları etkilemez");
 Check((await Send("POST","/api/profile/change-password",renewed,new{currentPassword="TestPass123!",newPassword="short",confirmPassword="short"})).StatusCode==HttpStatusCode.BadRequest,"Kısa şifre reddedilir");
 Check((await Send("POST","/api/profile/change-password",renewed,new{currentPassword="TestPass123!",newPassword="BetterPass456!",confirmPassword="Mismatch456!"})).StatusCode==HttpStatusCode.BadRequest,"Şifre tekrarı uyuşmazsa kayıt yapılmaz");
 await db.Users.Where(u=>u.Id==user.Id).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.PasswordResetTokenHash,"old-reset").SetProperty(x=>x.PasswordResetTokenExpiresAt,DateTime.UtcNow.AddHours(1)));
 var passwordResult=await Json(await Send("POST","/api/profile/change-password",renewed,new{currentPassword="TestPass123!",newPassword="BetterPass456!",confirmPassword="BetterPass456!"}));var passwordToken=passwordResult.GetProperty("token").GetString()!;
 Check((await Send("GET","/api/profile",renewed)).StatusCode==HttpStatusCode.Unauthorized&&(await Send("GET","/api/profile",passwordToken)).IsSuccessStatusCode,"Şifre değişikliği eski oturumları kapatıp mevcut oturumu yeniler");
 Check((await http.PostAsJsonAsync("/api/auth/login",new{email=user.Email,password="TestPass123!"})).StatusCode==HttpStatusCode.Unauthorized&&!string.IsNullOrEmpty(await Login(user,"BetterPass456!")),"Yeni şifreyle giriş yapılabilir; eski şifre çalışmaz");
 var saved=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==user.Id);
 Check(saved.PasswordResetTokenHash==null&&saved.PasswordResetTokenExpiresAt==null&&saved.PasswordHash!="BetterPass456!"&&new PasswordHasher<User>().VerifyHashedPassword(saved,saved.PasswordHash,"BetterPass456!")!=PasswordVerificationResult.Failed,"Şifre hashlenir ve eski sıfırlama bağlantıları geçersizleşir");
 var limitedToken=await Login(limited);
 for(var i=0;i<5;i++)Check((await Send("POST","/api/profile/revoke-other-sessions",limitedToken,new{currentPassword="WrongPassword!"})).StatusCode==HttpStatusCode.BadRequest,"Yanlış güvenlik şifresi reddedilir");
 Check((await Send("POST","/api/profile/revoke-other-sessions",limitedToken,new{currentPassword="WrongPassword!"})).StatusCode==HttpStatusCode.TooManyRequests,"Tekrarlanan güvenlik denemeleri sınırlandırılır");
 Console.WriteLine($"SONUÇ: {passed} profil/migration/yetki/oturum kontrolü başarılı.");
}finally{
 if(server is {HasExited:false}){server.Kill(true);await server.WaitForExitAsync();}server?.Dispose();SqlConnection.ClearAllPools();
 if(cs.InitialCatalog==database&&database.StartsWith("TravelaProfileTests_",StringComparison.Ordinal))await db.Database.EnsureDeletedAsync();
}
void Check(bool ok,string message){if(!ok)throw new Exception("BAŞARISIZ: "+message);passed++;Console.WriteLine("OK: "+message);}
static User NewUser(string name,string role="Passenger"){var u=new User{FullName="Yolcu "+name,Email=name+"@profile.example",Role=role};u.PasswordHash=new PasswordHasher<User>().HashPassword(u,"TestPass123!");return u;}
