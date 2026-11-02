# Koltuk seçimi ve rezervasyon

Sefer detayındaki (`/trips/:id`) koltuk planı otobüs kapasitesinden üretilir. Dolu koltuklar seçilemez. Passenger hesabı ile seçilen koltuk rezerve edilir; oturum yoksa girişten sonra aynı sefere dönülür. Rezervasyonlar `/reservations` adresinde ve üst menüdeki **Rezervasyonlarım** bağlantısında görüntülenir.

Bu akış ödeme almaz ve ödenmiş bilet oluşturmaz. Yolcu, kalkış zamanı gelmemiş aktif rezervasyonunu onay penceresinden iptal edebilir. İptal edilen kayıt numarası, zamanı ve nedeniyle korunur; koltuk yeniden alınabilir. Rezervasyonlarda süre dolma işlemi yoktur. Firma seferi iptal ederse ilgili aktif rezervasyonlar aynı transaction içinde iptal edilir ve seferin iptal nedeni yolcuya gösterilir.

## Çalıştırma

Mevcut SQL Server bağlantısı ile JWT ayarlarını koruyun. Çalışan backend'i durdurup proje kökünde migration'ı uygulayın ve API'yi yeniden başlatın:

```powershell
dotnet ef database update --project backend
dotnet run --project backend --launch-profile http
```

Başka bir terminalde:

```powershell
cd frontend
npm.cmd ci
npm.cmd run dev
```

Frontend varsayılan olarak `http://localhost:5173`, API `http://localhost:5062` adresini kullanır. JWT anahtarı, issuer ve audience mevcut geliştirme yapılandırmasında veya ortam değişkenlerinde (`Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`) tanımlı olmalıdır. Migration araçları için JWT gerekmez.

## Doğrulama

Çalışan API'nin dosyalarını kilitlememek için ayrı bir derleme yapılandırması kullanılabilir:

```powershell
dotnet build backend -c ReservationVerification --no-restore
dotnet run --project tests/Travela.ReservationTests -c ReservationVerification
cd frontend
npm.cmd run build
npm.cmd run lint
npm.cmd test
```

Test çalıştırıcısı gerçek SQL Server ve gerçek HTTP istekleri kullanır. Yapılandırılmış SQL Server'da `TravelaReservationTests_<rastgele kimlik>` adında ayrı bir veritabanı açar, migration'ları uygular ve işlem sonunda yalnızca kendi test veritabanını siler. SQL hesabının test veritabanı oluşturma/silme yetkisi gerekir; uygulama veritabanına test kaydı eklenmez. Ek bir test paketi kurulumu gerekmez. Başarısız kontrol sıfırdan farklı çıkış kodu üretir.

Kontroller: kimlik doğrulama ve Passenger rolü, kullanıcıya özel listeleme, sunucudan alınan fiyat ve yolcu kimliği, koltuk sınırları, iptal/geçmiş sefer, dolu koltuk, aynı koltuğa 12 eş zamanlı istek, farklı koltuklara eş zamanlı istek, son koltuk, doğrudan SQL benzersizlik ihlali, pasif oturum, rezervasyonlu seferin düzenlenmesi ve sefer iptalinin rezervasyonlara yansıması.

## Veri bütünlüğü

`Reservations(TripId, SeatNumber)` benzersiz indeksi yalnızca `Status = 'Active'` kayıtları kapsar. İptal geçmişi aynı koltukta korunabilir; birden fazla aktif kayıt veritabanında engellenir. Oluşturma, yolcu iptali ve sefer iptali önce aynı sefer satırını `UPDLOCK, HOLDLOCK` ile kilitler. Rezervasyon durumu ve `AvailableSeats` aynı transaction'da değiştirilir. Tekrar iptal idempotenttir ve kontenjanı ikinci kez artırmaz. Çakışma `409` döndürür. Yolcu ve fiyat POST gövdesinden alınmaz; kimlik oturumdan, fiyat seferden belirlenir.

