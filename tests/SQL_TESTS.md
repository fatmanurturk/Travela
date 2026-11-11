# Yerel SQL entegrasyon testleri — Aşama 0

Reservation, Route ve Reporting testleri ana uygulama veritabanını test hedefi
olarak kullanmaz. Sunucu/kimlik bilgileri `TravelaDbContextFactory` ile mevcut
yapılandırmadan okunur; veritabanı adı bağlantı açılmadan önce değiştirilir:

| Test projesi | Her çalıştırmada oluşturulan veritabanı |
| --- | --- |
| Travela.ReservationTests | `TravelaPaymentTests_<32 haneli GUID>` |
| Travela.RouteTests | `TravelaRouteTests_<32 haneli GUID>` |
| Travela.ReportingTests | `TravelaReportingTests_<32 haneli GUID>` |

Yerel geliştirme yapılandırmasındaki sunucu `HP1\MSSQLSERVER01`, ana veritabanı
`TravelaDB`'dir. Testler aynı SQL örneğinde bağımsız veritabanları oluşturur.
Her paket gerçek sunucuyu ve üretilen test veritabanını başlangıçta yazar;
tam bağlantı dizesini, kullanıcı parolasını veya token'ları yazmaz.

## Çalıştırma

SQL Server erişimi olan Windows hesabıyla normal yerel PowerShell oturumunda,
proje kökünden çalıştırın. Yönetici olarak PowerShell açmak gerekmez:

```powershell
./scripts/test-stage0.ps1
```

Betik mevcut paketlerle backend ve üç test projesini derler, bağlantı hedefi
güvenlik kontrollerini ve üç entegrasyon paketini sırayla çalıştırır. İlk hatada
durur. Varsayılan `Release` derlemesi açık `Debug` geliştirme API'sinin dosyalarını
kilitlemez veya sunucuyu durdurmayı gerektirmez. `-Configuration Debug` ile
değiştirilebilir. Yalnızca testleri yeniden çalıştırmak için `-SkipBuild` kullanılabilir.
Bağımlılıklar ilk kurulumda henüz restore edilmediyse mevcut proje paketlerini
önce restore edin; bu aşama yeni paket eklemez.

Bağlantı koruma kontrolleri SQL Server olmadan da çalışır:

```powershell
dotnet run --project tests/Travela.ReservationTests --configuration Release -- --test-db-unit
```

İsteğe bağlı `TRAVELA_TEST_SQL_CONNECTION` ortam değişkeni, yalnızca bu üç
paketi ayrı bir test SQL örneğine yönlendirir. Değerini komut çıktısına veya
Git'e kaydetmeyin. Bu bağlantıdaki veritabanı adı da daima yeni GUID içeren test
adıyla değiştirilir. Uzak sunucularda geçerli sertifika ve
`TrustServerCertificate=false` gerekir.

## Şifreleme ve Windows çalıştırma ortamı

Önceki `SQL error 20` hatası kısıtlı Codex sandbox içinde yeniden üretildi.
Aynı paket normal yerel Windows izinleriyle SQL Server'a bağlanabildi.
Bağımsız salt okunur sorgu da yerel bağlantıda `encrypt_option=TRUE` döndürdü.
Dolayısıyla hata tek başına sunucunun TLS desteklemediği anlamına gelmiyordu.

`tests/Shared/SqlTestDatabase.cs` test bağlantısında en az `Encrypt=Mandatory`
zorunlu kılar; mevcut `Strict` ayarını düşürmez. Bağlantı açılışında, herhangi
bir test veritabanı oluşturulmadan önce `master` üzerinde yalnızca mevcut
oturumun şifreleme ve sunucu bilgisi okunur. Şifreleme doğrulanamazsa test
başlamaz. Veritabanı yazma ve migration işlemleri üretilmiş test hedefindedir.

Yerel sunucunun kendinden imzalı sertifikası için mevcut
`TrustServerCertificate=true` ayarı yalnızca yerel makine/loopback adreslerinde
kabul edilir. Bu, trafiğin şifrelemesini kapatmaz; sertifika kimlik doğrulamasını
atlar. Uzak sunucuda bu ayar reddedilir. Üretim bağlantı ayarları değiştirilmez.

