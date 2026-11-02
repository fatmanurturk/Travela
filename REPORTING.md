# Firma raporları

`/company` ve `/company/reports`, `CompanyReportingService` üzerinden aynı hesaplamaları kullanır. API: `GET /api/company/reports` (ayrıca `/overview`), `/options`, `/csv`. Yalnızca aktif Company hesabının veritabanındaki firmasına izin verilir. İstemciden gelen `companyId` kullanılmaz. Başka firmanın sefer/güzergâh filtresi 404; oturumsuz erişim 401, uygunsuz rol 403 döner. Kişisel rapor yanıtları `private, no-store` kullanır.

## Metrikler ve kapsam

- Satılan: doğrulanmış Paid ödeme + Ticket kaydı + PNR. Sonraki iptal satış geçmişini silmez. Aktif: bu satışların halen Active olanları. İptal: kesinleşmiş Cancelled biletler.
- Tahsilat: yalnızca Paid ödeme tutarı. Bilet düzenlenemeyip telafi iadesi gereken doğrulanmış ödeme de tahsilattır; satış değildir.
- Tamamlanan iade: yalnızca Succeeded iade. Pending tutarı ve Failed adet/tutarı ayrıdır. `Tahsilat − tamamlanan iade` bu iki toplamın farkıdır, kâr değildir.
- Her ödeme siparişi yalnızca bir kez değerlendirilir. Bildirim tablosuna join yapılmaz. Bekleyen/başarısız ödemeler ve eski ücretsiz Legacy kayıtları finansal toplam veya satış değildir.
- `environment=real|test`: varsayılan gerçek işlemler. `IsTest` üzerinden kesin ayrım yapılır; gerçek veri yoksa sıfırdır. Teste otomatik geçilmez. Seçili ortam ve dönem tek özet satırındadır; test raporunun üstünde belirgin ortam uyarısı, CSV'de test etiketi bulunur. Kartlar yalnızca başlık ve değeri gösterir; hesaplama açıklamaları klavye/dokunma ile açılabilen “Rapor hakkında” alanındadır. Gerçek tahsilat yapılmaz.

`basis=transaction` işlem tarihidir: satış IssuedAt, iptal CancelledAt, tahsilat PaidAt, tamamlanan iade RefundedAt, halen bekleyen/başarısız iade RefundStatusChangedAt üzerinden seçilir. Bu nedenle dönemdeki iptal/iade daha eski bir satışa ait olabilir; satılan eksi aktif her zaman dönemdeki iptal sayısı değildir. Bekleyen/başarısız toplamlar durum geçmişi akış sayacı değil, belirtilen tarihte güncel durumuna geçmiş ve halen o durumda olan kayıtların tutarıdır.

`basis=departure` seçili kalkış tarihindeki seferlerin tüm bilet/ödeme kayıtlarını ve güncel durumlarını kapsar. İşlem dönemine ait nakit hareketi değildir. Grafik günleri bu modda sefer kalkış günüdür; işlem modunda ilgili olay günüdür. Adet ve TRY grafikleri ayrı eksenlerdedir, eksik günler sıfırla doldurulur ve erişilebilir veri tabloları vardır.

`period=today|week|month|custom`: bugün; bugün dahil son 7 gün; ay başından bugüne; `from=yyyy-MM-dd&to=yyyy-MM-dd`. Türkiye günü UTC+03:00 ile UTC'ye dönüştürülür; başlangıç dahil, bitiş gününü izleyen gece yarısı hariçtir. Aralık en fazla 366 gün ve 2000–2100 yılları arasındadır. Sefer/güzergâh filtreleri, ortam, dönem, kapsam, sayfa ve sıralama URL'de korunur. Bekleyen eski yanıtlar AbortController ile yeni sonucu ezemez.

## Koltuklar

Koltuk bilgileri raporun üretim anındaki güncel durumdur. İşlem kapsamındaki sefer kümesi, seçili dönemde ödeme denemesi/satış/iptal/tahsilat/iade hareketi olan seferlerdir; kalkış kapsamında o tarihlerdeki tüm firma seferleridir. Yaklaşan seferler bu kümenin ilk 5 gelecekteki planlı seferidir.

Doluluk = seçili ortamdaki aktif, ödenmiş, düzenlenmiş koltuk / kapasite × 100. Genel oran, aktif satılmış koltuk toplamı / ilgili sefer kapasitesi toplamıdır. Geçici ödeme tutması, Legacy blokajı ve diğer ortamın blokajı ayrı gösterilir. Süresi dolmuş tutma satışa açık sayıyı azaltmaz. Satışa açık sayı, diğer ortam dahil tüm gerçek blokajları dikkate alır; kapalı/geçmiş/iptal seferde sıfırdır. Kapasite sıfırsa veya saklanan kontenjan/koltuklar tutarsızsa oran ve satışa açık değer hesaplanmaz; açıklama sunulur. Raporlar koltuk kaydını değiştirmez.