Rezervasyonu bulunan seferin bilgileri ve seferlere bağlı otobüsün kapasitesi değiştirilemez. Böylece koltuk numaraları ile kontenjanın tutarlılığı korunur. Mevcut eski bir seferde boş koltuk sayısı, kapasite ve rezervasyon kayıtlarıyla eşleşmiyorsa koltuk planı ve rezervasyon kapatılır; bilinmeyen koltuklar için kayıt uydurulmaz veya mevcut kontenjan otomatik olarak sıfırlanmaz. Bu kayıtların firma tarafından doğrulanması gerekir.

`20261102075307_AddSeatReservations` migration'ı 2 Kasım 2026'da yerel uygulamanın kullandığı `TravelaDB` veritabanına uygulandı. Öncesinde `/api/trips/public/:id`, SQL Server 208 (`Invalid object name 'Reservations'`) hatasıyla HTTP 500 dönüyordu. Migration mevcut kayıtları değiştirmeden rezervasyon tablosunu, ilişkileri ve indeksleri ekledi. Yeni kurulumlarda yukarıdaki `database update` adımı yine gereklidir.

Çalışan API ve Vite üzerinden mevcut seferleri, koltuk verilerini ve bekleyen migration olmadığını kayıt değiştirmeden doğrulamak için:

```powershell
dotnet run --project tests/Travela.ReservationTests -c ReservationVerification -- --verify-existing
```

Olmayan sefer detay isteği `404` döner. Geçmiş veya iptal edilmiş mevcut seferin detayı `200` ile görüntülenir, `canReserve=false` olur; rezervasyon POST isteği `409` döner. Sunucu/veritabanı hataları `500` sınıfındadır ve bu durumlardan ayrıdır.

## İptal ve firma yönetimi

`20261102101401_AddReservationCancellation` migration'ı yerel `TravelaDB` üzerine uygulanmıştır. Aynı `Reservations` tablosuna `Status` (`Active` / `Cancelled`), `CancelledAt` ve `CancellationReason` eklendi. Eski aktif kayıtlar korunur. Önceden iptal edilmiş seferlerdeki rezervasyonlar iptal durumuna geçirilir ve kontenjanları serbest bırakılır. İptal geçmişi bulunan veritabanında veri kaybına neden olacak geri migration engellenir.

- `POST /api/reservations/{reservationNumber}/cancel`: yalnızca rezervasyonun sahibi olan aktif Passenger hesabı; kalkış zamanı gelmeden iptal. Tekrar istek başarılı yanıt verir, kontenjanı tekrar değiştirmez. Başka yolcunun numarası `404`, yanlış rol `403`, geçmiş kalkış `409` döner.
- `GET /api/reservations/company`: aktif Company hesabı için yalnızca oturumdaki firmanın kayıtları. `tripId`, `dateFrom`, `dateTo`, `status`, `pageNumber`, `pageSize` filtreleri vardır. Tarihler Türkiye saatindeki kalkış gününü kapsar. Yolcu isimleri yalnızca firma yetkili listesinde bulunur.
- Mevcut sefer iptali, aktif rezervasyonları ve kontenjanı aynı transaction'da günceller; seferin iptal nedeni yolcu ve firma ekranında görünür. Yolcunun daha önce iptal ettiği kaydın zamanı/nedeni değiştirilmez.

Firma ekranı `/company/reservations`, yolcu ekranı `/reservations` adresindedir. Yolcu iptalinde yerel onay penceresi açılır; odak pencere içinde kalır ve işlem sürerken tekrar gönderim engellenir. Hiçbir işlem ödeme veya iade yapmaz.

Doğrulama: 66 SQL Server/HTTP kontrolü ve 5 ön yüz testi geçti. Kontroller; 8 eş zamanlı iptalin tek kontenjan artışı üretmesini, iptal/yeniden rezervasyon yarışını, sefer/yolcu/oluşturma yarışını, başka yolcu ve başka firma erişiminin engellenmesini, Türkiye tarih sınırlarını ve eski kayıtların migration sırasında korunmasını kapsar. Test verileri ayrı geçici veritabanındadır. Yerel uygulamadaki mevcut 5 sefer ayrıca salt okunur isteklerle doğrulanmıştır.