Codex kısıtlı çalıştırmada aynı hata sürerse yalnızca test komutunu
sandbox dışında normal kullanıcı izinleriyle çalıştırın. Yeni hata mesajı bunu
açıklar. `Encrypt=false`, TLS/registry değişikliği, yönetici hesabı veya SQL
kimlik doğrulamasına geçiş çözüm olarak kullanılmaz.

SqlClient'in managed networking anahtarı eklenmedi: Microsoft belgelerinde
non-domain Windows kimlik doğrulaması için kısıt belirtiliyor ve burada çalışan
native bağlantı yolunu değiştirmek gerekmiyor.

## Veri koruması ve test verileri

- Üç izinli önek dışında test veritabanı üretilemez.
- Yazma/temizlik hedefi üretilen tam GUID adı ve sunucuyla eşleşmelidir.
  `TravelaDB`, sistem veritabanları ve başka çalıştırmanın test veritabanı
  temizlik hedefi olamaz.
- Test API alt sürecine de aynı geçici bağlantı dizesi aktarılır.
- Başarılı/başarısız testin `finally` bloğu yalnızca kendi test veritabanını
  temizler. Sunucu ön kontrolü başarısızsa veritabanı oluşturulmaz ve temizlik
  yeni bir SQL hatasıyla asıl hatayı gizlemez.
- Tarihi canlı doğrulama/migration komutları (`--migrate-*`, `--verify-*`)
  bu betik tarafından çağrılmaz. Yeni migration eklenmez veya ana veriye
  migration uygulanmaz; mevcut migration'lar yalnızca test veritabanına uygulanır.

SQL bağlantısı açılınca ortaya çıkan eski fixture eksikleri de giderildi:
Route hukuki onay testinde cinsiyet ve doğrulanmış fiziksel plan tanımlandı;
Reporting sentetik ödeme kayıtlarında `OriginalAmount=Amount` dolduruldu.
Uygulamanın doğrulama ve finansal kısıtları gevşetilmedi.

## 11 Kasım 2026 doğrulaması

`scripts/test-stage0.ps1` normal yerel Windows izinleriyle başarıyla tamamlandı:

| Kontrol | Sonuç |
| --- | --- |
| Backend ve üç test projesi, Release | 0 hata, 0 uyarı |
| SQL hedefi/şifreleme güvenlik kontrolleri | 20 geçti; SQL bağlantısı gerektirmez |
| Reservation entegrasyonu | 197 geçti |
| Route entegrasyonu | 61 geçti |
| Reporting entegrasyonu | 95 geçti |
| Frontend `npx tsc -b` | Başarılı |
| Frontend `npx oxlint` | Çıkış 0; mevcut uyarılar sürüyor |
| Frontend `npm test` | 31 geçti |

Üç SQL bağlantısında da şifreleme `TRUE` doğrulandı. Her çalıştırmanın kendi
test veritabanı temizlendi. Yeni migration, uygulama kodu veya paket değişikliği
yapılmadı. Aşama 1 başlatılmadı.