## CSV ve kaynak sınırları

CSV tüm filtreli seferleri kapsar; sayfalama CSV'yi kesmez. UTF-8 BOM, noktalı virgül ayırıcı, Türkçe başlık/TRY/tarih biçimleri, kapsam ve üretim zamanı içerir. Metinlerde `= + - @`, tab ve satır başlangıçları formül korumasına alınır; çift tırnaklar kaçırılır. Yolcu adı/e-posta dışa aktarılmaz. Yerel test dosya adı `YEREL-TEST` ve her satır etiketi içerir. En fazla 10.000 sefer dışa aktarılır; aşılırsa 422 ve filtreyi daraltma açıklaması döner, sessiz kırpma yapılmaz. Sorgular gruplamayı/filtrelemeyi SQL'de yapar; tablo sayfaları en fazla 100, güzergâh sıralaması ilk 20, güzergâh önerileri ilk 500 kayıttır (numara ayrıca yazılabilir). Sefer sıralaması izinli kolonlarla sınırlıdır.

## Migration ve geçmiş

`20261102131249_AddReportingDatesAndIndexes` mevcut verileri silmeden IssuedAt, RefundRequestedAt, RefundStatusChangedAt ve rapor indekslerini ekler. Yeni ödeme/iade işlemleri tarihleri işlem sırasında kaydeder. Eski biletler için, önceki uygulamada aynı ödeme doğrulama işleminde gerçekleşen düzenlenme zamanı PaidAt'ten geri doldurulur; asıl CreatedAt/CancelledAt/PNR değiştirilmez. Eski Succeeded iadelerde mevcut RefundedAt korunarak durum tarihi doldurulur.

Eski Pending/Failed iadelerin durum tarihi daha önce saklanmadığından geri üretilemez. NULL bırakılır: işlem raporundaki tarihli tutarlara katılmaz, sayfa ve CSV'de adet/tutar uyarısı gösterilir; kalkış tarihi kapsamında güncel tutarı dahil edilir. Bu tarih belirsizliği veri uydurularak kapatılmaz. Başarısız eski iadeler ilgili kayıt bağlantısıyla izlenebilir.

Mevcut uygulama veritabanında migration uygulandı; 6 sefer, 2 rezervasyon/bilet kaydı ve 1 ödeme kaydı korunarak kontrol edildi. Gerçek mod mevcut veride sıfırdır; yerel test verisine geçiş yapılmaz.

## Çalıştırma ve doğrulama

PowerShell'de:

```powershell
dotnet ef database update --project backend
dotnet run --project backend --launch-profile http
# Ayrı terminal:
cd frontend
npm.cmd install
npm.cmd run dev
```

SQL Server bağlantısı mevcut appsettings/ortam ayarlarıyla, JWT mevcut user-secrets/ortam ayarlarıyla sağlanır. Gizli anahtar kaynak koda eklenmez. API yalnızca Development/Testing yerel sağlayıcısıyla çalışır; gerçek ödeme entegrasyonu bu değişiklik kapsamında değildir.

```powershell
dotnet run --project tests/Travela.ReportingTests -c ReportingVerification
dotnet run --project tests/Travela.ReservationTests -c ReportingVerification
cd frontend
npm.cmd run build
npm.cmd test
npm.cmd run test:browser
npm.cmd run lint
```

SQL testleri yalnızca `TravelaReportingTests_<guid>` / `TravelaPaymentTests_<guid>` adlı geçici veritabanlarında çalışır; sonunda bu veritabanlarını kaldırır. Uygulama veritabanını sıfırlamaz. Tarayıcı testleri SQL/HTTP testinin `tmp/report-qa` ve `tmp/ticket-qa` altına yazdığı sentetik yanıtları kullanarak gerçek React/CSS'yi sınar; yetki ve finansal doğruluk gerçek SQL/HTTP testlerinde kontrol edilir. Ekran görüntüleri `tmp/report-qa/screenshots` altındadır. Rapor testleri 10.000 üzeri çıktı sınırı, migration koruması, iptal/iade tarihleri, bildirimlerin toplamı çoğaltmaması, ortam/firma ayrımı ve kart/grafik/CSV tutarlılığını kapsar. Mevcut ödeme testleri 121/120/119 dakika iptal sınırını, eş zamanlı satın alma/iptali ve sefer iptalini de doğrular.
