using System.Diagnostics;
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
using Travela.Api.Reporting;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
Directory.SetCurrentDirectory(root);
using var source = new TravelaDbContextFactory().CreateDbContext([]);
if (args.Contains("--snapshot-existing") || args.Contains("--verify-existing"))
{
 var counts=new { Trips=await source.Trips.CountAsync(),Reservations=await source.Reservations.CountAsync(),Payments=await source.PaymentOrders.CountAsync() };
 var directory=Path.Combine(root,"tmp","report-qa");Directory.CreateDirectory(directory);var file=Path.Combine(directory,"live-before.json");
 if(args.Contains("--snapshot-existing")){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(counts));Console.WriteLine("Migration öncesi mevcut kayıt sayıları saklandı: "+JsonSerializer.Serialize(counts));return;}
 if((await source.Database.GetPendingMigrationsAsync()).Any())throw new Exception("Bekleyen migration var.");
 if(await File.ReadAllTextAsync(file)!=JsonSerializer.Serialize(counts))throw new Exception("Mevcut kayıt sayıları değişti.");
 var existingService=new CompanyReportingService(source,TimeProvider.System);
 foreach(var companyId in await source.Companies.AsNoTracking().Select(c=>c.Id).ToListAsync())foreach(var mode in new[]{"real","test"})
 {
  var report=await existingService.Get(companyId,new(){Environment=mode},default);
  Console.WriteLine($"Firma #{companyId}, {mode}: satış {report.Metrics.Sold}, tahsilat {report.Metrics.Collected}, iade {report.Metrics.Refunded}; SQL raporu başarılı.");
 }
 Console.WriteLine("Migration uygulandı ve mevcut kayıt sayıları korundu: "+JsonSerializer.Serialize(counts));return;
}
var database = "TravelaReportingTests_" + Guid.NewGuid().ToString("N");
var cs = new SqlConnectionStringBuilder(source.Database.GetConnectionString()) { InitialCatalog = database };
await using var db = new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(cs.ConnectionString).Options);
Process? server = null; var log = new StringBuilder(); var passed = 0;
try
{
 await db.Database.MigrateAsync();
 var instant = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
 var clock = new FixedClock(instant); var service = new CompanyReportingService(db, clock);
 var start = new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc); var end = start.AddDays(1);
 var a = new Company { Name = "Rapor Firma" }; var b = new Company { Name = "Diğer Firma" };
 var c1 = User("company", "Company"); var c2 = User("other", "Company"); var passenger = User("passenger"); var admin = User("admin", "Admin");
 var route = new Travela.Api.Models.Route { DepartureCity = "=FORMÜL Şehri", ArrivalCity = "İzmir Çeşme", EstimatedDurationMinutes = 120 };
 var t1 = Trip(a, route, 10, "34 TEST 1"); var t2 = Trip(a, route, 30, "34 TEST 2"); var foreign = Trip(b, new() { DepartureCity = "Gizli", ArrivalCity = "Şehir", EstimatedDurationMinutes = 60 }, 20, "34 TEST 3");
 db.AddRange(a,b,c1,c2,passenger,admin,t1,t2,foreign); await db.SaveChangesAsync(); c1.CompanyId=a.Id; c2.CompanyId=b.Id; await db.SaveChangesAsync();
 // Synthetic ledger records only in this isolated database: no provider calls or money.
 var active = Order(t1,1,100,"Paid",true,start);
 Order(t1,2,200,"Paid",true,start.AddHours(2),"Succeeded");
 Order(t1,3,300,"Paid",true,start.AddHours(3),"Pending");
 Order(t1,4,400,"Paid",true,start.AddHours(4),"Failed");
 var previous = Order(t1,5,500,"Paid",true,start.AddDays(-4),"Succeeded"); previous.RefundedAt=start.AddHours(10); previous.RefundStatusChangedAt=previous.RefundedAt; previous.Reservation.CancelledAt=start.AddHours(9);
 Order(t1,6,600,"Pending",true,start.AddHours(1));
 Order(t1,7,700,"Failed",true,start.AddHours(1));
 var expired = Order(t1,8,800,"Pending",true,start.AddHours(1)); expired.Reservation.HoldExpiresAt=instant.AddSeconds(-1);
 var legacy = new Reservation { Trip=t1,Passenger=passenger,SeatNumber=9,Price=500,CreatedAt=start,Kind="Legacy" }; db.Add(legacy);
 Order(t1,10,1000,"Paid",false,start.AddHours(1));
 Order(t2,1,110,"Paid",true,start.AddTicks(-1)); // excluded by Turkey start boundary
 Order(t2,2,120,"Paid",true,end); // excluded by end-exclusive boundary
 Order(t2,3,130,"Paid",true,end.AddTicks(-1)); // included by last tick of Turkey day
 var unknown=Order(t2,4,140,"Paid",true,start.AddDays(-5),"Pending"); unknown.RefundStatusChangedAt=null; unknown.RefundRequestedAt=null;
 Order(foreign,1,9999,"Paid",true,start);
 await db.SaveChangesAsync();
 // Cached inventory includes expired holds until the cleanup worker; reporting must not block them.
 foreach(var t in new[]{t1,t2,foreign}) t.AvailableSeats=t.Bus.SeatCapacity-await db.Reservations.CountAsync(r=>r.TripId==t.Id&&r.Status=="Active");
 for(var i=0;i<3;i++) db.PaymentNotifications.Add(new() {OrderId=active.Id,Provider="LocalTest",EventId="duplicate-event-"+i});
 await db.SaveChangesAsync();
 var q = Query(); var r=await service.Get(a.Id,q,default);
 Check(r.Metrics.Sold==6 && r.Metrics.Active==3 && r.Metrics.Cancelled==5,"Satış, aktif ve iptal ayrı; önceki dönem bileti iptale dahil");
 Check(r.Metrics.Collected==2130 && r.Metrics.Refunded==700 && r.Metrics.PendingRefund==300 && r.Metrics.FailedRefundCount==1 && r.Metrics.FailedRefundAmount==400 && r.Metrics.CollectedMinusRefunded==1430,"Tahsilat, tamamlanan/bekleyen/başarısız iade ve fark doğru");
 Check(r.TotalTrips==2 && r.Trips.All(t=>t.Id!=foreign.Id),"Başka firma tüm gruplardan dışlandı");
 Check(r.Occupancy.ActiveSoldSeats==5 && r.Occupancy.Capacity==40 && r.Occupancy.Percent==12.5m,"Farklı kapasitelerde ağırlıklı doluluk; basit ortalama değil");
 var row=r.Trips.Single(t=>t.Id==t1.Id);
 Check(row.ActiveSoldSeats==2&&row.HeldSeats==1&&row.LegacyBlockedSeats==1&&row.OtherEnvironmentBlocks==0&&row.ExpiredHolds==1&&row.AvailableForSale==6,"Ücretsiz ve diğer ortam blokajı ayrı; başarısız/sona ermiş tutma boş");
 Check(r.UnknownRefundDateCount==1&&r.UnknownRefundDateAmount==140,"Eski bilinmeyen iade tarihi uydurulmadı ve uyarıldı");
 Check(r.Daily.Count==1&&r.Daily[0].Sold==6&&r.Daily[0].Cancelled==5&&r.Daily[0].Collected==2130&&r.Daily[0].Refunded==700,"Grafik, kart ve Turkey gün sınırları aynı");
 Check(r.Routes.Sum(x=>x.Sold)==r.Metrics.Sold&&r.Trips.Sum(x=>x.Collected)==r.Metrics.Collected&&r.Routes.Sum(x=>x.Refunded)==r.Metrics.Refunded,"Kart, güzergâh ve sefer toplamları eşleşiyor");
 var real=Query(); real.Environment="real"; var realReport=await service.Get(a.Id,real,default);
 Check(realReport.Metrics.Sold==6&&realReport.Metrics.Collected==2130&&realReport.Metrics.Refunded==700,"Eski ortam parametresi firma kayıtlarını dışlamıyor");
 real.TripId=t2.Id; var zero=await service.Get(a.Id,real,default); Check(zero.Metrics.Sold==1&&zero.Metrics.Collected==130,"Firmanın kayıtları sağlayıcı türünden bağımsız raporlanır");
 q.To=new DateOnly(2026,11,3); var filled=await service.Get(a.Id,q,default); Check(filled.Daily.Count==3&&filled.Daily[2].Sold==0&&filled.Daily[2].Collected==0,"Verisiz günler sıfırla dolduruldu");
 q=Query(); q.Basis="departure"; q.From=q.To=new DateOnly(2026,11,10); var cohort=await service.Get(a.Id,q,default);
 Check(cohort.Metrics.Sold==10&&cohort.Metrics.PendingRefund==440&&cohort.Metrics.Collected==3000,"Sefer tarihi güncel kohortu tüm satış tarihlerini ve bilinmeyen iadeyi kapsıyor");
 Check(cohort.Trips.Sum(x=>x.Sold)==cohort.Metrics.Sold&&cohort.Daily.Sum(x=>x.Collected)==cohort.Metrics.Collected,"Sefer kapsamında grafik ve tablo toplamları tutarlı");
 q=Query(); q.PageSize=1; var paged=await service.Get(a.Id,q,default); Check(paged.Trips.Count==1&&paged.TotalTrips==2&&paged.Metrics.Sold==6,"Sayfalama metrikleri kesmiyor");
 q.Page=2; var second=await service.Get(a.Id,q,default); Check(second.Trips.Count==1&&second.Trips[0].Id!=paged.Trips[0].Id,"Backend sayfalama deterministik");
 foreach(var sort in new[]{"departure","sold","collected","refunded","occupancy","available"}) {q.Sort=sort; q.Direction="desc"; await service.Get(a.Id,q,default);} Check(true,"İzinli sıralamalar SQL üzerinde çalışıyor");
 var bytes=await service.Csv(a.Id,q,default); var csv=Encoding.UTF8.GetString(bytes);
 Check(bytes.AsSpan(0,3).SequenceEqual(new byte[]{239,187,191})&&!csv.Contains("YEREL TEST")&&csv.Contains("'=FORMÜL Şehri"),"CSV BOM, firma kapsamı ve formül koruması");
 var csvRows=csv.Split('\n').Where(x=>x.StartsWith("\"Firma işlemleri\"")&&x.Contains("\"34 TEST")).ToArray();
 Check(csvRows.Length==2&&csvRows.Any(x=>x.Contains("\""+t1.Id+"\""))&&csvRows.Any(x=>x.Contains("\""+t2.Id+"\"")),"CSV yalnızca sayfayı değil tüm filtreli sonuçları içeriyor");
 var csvCells=csvRows.Select(line=>line.TrimEnd('\r').Split(';').Select(cell=>cell.Trim('"')).ToArray()).ToArray();
 var tr=System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
 Check(csvCells.Sum(c=>int.Parse(c[11]))==r.Metrics.Sold&&csvCells.Sum(c=>decimal.Parse(c[14],tr))==r.Metrics.Collected&&csvCells.Sum(c=>decimal.Parse(c[15],tr))==r.Metrics.Refunded,"CSV satış/tahsilat/iade toplamları kart ve grafikle aynı");
 Check(csv.Contains("durum tarihi bilinmiyor"),"CSV eksik tarihi olan eski iadeyi açıklar");
 foreach(var text in new[]{"=1+1"," +SUM(A1)","-FORMULA","@test","\tX","\rX","\nX"})Check(CompanyReportingService.SafeText(text).StartsWith("'"),"CSV metin formülü engellendi");
 Check(!csv.Contains(passenger.FullName)&&!csv.Contains(passenger.Email),"CSV gereksiz yolcu kişisel verisi içermiyor");
 var scope=service.Scope(new(){Period="month"}); Check(scope.StartUtc==new DateTime(2026,10,31,21,0,0,DateTimeKind.Utc)&&scope.EndExclusiveUtc==new DateTime(2026,11,2,21,0,0,DateTimeKind.Utc),"Türkiye ay başlangıcı UTC'ye doğru dönüştürüldü");
 var midnight=new CompanyReportingService(db,new FixedClock(new DateTime(2026,10,31,21,0,0,DateTimeKind.Utc))).Scope(new(){Period="today"}); Check(midnight.From==new DateOnly(2026,11,1),"UTC önceki günken Türkiye tarihi yeni ay");
 var week=service.Scope(new(){Period="week"}); Check(week.From==new DateOnly(2026,10,27)&&week.To==new DateOnly(2026,11,2),"Son 7 Gün bugün dahil");
 foreach(var bad in new[]{new CompanyReportQuery{Period="custom",From=new(2026,11,2),To=new(2026,11,1)},new(){Period="custom",From=new(2025,1,1),To=new(2026,11,1)},new(){Sort="SQL"},new(){Page=0},new(){Environment="auto"}}) { try{service.Scope(bad);throw new Exception("Filtre kabul edildi");}catch(ReportQueryException){Check(true,"Geçersiz filtre reddedildi");} }
 // Unsafe inventory must never yield invented free seats or a misleading occupancy percentage.
 await db.Trips.Where(t=>t.Id==t1.Id).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.AvailableSeats,0));
 var invalid=await service.Get(a.Id,Query(),default); Check(invalid.Occupancy.Percent==null&&invalid.Occupancy.InvalidTrips==1&&invalid.Trips.Single(t=>t.Id==t1.Id).AvailableForSale==null,"Tutarsız kapasite/kayıt için açıklayıcı güvenli sonuç");
 await db.Trips.Where(t=>t.Id==t1.Id).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.AvailableSeats,t1.AvailableSeats));
 // Exact boundaries use a fixed clock against this isolated SQL database.
 var lifeCases=new[] {
  (Status:"Scheduled", Departure:instant.AddMinutes(1), Arrival:instant.AddHours(1), Expected:"Scheduled"),
  (Status:"Planlandi", Departure:instant, Arrival:instant.AddHours(1), Expected:"InProgress"),
  (Status:"Scheduled", Departure:instant.AddHours(-1), Arrival:instant, Expected:"Completed"),
  (Status:"Scheduled", Departure:instant.AddDays(-2), Arrival:instant.AddDays(-1), Expected:"Completed"),
  (Status:"Devam Ediyor", Departure:instant.AddHours(-1), Arrival:instant.AddMinutes(1), Expected:"InProgress"),
  (Status:"Cancelled", Departure:instant.AddDays(-2), Arrival:instant.AddDays(-1), Expected:"Cancelled"),
  (Status:"İptal Edildi", Departure:instant.AddHours(-1), Arrival:instant.AddHours(1), Expected:"İptal Edildi"),
  (Status:"Completed", Departure:instant.AddHours(1), Arrival:instant.AddHours(2), Expected:"Scheduled")
 };
 var lifeRows=lifeCases.Select(x=>new Trip{CompanyId=a.Id,BusId=t2.BusId,RouteId=route.Id,DepartureTime=x.Departure,ArrivalTime=x.Arrival,Status=x.Status,Price=321,AvailableSeats=27,CancelledAt=x.Expected is "Cancelled" or "İptal Edildi"?instant.AddDays(-3):null,CancellationReason=x.Expected is "Cancelled" or "İptal Edildi"?"Firma iptali":null}).ToArray();
 db.Trips.AddRange(lifeRows);await db.SaveChangesAsync();
 var lifecycle=new Travela.Api.Trips.TripLifecycleService(db,clock);
 await lifecycle.Synchronize();
 var lifeIds=lifeRows.Select(x=>x.Id).ToArray();var actualLife=await db.Trips.AsNoTracking().Where(t=>lifeIds.Contains(t.Id)).OrderBy(t=>t.Id).ToListAsync();
 for(var i=0;i<lifeCases.Length;i++)Check(actualLife[i].Status==lifeCases[i].Expected,$"Otomatik durum sınırı: {lifeCases[i].Status} → {lifeCases[i].Expected}");
 Check(actualLife.All(t=>t.AvailableSeats==27&&t.Price==321),"Otomatik durum güncellemesi koltuk ve fiyatı değiştirmedi");
 Check(actualLife.Where(t=>t.Status is "Cancelled" or "İptal Edildi").All(t=>t.CancellationReason=="Firma iptali"&&t.CancelledAt==instant.AddDays(-3)&&t.UpdatedAt==null),"İptal edilmiş seferlerin durumu ve geçmişi korundu");
 Check(await lifecycle.Synchronize()==0,"Aynı saatle yinelenen eşitleme kayıtları tekrar değiştirmez");
 await db.Trips.Where(t=>lifeIds.Contains(t.Id)).ExecuteDeleteAsync();
 var backgroundTrip=new Trip{CompanyId=a.Id,BusId=t2.BusId,RouteId=route.Id,DepartureTime=DateTime.UtcNow.AddHours(-2),ArrivalTime=DateTime.UtcNow.AddHours(-1),Status="Scheduled",Price=100,AvailableSeats=30};
 db.Trips.Add(backgroundTrip);await db.SaveChangesAsync();

 var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
 var config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
 var startProcess=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.Combine(root,"backend"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 startProcess.ArgumentList.Add(Path.Combine(root,"backend","bin",config,"net10.0","Travela.API.dll"));
 startProcess.Environment["ASPNETCORE_URLS"]=$"http://127.0.0.1:{port}"; startProcess.Environment["ASPNETCORE_ENVIRONMENT"]="Testing";
 startProcess.Environment["ConnectionStrings__DefaultConnection"]=cs.ConnectionString; startProcess.Environment["Jwt__Key"]=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
 startProcess.Environment["Jwt__Issuer"]="ReportTests";startProcess.Environment["Jwt__Audience"]="ReportTests";startProcess.Environment["Payments__RecoveryEnabled"]="false";startProcess.Environment["Notifications__WorkerEnabled"]="false";startProcess.Environment["Logging__LogLevel__Default"]="Warning";
 server=Process.Start(startProcess)!;server.OutputDataReceived+=(_,e)=>{lock(log)log.AppendLine(e.Data);};server.ErrorDataReceived+=(_,e)=>{lock(log)log.AppendLine(e.Data);};server.BeginOutputReadLine();server.BeginErrorReadLine();
 using var http=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(45)};
 for(var i=0;i<80;i++){try{if((await http.GetAsync("/api/test")).IsSuccessStatusCode)break;}catch(HttpRequestException){} if(server.HasExited)throw new Exception(log.ToString());await Task.Delay(150);}
 // Read directly from SQL after startup: no MVC trip read has run yet.
 for(var retry=0;retry<50&&!await db.Trips.AsNoTracking().AnyAsync(t=>t.Id==backgroundTrip.Id&&t.Status=="Completed");retry++)await Task.Delay(100);
 Check(await db.Trips.AsNoTracking().AnyAsync(t=>t.Id==backgroundTrip.Id&&t.Status=="Completed"),"Arka plan görevi açılışta geçmiş seferi panel isteği olmadan tamamlar");
 await db.Trips.Where(t=>t.Id==backgroundTrip.Id).ExecuteDeleteAsync();

 var token=await Login(c1);var other=await Login(c2);var passengerToken=await Login(passenger);var adminToken=await Login(admin);
 var lifecycleRole=await db.Roles.FirstOrDefaultAsync(r=>r.Name=="Company")??new Role{Name="Company"};
 foreach(var permissionName in new[]{"trip.view","trip.update"})db.RolePermissions.Add(new RolePermission{Role=lifecycleRole,Permission=new Permission{Name=permissionName}});
 await db.SaveChangesAsync();
 var readTrip=new Trip{CompanyId=a.Id,BusId=t2.BusId,RouteId=route.Id,DepartureTime=DateTime.UtcNow.AddMinutes(-20),ArrivalTime=DateTime.UtcNow.AddMinutes(20),Status="Scheduled",Price=100,AvailableSeats=30};
 db.Trips.Add(readTrip);await db.SaveChangesAsync();
 var readResult=await Send("/api/trips/"+readTrip.Id,token);readResult.EnsureSuccessStatusCode();
 var readJson=await readResult.Content.ReadFromJsonAsync<JsonElement>();
 Check(readJson.GetProperty("status").GetString()=="InProgress","Sefer detayı okunurken kalkış saati geçmiş kayıt otomatik başlar");
 var activeList=await(await Send("/api/trips?status=InProgress&pageSize=100",token)).Content.ReadFromJsonAsync<JsonElement>();
 Check(activeList.GetProperty("items").EnumerateArray().Any(t=>t.GetProperty("id").GetInt32()==readTrip.Id),"Devam eden sefer durum filtresinde görünür");
 var activeSummary=await(await Send("/api/trips/summary",token)).Content.ReadFromJsonAsync<TripSummaryDto>();
 Check(activeSummary!.InProgressTrips==await db.Trips.CountAsync(t=>t.CompanyId==a.Id&&t.Status=="InProgress"),"Devam eden sefer özeti doğru sayılır");
 using(var manual=new HttpRequestMessage(HttpMethod.Patch,$"/api/trips/{readTrip.Id}/status"){Content=JsonContent.Create(new{status="Completed"})}){
  manual.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  Check((await http.SendAsync(manual)).StatusCode==HttpStatusCode.Gone,"Manuel durum endpoint'i 410 ile kapalıdır");
 }
 await db.Trips.Where(t=>t.Id==readTrip.Id).ExecuteUpdateAsync(u=>u.SetProperty(t=>t.ArrivalTime,DateTime.UtcNow.AddMinutes(-1)));
 var completedList=await Send("/api/trips?status=Completed&pageSize=100",token);completedList.EnsureSuccessStatusCode();
 var completedJson=await completedList.Content.ReadFromJsonAsync<JsonElement>();
 Check(completedJson.GetProperty("items").EnumerateArray().Any(t=>t.GetProperty("id").GetInt32()==readTrip.Id&&t.GetProperty("status").GetString()=="Completed"),"Varış saati geçen sefer tamamlandı filtresinde anında görünür");
 var lifeSummary=await(await Send("/api/trips/summary",token)).Content.ReadFromJsonAsync<TripSummaryDto>();
 Check(lifeSummary!.CompletedTrips==await db.Trips.CountAsync(t=>t.CompanyId==a.Id&&t.Status=="Completed"),"Sefer özeti ve liste aynı otomatik durumları kullanır");
 await db.Trips.Where(t=>t.Id==readTrip.Id).ExecuteDeleteAsync();

 const string url="/api/company/reports?period=custom&from=2026-11-01&to=2026-11-01&environment=test&pageSize=1";
 var response=await Send(url,token); if(!response.IsSuccessStatusCode)throw new Exception(await response.Content.ReadAsStringAsync()+log);
 var apiReport=(await response.Content.ReadFromJsonAsync<CompanyReportResponse>())!;Check(apiReport.Metrics.Sold==6&&apiReport.TotalTrips==2&&apiReport.Trips.Count==1,"HTTP raporu SQL servisiyle aynı sonuç");
 var overview=(await (await Send(url.Replace("reports?","reports/overview?"),token)).Content.ReadFromJsonAsync<CompanyReportResponse>())!;
 Check(JsonSerializer.Serialize(overview.Metrics)==JsonSerializer.Serialize(apiReport.Metrics),"Genel Bakış ve Raporlar aynı metrikler");
 foreach(var endpoint in new[]{url,url.Replace("reports?","reports/csv?")}){
  Check((await Send(endpoint,null)).StatusCode==HttpStatusCode.Unauthorized,"Girişsiz rapor/CSV 401");
  Check((await Send(endpoint,passengerToken)).StatusCode==HttpStatusCode.Forbidden,"Yolcu rapor/CSV 403");
  Check((await Send(endpoint,adminToken)).StatusCode==HttpStatusCode.Forbidden,"Firma raporu admin akışına taşınmadı");
  Check((await Send(endpoint+"&tripId="+foreign.Id,token)).StatusCode==HttpStatusCode.NotFound,"Başka firma sefer filtresi rapor/CSV 404");
  Check((await Send(endpoint+"&routeId="+foreign.RouteId,token)).StatusCode==HttpStatusCode.NotFound,"Başka firma güzergâh filtresi rapor/CSV 404");
 }
 var attacker=(await(await Send(url+"&companyId="+a.Id,other)).Content.ReadFromJsonAsync<CompanyReportResponse>())!; Check(attacker.Metrics.Collected==9999&&attacker.Trips.All(t=>t.Id==foreign.Id),"İstemci companyId yok sayıldı; doğrulanmış hesaptan firma");
 var badResponse=await Send(url+"&sort=evil",token);Check(badResponse.StatusCode==HttpStatusCode.BadRequest,"HTTP hatalı sıralama 400");
 var exportResponse=await Send(url.Replace("reports?","reports/csv?"),token);Check(exportResponse.IsSuccessStatusCode&&exportResponse.Content.Headers.ContentDisposition!.FileNameStar!.StartsWith("Travela-sefer-raporu-")&&exportResponse.Headers.CacheControl?.NoStore==true,"CSV standart dosya adı ve özel no-store yanıt");
 Check((await Send("/api/reservations/company?orderId="+active.Id,other)).StatusCode==HttpStatusCode.NotFound,"Başarısız iade bağlantısı başka firmaya veri açmıyor");
 Check((await Send(url.Replace("from=2026-11-01", "from=2026-11-04"),token)).StatusCode==HttpStatusCode.BadRequest,"HTTP geçersiz tarih aralığı 400");
 // Exercise additive migration on older persisted tickets without touching the application's database.
 await db.GetService<IMigrator>().MigrateAsync("20261102110356_HardenPaymentInvariants");
 await db.Database.MigrateAsync();
 var backfill=await db.Reservations.AsNoTracking().SingleAsync(x=>x.Id==active.ReservationId);
 var refundBackfill=await db.PaymentOrders.AsNoTracking().SingleAsync(x=>x.Id==previous.Id);
 var unknownBackfill=await db.PaymentOrders.AsNoTracking().SingleAsync(x=>x.Id==unknown.Id);
 Check(backfill.Pnr==active.Reservation.Pnr&&backfill.IssuedAt==active.PaidAt&&backfill.CreatedAt==active.Reservation.CreatedAt,"Migration mevcut PNR ve geçmişi korudu; doğrulanma tarihinden düzenlenme tarihi geri dolduruldu");
 Check(refundBackfill.RefundStatusChangedAt==refundBackfill.RefundedAt&&unknownBackfill.RefundStatusChangedAt==null&&unknownBackfill.RefundRequestedAt==null,"Migration tamamlanan iade tarihini korudu; bilinmeyen geçmişi uydurmadı");
 // New operations are exercised only in this disposable database.
 var createPermission = await db.Permissions.FirstOrDefaultAsync(p=>p.Name=="trip.create") ?? new Permission { Name="trip.create" };
 var companyRole = await db.Roles.FirstOrDefaultAsync(role=>role.Name=="Company") ?? new Role { Name="Company" };
 db.RolePermissions.Add(new RolePermission { Role=companyRole, Permission=createPermission }); await db.SaveChangesAsync();
 var operationStart = DateTime.UtcNow.AddDays(100).Date.AddHours(10);
 CreateTripDto NewTrip(DateTime at, int? busId=null) => new() { BusId=busId??t1.BusId, RouteId=route.Id, DepartureTime=at, ArrivalTime=at.AddHours(2), Price=222.22m };
 async Task<HttpResponseMessage> Batch(string? auth, params CreateTripDto?[] trips) {
  using var message=new HttpRequestMessage(HttpMethod.Post,"/api/trips/batch") { Content=JsonContent.Create(new {trips}) };
  if(auth!=null)message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",auth);
  return await http.SendAsync(message);
 }
 var beforeBatch=await db.Trips.CountAsync();
 Check((await Batch(null,NewTrip(operationStart))).StatusCode==HttpStatusCode.Unauthorized,"Toplu sefer girişsiz oluşturulamaz");
 Check((await Batch(passengerToken,NewTrip(operationStart))).StatusCode==HttpStatusCode.Forbidden,"Yolcu toplu sefer oluşturamaz");
 Check((await Batch(token)).StatusCode==HttpStatusCode.BadRequest,"Boş toplu sefer reddedildi");
 Check((await Batch(token,Enumerable.Repeat<CreateTripDto?>(NewTrip(operationStart),101).ToArray())).StatusCode==HttpStatusCode.BadRequest,"100 üzeri toplu sefer reddedildi");
 Check((await Batch(token,new CreateTripDto?[]{null})).StatusCode==HttpStatusCode.BadRequest,"Null sefer kalemi reddedildi");
 Check((await Batch(token,NewTrip(operationStart),NewTrip(operationStart.AddHours(1)))).StatusCode==HttpStatusCode.Conflict&&await db.Trips.CountAsync()==beforeBatch,"Toplu işlemin kendi içindeki çakışma tüm kaydı engeller");
 Check((await Batch(token,NewTrip(operationStart),NewTrip(operationStart.AddDays(1),foreign.BusId))).StatusCode==HttpStatusCode.BadRequest&&await db.Trips.CountAsync()==beforeBatch,"Başka firmanın otobüsü reddedilir; kısmi kayıt yok");
 var forged=NewTrip(operationStart);forged.CompanyId=b.Id;
 Check((await Batch(token,forged)).StatusCode==HttpStatusCode.Forbidden,"Firma kimliği değiştirilerek toplu sefer oluşturulamaz");
 Check((await Batch(token,NewTrip(DateTime.UtcNow.AddDays(-1)))).StatusCode==HttpStatusCode.BadRequest,"Toplu işlemde geçmiş tarih engellendi");
 var batchResponse=await Batch(token,NewTrip(operationStart),NewTrip(operationStart.AddDays(1)));
 if(!batchResponse.IsSuccessStatusCode)throw new Exception(await batchResponse.Content.ReadAsStringAsync());
 var batchJson=await batchResponse.Content.ReadFromJsonAsync<JsonElement>();var batchIds=batchJson.GetProperty("tripIds").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
 var saved=await db.Trips.AsNoTracking().Where(t=>batchIds.Contains(t.Id)).ToListAsync();
 Check(batchResponse.StatusCode==HttpStatusCode.Created&&saved.Count==2&&saved.All(t=>t.CompanyId==a.Id&&t.Status=="Scheduled"&&t.Price==222.22m&&t.AvailableSeats==t1.Bus.SeatCapacity),"Toplu seferler doğru firma, fiyat, durum ve tam kapasiteyle oluşturuldu");
 Check(!await db.Reservations.AnyAsync(x=>batchIds.Contains(x.TripId)),"Sefer oluştururken bilet ve yolcu kopyalanmadı");
 var countAfter=await db.Trips.CountAsync();
 Check((await Batch(token,NewTrip(operationStart.AddDays(2)),NewTrip(operationStart))).StatusCode==HttpStatusCode.Conflict&&await db.Trips.CountAsync()==countAfter,"Mevcut seferle çakışma varsa toplu işlemin tamamı geri alınır");
 Check((await Batch(token,NewTrip(operationStart.AddHours(2)))).StatusCode==HttpStatusCode.Created,"Bitiş saatinde başlayan yeni sefer çakışmaz");
 var races=await Task.WhenAll(Batch(token,NewTrip(operationStart.AddDays(3))),Batch(token,NewTrip(operationStart.AddDays(3))));
 Check(races.Count(x=>x.StatusCode==HttpStatusCode.Created)==1&&races.Count(x=>x.StatusCode==HttpStatusCode.Conflict)==1,"Eşzamanlı aynı otobüs/saat isteğinde tek sefer oluşturuldu");
 var nearTrip=new Trip {CompanyId=a.Id,BusId=t2.BusId,RouteId=route.Id,DepartureTime=DateTime.UtcNow.AddHours(3),ArrivalTime=DateTime.UtcNow.AddHours(5),Price=100,AvailableSeats=30};
 var overdueTrip=new Trip {CompanyId=a.Id,BusId=t2.BusId,RouteId=route.Id,DepartureTime=DateTime.UtcNow.AddHours(-3),ArrivalTime=DateTime.UtcNow.AddHours(-1),Price=100,AvailableSeats=30};
 var inactiveBus=new Bus {CompanyId=a.Id,PlateNumber="34 OPS 1",NormalizedPlateNumber="34OPS1",SeatCapacity=20,IsActive=false};
 var unavailableTrip=new Trip {CompanyId=a.Id,Bus=inactiveBus,RouteId=route.Id,DepartureTime=DateTime.UtcNow.AddHours(6),ArrivalTime=DateTime.UtcNow.AddHours(8),Price=100,AvailableSeats=20};
 db.AddRange(nearTrip,overdueTrip,unavailableTrip);await db.SaveChangesAsync();
 const string attentionUrl="/api/company/attention";
 Check((await Send(attentionUrl,null)).StatusCode==HttpStatusCode.Unauthorized&&(await Send(attentionUrl,passengerToken)).StatusCode==HttpStatusCode.Forbidden,"Güncel durumlar giriş ve firma rolü gerektirir");
 var attentionResponse=await Send(attentionUrl+"?companyId="+b.Id+"&from=2099-01-01",token);
 var attention=await attentionResponse.Content.ReadFromJsonAsync<Travela.Api.Controllers.CompanyAttentionResponse>();
 var group=attention!.Groups.ToDictionary(g=>g.Id);
 Check(group["departures"].Items.Any(x=>x.Id==nearTrip.Id.ToString())&&!group.ContainsKey("status")&&await db.Trips.AsNoTracking().AnyAsync(t=>t.Id==overdueTrip.Id&&t.Status=="Completed")&&group["resources"].Items.Any(x=>x.Id==unavailableTrip.Id.ToString()),"Uyarılar güncel kaynak sorunlarını kapsar; geçmiş sefer tamamlanır ve manuel durum uyarısı gösterilmez");
 Check(group["refunds"].Count==3&&group["refunds"].Items.All(x=>x.Href.StartsWith("/company/reservations?orderId=")),"Bekleyen/başarısız iadeler tarih filtresinden bağımsız ve doğrudan işlem bağlantılıdır");
 Check(attentionResponse.Headers.CacheControl?.NoStore==true&&group.Values.SelectMany(g=>g.Items).All(x=>x.Id!=foreign.Id.ToString()),"Güncel durumlar önbelleğe alınmaz; başka firmaya ait seferler görünmez");
 var otherAttention=await(await Send(attentionUrl,other)).Content.ReadFromJsonAsync<Travela.Api.Controllers.CompanyAttentionResponse>();
 Check(otherAttention!.Groups.All(g=>g.Count==0),"Başka firma işlem uyarılarını göremez");

 await db.Users.Where(u=>u.Id==c1.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.IsActive,false));
 Check((await Send(url,token)).StatusCode==HttpStatusCode.Unauthorized&&(await Send(url.Replace("reports?","reports/csv?"),token)).StatusCode==HttpStatusCode.Unauthorized,"Eski token pasif firma kullanıcısına rapor/CSV yetkisi vermiyor (401)");
 var output=Path.Combine(root,"tmp","report-qa");Directory.CreateDirectory(output);
 await File.WriteAllTextAsync(Path.Combine(output,"test.json"),JsonSerializer.Serialize(apiReport,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 await File.WriteAllTextAsync(Path.Combine(output,"real.json"),JsonSerializer.Serialize(realReport,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 await File.WriteAllBytesAsync(Path.Combine(output,"test.csv"),await exportResponse.Content.ReadAsByteArrayAsync());
 // Bound the full export explicitly, never silently truncate.
 var extra=new List<Trip>();for(var i=0;i<CompanyReportingService.CsvLimit;i++)extra.Add(new(){CompanyId=a.Id,BusId=t1.BusId,RouteId=route.Id,DepartureTime=t1.DepartureTime,ArrivalTime=t1.ArrivalTime,Price=500,AvailableSeats=10});
 db.Trips.AddRange(extra);await db.SaveChangesAsync();var limit=Query();limit.Basis="departure";limit.From=limit.To=new(2026,11,10);
 try{await service.Csv(a.Id,limit,default);throw new Exception("CSV sınırı uygulanmadı");}catch(ReportQueryException ex){Check(ex.Status==422,"10000 üzeri CSV açık hata; sonuçlar kesilmedi");}
 Console.WriteLine($"SONUÇ: {passed} raporlama SQL/HTTP kontrolü başarılı.");

 Trip Trip(Company company,Travela.Api.Models.Route r,int capacity,string plate)=>new(){Company=company,Bus=new(){Company=company,SeatCapacity=capacity,PlateNumber=plate,NormalizedPlateNumber=plate.Replace(" ","")},Route=r,DepartureTime=new(2026,11,10,9,0,0,DateTimeKind.Utc),ArrivalTime=new(2026,11,10,11,0,0,DateTimeKind.Utc),Price=500,AvailableSeats=capacity};
 PaymentOrder Order(Trip t,int seat,decimal amount,string status,bool test,DateTime paid,string refund="None"){
  var cancelled=refund!="None"||status=="Failed";var ticket=status=="Paid";
  var reservation=new Reservation{Trip=t,Passenger=passenger,SeatNumber=seat,Price=amount,CreatedAt=paid.AddMinutes(-1),Kind=ticket?"Ticket":"Hold",Pnr=ticket?Guid.NewGuid().ToString("N"):null,IssuedAt=ticket?paid:null,HoldExpiresAt=ticket?null:instant.AddMinutes(5),Status=cancelled?"Cancelled":"Active",CancelledAt=cancelled?start.AddHours(5):null};
  var order=new PaymentOrder{Reservation=reservation,PassengerId=passenger.Id,RequestKey=Guid.NewGuid(),Amount=amount,PaymentStatus=status,IsTest=test,Provider=test?"LocalTest":"FixtureVerified",CreatedAt=paid.AddMinutes(-1),PaidAt=ticket?paid:null,ProviderReference=ticket?Guid.NewGuid().ToString("N"):null,RefundStatus=refund,RefundAmount=refund!="None"?amount:0,RefundRequestedAt=refund!="None"?start.AddHours(5):null,RefundStatusChangedAt=refund!="None"?start.AddHours(6):null,RefundedAt=refund=="Succeeded"?start.AddHours(6):null,RefundReference=refund=="Succeeded"?Guid.NewGuid().ToString("N"):null,LastError=refund=="Failed"?"Sentetik test iadesi başarısız":null};db.Add(order);return order;
 }
 async Task<string> Login(User user){var result=await http.PostAsJsonAsync("/api/auth/login",new{email=user.Email,password="TestPass123!"});result.EnsureSuccessStatusCode();return(await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;}
 async Task<HttpResponseMessage> Send(string path,string? auth){using var request=new HttpRequestMessage(HttpMethod.Get,path);if(auth!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",auth);return await http.SendAsync(request);}
}
finally{
 if(server is {HasExited:false}){server.Kill(true);await server.WaitForExitAsync();}server?.Dispose();SqlConnection.ClearAllPools();
 if(cs.InitialCatalog==database&&database.StartsWith("TravelaReportingTests_",StringComparison.Ordinal))await db.Database.EnsureDeletedAsync();
}
void Check(bool ok,string message){if(!ok)throw new Exception("BAŞARISIZ: "+message);passed++;Console.WriteLine("OK: "+message);}
static User User(string name,string role="Passenger"){var user=new User{FullName="Yolcu "+name,Email=name+"@report.travela.test",Role=role};user.PasswordHash=new PasswordHasher<User>().HashPassword(user,"TestPass123!");return user;}
static CompanyReportQuery Query()=>new(){Period="custom",From=new(2026,11,1),To=new(2026,11,1),Environment="test"};
sealed class FixedClock(DateTime utc):TimeProvider{public override DateTimeOffset GetUtcNow()=>new(DateTime.SpecifyKind(utc,DateTimeKind.Utc));}