Kaynaklar:
- [Microsoft — şifreleme ve sertifika doğrulaması](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation)
- [Microsoft — SqlClient AppContext anahtarları](https://learn.microsoft.com/en-us/sql/connect/ado-net/appcontext-switches)

## Aşama 1 — segment çekirdeği ve migration taslağı

`scripts/test-stage1.ps1` üç mevcut entegrasyon paketini, yeni segment SQL
testlerini ve EF model/migration eşleşmesini doğrular. Yazma ve mevcut
migration'ların uygulanması yine yalnızca GUID adlı geçici test veritabanlarındadır.
Frontend doğrulaması: `frontend/` altında `npm test`, `npm run build`, `npm run lint`.

Taslak: `20261111124242_AddIntermediateStopInventory`. **Ana veritabanına henüz
uygulanmadı; uygulama ayrı kullanıcı onayına bağlıdır.** Yeni backend sürümünü
ana veritabanında çalıştırmak için önce bu şema geçişinin onaylanması gerekir.
`Trips:IntermediateStopsEnabled=false` kalır; yeni segment satış endpoint'i veya
arayüzü bu aşamada açılmaz. Mevcut idempotency özetleri ve bilet JSON'ları değişmez.

Yeni `RouteStop`, `TripStop`, `TripLeg`, `TripSegmentFare`, `ReservationLeg`
modelleri ve `TripSegmentService` ortak envanter çekirdeğini oluşturur. Mevcut
tam güzergâh yazıcıları, Serializable transaction ve sefer UPDLOCK/HOLDLOCK
korunarak bağlanmıştır. Koltuk-dakika raporu ve segment satış arayüzleri kendi
sonraki aşamalarında uygulanacaktır.

Bacak üretimi dar kapsamlı `TR_Reservations_Legs` AFTER trigger'ındadır;
fiyat, cinsiyet, durak seçimi ve satış kuralları uygulama servisinde kalır.
`TR_ReservationLegs_Consistency` eksik/taşınmış/silinmiş bacağı ve ebeveynle
uyumsuz aktifliği reddeder. Filtreli tekil indeks aktif `(TripId, SeatNumber,
LegNumber)` içindir. Eski sefer/koltuk tekilliği, yeni indeks oluşturulup geçmiş
veriyle doğrulanana kadar kaldırılmaz. Hold→Ticket aynı bacakları korur; iptal,
başarısız ödeme ve süre aşımı geçmiş satırları silmeden pasifleştirir.

EF trigger bulunan tablolarda `UseSqlOutputClause(false)` kullanır. Trigger'ın
ürettiği bacaklar EF ile yeniden eklenmez; veritabanından sorgulanır. Tam
güzergâh boş koltuk önbelleği, bütün bacaklardaki dolu **farklı koltukların
birleşiminden** hesaplanır; bacak minimumu veya artırım/azaltım kullanılmaz.

Eski şemada şehirler seferin `RouteId` ile bağlı olduğu iki uçtan okunur
(mevcut API, bağlı güzergâhın şehirlerini değiştirmeyi engeller). Yeni rota
durak şablonu bu geçişte kaynak değildir. Saat ve terminal anlık görüntüleri
mevcut sefer sütunlarından alınır. Rezervasyonda eski bilet JSON'u varsa
kayıtlı yolculuk saatleri korunur; yoksa sefer saatleri kullanılır. İptaller
dahil her eski rezervasyon ilk→son durağa bağlanır.

`SegmentMigrationChecks` eski şemada gerçek SQL verisi üretir; geçiş öncesi
ve sonrası bütün mevcut tabloların kayıt sayıları ve eski sütunlarının SHA-256
parmak izlerini karşılaştırır. Ödeme/iade toplamları, PNR, bilet JSON'u ve eski
koltuk numaraları ayrıca korunur. Bozuk veri testi geçişin durduğunu ve
verinin otomatik düzeltilmediğini denetler. Bunlar gerçek geliştirme verisi
üzerinde denenmez.

Geri dönüş: `Down` hiçbir tablo/sütun silmez; 51013 ile işlemi reddeder.
Uygulama onayından önce yazmaları durdurup doğrulanmış tam yedek alınmalı;
geri dönüş gerekirse yeni backend/worker'lar durdurulmalı, geçiş öncesi yedek
ayrı hedefe geri yüklenip parmak izi/finansal toplamlar kontrol edilmeli ve
eski uygulama sürümüyle bağlantı değiştirilmelidir. Yeni segment verisi varken
eski tekil indeksi geri ekleyerek kısmi geri dönüş yapılmaz.

11 Kasım 2026 Aşama 1 doğrulaması:

| Kontrol | Sonuç |
| --- | --- |
| Yeni segment/migration/gerçek SQL eşzamanlılık testleri | 63 geçti |
| Reservation / Route / Reporting | 197 / 61 / 95 geçti |
| SQL hedef/şifreleme korumaları | 20 geçti |
| Profil/oturum/hesap regresyonları | 136 geçti |
| Ödeme sağlayıcısı / başlangıç-DI kontrolleri | 107 / 79 geçti |
| Worker yapılandırma, bekleme ve iptal kontrolleri | Başarılı; SQL/teslimat yok |
| Backend/test derlemeleri ve EF model eşleşmesi | Başarılı; derlemelerde 0 uyarı, 0 hata |
| Frontend birim / üretim derlemesi / lint | 31 geçti / başarılı / çıkış 0, mevcut 32 uyarı |

Vite'ın mevcut 500 kB paket boyutu uyarısı sürmektedir. Üç zorunlu SQL paketi
ve segment paketi şifreli bağımsız test veritabanlarını temizledi. Üretilen
inceleme SQL'i `tmp/segment-stage1/migration.sql` konumundadır; yürütülmemiştir.
Ana veritabanı değiştirilmedi; özellik bayrağı açılmadı; Aşama 2 başlatılmadı.
