# Gelişmiş koltuk seçimi ve güneş yönü

## Otobüs planı yönetimi düzeltmesi — 10 Kasım 2026

Sefer #21 veritabanında incelendi: **otobüs #2, firma #1, plaka 34 TRV 2026, kapasite 46**. Otobüsün `SeatLayout` alanı `Unspecified`, plan JSON'u boş. Seferde eski bir `Unspecified` taslağı var; bu fiziksel plan doğrulaması sayılmaz. İnceleme sırasında bu seferin rezervasyon/bilet sayısı 0. `ReadVerified` bu yüzden planı reddediyor; API'nin `seats: []`, `seatPlanAvailable: false`, `canReserve: false` dönmesi güvenlik kuralının beklenen sonucu.

Önceki otobüs planı kaydetme işlemi yalnızca yeni seferleri etkiliyordu. Firma paneline liste ve detaydan erişilen ayrı **Koltuk Planı Yönetimi** ekranı eklendi. Yeni ekran:

1. Doğrulanmamış otobüste düzeni boş bırakır. Firma gerçek araca göre 2+1 veya 2+2 seçer; kapasiteye göre yalnızca taslak oluşturur.
2. Şoför alanı ve geniş koridor içeren yatay planda koltuğa tıklayarak numara/satır/sütun/blokaj düzenlenir. Tüm konumlar tabloda da değiştirilebilir. Fiziksel eş koltuk anında gösterilir. Numara veya konum çakışması kaydı engeller.
3. Gerçek araçla eşleşme açıkça doğrulanır. Her plan düzenlemesi ve aktarılacak sefer seçimi bu onayı sıfırlar.
4. Mevcut seferler ayrı ayrı seçilir. Yalnızca kalkışı gelmemiş, planlanmış, kapasitesi eşleşen, aktif rezervasyonu olmayan ve fiziksel planı henüz doğrulanmamış seferler aktarıma açıktır. Diğer seferler ve biletler korunur.
5. Otobüs ve seçilen seferler tek `Serializable` transaction içinde güncellenir. Seferler ve otobüs kilitlenir; kayıt anında koşullar yeniden kontrol edilir. Tek sefer uygun değilse hiçbir plan değiştirilmez. Mevcut sefer blokajları korunur. Eski plan sürümüyle güncelleme HTTP 409 döndürür.
6. Genel otobüs ekleme/düzenleme API'si de fiziksel plan değişikliklerinde `seatPlanConfirmed` ister; bu yoldan onaysız satış açılması önlendi. Düzeni bilinmeyen yeni otobüs `Unspecified` kaydedilebilir.

Yeni `GET /api/buses/{id}/seat-plan` endpoint'i kapasiteyi, doğrulanmış planı, sürümünü ve firma kapsamındaki bağlı seferlerin aktarım uygunluğunu döndürür. Mevcut `PUT /api/buses/{id}/seat-plan` isteğine `applyToTripIds` ve `expectedPlanVersion` eklendi. Firma sahipliği ve `bus.view` / `bus.update` yetkileri uygulanır. Public API ayrıca gerçek satışa açık koltuk sayısını ve tüm dolu koltukların plan içinde olduğunu denetler. `canReserve`, doğrulanmış planın yanında sefer/firma/araç/güzergâh/terminal/tarih/kontenjan koşullarına bağlıdır.

Bu düzeltmede yeni migration gerekmedi. Gerçek otobüs veya sefer kaydı otomatik doğrulanmadı; gerçek düzeni firma seçmelidir. Sefer #21 için: **Firma Paneli → Otobüs Yönetimi → 34 TRV 2026 → Koltuk Planı Yönetimi**. Doğru düzeni seçin, taslağı gerçek araçla karşılaştırıp düzenleyin, **Sefer #21** kutusunu seçin ve son doğrulamayı işaretleyerek kaydedin. Ardından `/trips/21` planı seferin kaydedilmiş konumlarından gösterir. Bu akış 46 koltuklu izole test verisiyle doğrulandı; gerçek araç yerleşimi tahmin edilmedi.

Değişen/eklenen dosyalar: `backend/Controllers/BusController.SeatPlans.cs`, `BusController.cs`, `TripsController.Journey.cs`, `TripsController.cs`, `backend/DTOs/BusDtos.cs`, `backend/Trips/SeatPlans.cs`; `frontend/src/components/BusSeatPlanManagement.tsx`, `PhysicalSeatPlanEditor.tsx`, `PhysicalSeatPlanEditor.css`, `SeatMap.tsx`, `TripSeatEditor.tsx`, `frontend/src/pages/admin/BusManagement.tsx`, `frontend/src/physicalSeatPlans.ts`; `tests/Travela.ReservationTests/BusSeatPlanChecks.cs`, `JourneyChecks.cs`, `Program.cs`, `frontend/tests/physical-seat-plans.test.mjs`, `frontend/tests/browser/bus-seat-plan-management.spec.mjs`, `journey.spec.mjs`.

Doğrulama: backend derlemesi 0 hata/0 uyarı; EF modeli ile migration eşleşiyor. SQL Server üzerinde geçici test veritabanında **197 kontrol**, frontend'de **31 birim testi**, plan yönetimi/yolcu/ödeme regresyonlarında **17 tarayıcı testi** geçti. 46 koltuk, doğrulama zorunluluğu, atomik aktarım, firma yetkileri, eski kayıtların korunması, aktarım sonrası API görünürlüğü ve cinsiyet kontrollü rezervasyon test edildi. 390 ve 1440 px ekran görüntüleri incelendi (`tmp/ticket-qa/plan-management-*.png`). Vite derlemesi başarılı; mevcut büyük JS parçası uyarısı sürüyor.

10 Kasım 2026 tarihinde mevcut React/ASP.NET Core/SQL Server akışına entegre edildi. Yeni paket veya rezervasyon tablosu eklenmedi. `node_modules` değiştirilmedi.

## Aşamalar ve değişen dosyalar

| Aşama | İşlem | Dosyalar |
| --- | --- | --- |
| 1 – İnceleme | Mevcut sefer planı snapshot'ları, yolcu snapshot'ları, grup ödemeleri, kampanyalar, tutma süresi, unique index ve transaction kilitleri incelendi. | `backend/Trips/SeatPlans.cs`, `backend/Payments/PaymentService*.cs`, `backend/Data/TravelaDbContext.cs`, mevcut migration'lar, `PublicTripDetailsPage.tsx`, `PassengerCheckoutForm.tsx`, `SeatMap.tsx` |
| 2 – Yolcu cinsiyeti | Her koltuk için zorunlu Kadın/Erkek seçimi. Hesap bilgileri doldurulurken cinsiyet türetilmez. Koltuk kaldırılınca form ve cinsiyet silinir. Eksik veya uyumsuz bilgilerle ödeme başlatılamaz. | `backend/Models/Reservation.cs`, `backend/DTOs/GroupPurchaseDtos.cs`, `backend/DTOs/PaymentDtos.cs`, `backend/Payments/PurchasePresentation.cs`, `frontend/src/components/PassengerCheckoutForm.tsx`, `frontend/src/pages/PublicTripDetailsPage.tsx`, `frontend/src/payments.ts`, `CheckoutPage.tsx`, `GroupCheckoutPage.tsx` |
| 3 – Fiziksel komşuluk | Satır ve fiziksel sütundan eş koltuk belirlenir. Aynı sepet ve aktif tutmalar denetlenir. Yeni sipariş, ödeme sayfasının açılışı ve bilet kesinleştirmesi sunucuda doğrulanır. Araç değişimi mevcut yolcunun fiziksel yerini ve komşuluğunu korumalıdır. | `backend/Trips/SeatCompatibility.cs`, `SeatPlans.cs`, `backend/Payments/PaymentService.cs`, `PaymentService.Groups.cs`, `PaymentService.Seats.cs`, `backend/Controllers/TicketsController.cs`, `TripsController.Changes.cs`, `frontend/src/seatCompatibility.ts` |
| 4 – Otobüs görünümü | Ön solda, direksiyon/şoför, sırtlıklı koltuklar, 48 px koltuk ve 88 px koridor. Mobilde yatay kaydırma. Erkek mavi, kadın pembe, seçili yeşil, kapalı/geçici gri; metin etiketleri ve erişilebilir açıklamalar. 2+1/2+2/özel plan. | `frontend/src/components/SeatMap.tsx`, `SeatMap.css`, `frontend/src/Site.css`, `PublicTripDetailsPage.tsx`, `PhysicalSeatPlanEditor.tsx`, `TripSeatEditor.tsx`, `frontend/src/pages/admin/BusManagement.tsx`, `backend/Controllers/BusController.cs`, `TripsController.Journey.cs` |
| 5 – Güneş | Tarih/saat, gerçek yol geometrisi, azimut, yükseklik ve segment hareket yönü birlikte kullanılır. En fazla 5 dakikalık aralıklarla sol/sağ/gece süreleri hesaplanır. | `backend/Trips/SunExposure.cs`, `backend/Models/Route.cs`, `backend/DTOs/PublicTripDtos.cs`, `RouteManagementDtos.cs`, `backend/Controllers/TripsController.cs`, `RoutesController.cs`, `frontend/src/components/RouteGeometryEditor.tsx`, `frontend/src/pages/company/RouteManagement.tsx`, `SeatMap.tsx` |
| 6 – Migration | Nullable `Reservations.PassengerGender` ve `Routes.GeometryJson` ile kontrollü cinsiyet CHECK kısıtı. Veri içeren kolonların geri migration ile düşürülmesi engellenir. | `backend/Data/TravelaDbContext.cs`, `backend/Migrations/20261110061925_AddPassengerGenderAndRouteGeometry.cs`, ilgili Designer ve `TravelaDbContextModelSnapshot.cs`, `tests/Travela.ReservationTests/SeatRulesMigration.cs` |
| 7 – Test | Fiziksel komşuluk, cinsiyet, SQL yarışları, tutma süresi, sefer bağımsızlığı, güneş ve mevcut ödeme akışları; mobil ve masaüstü tarayıcı kontrolleri. | `tests/Travela.ReservationTests/SeatRulesChecks.cs`, `Program.cs`, `GroupPurchaseChecks.cs`, `JourneyChecks.cs`, `NotificationChangeChecks.cs`, `TicketOperationsChecks.cs`, `frontend/tests/seat-compatibility.test.mjs`, `frontend/tests/browser/seat-rules.spec.mjs`, `group-purchases.spec.mjs`, `journey.spec.mjs`, `legal-consent.spec.mjs` |

## Veri ve yetkilendirme

- Yeni cinsiyet değerleri `Female` / `Male`; eski kayıtlarda `NULL`. Kullanıcı profilinden veya rezervasyonu satın alan kişinin cinsiyetinden çıkarım yapılmaz.
- Cinsiyeti bilinmeyen aktif eski kaydın eş koltuğuna satış engellenir. Koridor karşısı ve tekli koltuklar etkilenmez.
- `Trip.SeatPlanJson` seferin kendi fiziksel planıdır; otobüsün güncel planı eski seferlere otomatik uygulanmaz. Doluluk `TripId` üzerinden rezervasyonlardan gelir.
- Sütun 3 koridor; 1–2 ve 4–5 kendi aralarında fiziksel eştir. Otobüs öne, ekranın soluna bakar: üstteki 1–2 otobüsün sağı; alttaki 4–5 otobüsün soludur. Satırlar önden arkaya ilerler. Numara sırasından komşuluk türetilmez. Özel planlar farklı numaralama, eksik eş ve değişken satırları destekler; tek orta koridor kullanılır.
- Firma/yönetici otobüs detayındaki veya sefer düzenleme ekranındaki **Fiziksel koltuk planını tanımla / doğrula** alanını kullanır. Standart taslak gerçek araçla karşılaştırılmalı, gerekiyorsa JSON konumları düzeltilmelidir. Açık doğrulama olmadan yeni plan endpoint'i kaydetmez.
- `PUT /api/buses/{id}/seat-plan`: `bus.update` yetkisi ve firma sahipliği; yeni seferler için plan. `PUT /api/trips/{id}/seat-plan`: `trip.update` yetkisi ve sefer sahipliği. Aktif rezervasyonlu doğrulanmış seferin fiziksel planı değiştirilemez. Eski `Unspecified` seferler mevcut bilet numaraları korunarak tanımlanabilir.
- Güzergâh detayında yalnızca yönetici **Güneş hesabı için doğrulanmış yol geometrisi** alanını kullanır. `GET/PUT /api/routes/{id}/geometry` mevcut yetki sistemine bağlıdır. Koordinatlar kalkıştan varışa doğru gerçek yolu izlemeli; HTTPS kaynak ve doğrulama onayı verilmelidir. En az 3 nokta, ardışık noktalar arasında en fazla 20 km, en fazla 20.000 nokta kabul edilir. Kaynağı yönetici doğrular; otomatik harita sağlayıcısı entegrasyonu yapılmadı.
- Yeni API alanları: public trip yanıtında `seatOccupancies` ve `sunExposure`; yolcu isteğinde `gender`; ödeme/bilet yanıtında `passengerGender`. Mevcut endpoint yolları ve kampanya hesaplaması korundu.
- Eksik/geçersiz bilgiler HTTP 400; koltuk/tutma/uyumluluk/satış çatışmaları HTTP 409; yetkisiz ve bulunamayan kaynaklar mevcut 401/403/404 davranışını korur.
- Satış ve kesinleştirme, mevcut `Serializable` transaction + sefer `UPDLOCK/HOLDLOCK` + aktif `(TripId, SeatNumber)` unique index ile korunur. Kesinleştirme doğrulaması başarısızsa mevcut telafi iade akışı kullanılır.

## Güneş hesabının sınırları

[NOAA General Solar Position Calculations](https://gml.noaa.gov/grad/solcalc/solareqns.PDF) denklemleri kullanıldı. Yeni astronomi paketi kurulmadı. Hesap UTC anlarıyla yapılır; Türkiye UTC+3 gösterimi aynı anı korur. Yol boyunca konum ve yön değişir; süre yol segmentlerinin uzunluğuna orantılı dağıtılır. Her örnekte güneş ufkun altındaysa gece süresine eklenir. Yolculuğun tamamı geceyse **Gece seferi** gösterilir; bir bölümü gündüzse gündüz tarafları ve gece süresi birlikte değerlendirilir. İki taraf yakınsa veya güneş çoğunlukla ön/arka hizasındaysa belirgin taraf gösterilmez.

Sonuç tahminidir. Trafik, duraklamalar, hız değişiklikleri, bulut, arazi/bina gölgesi ve kırılma modellenmez. Yalnızca şehir adları, kalkış saati veya iki uzak koordinat kullanılarak yön üretilmez. Güvenilir veri yoksa **Güneş yönü bilgisi hesaplanamadı** gösterilir.

## Doğrulanan sonuçlar

- `dotnet build tests/Travela.ReservationTests --no-restore`: başarılı, 0 uyarı / 0 hata.
- `dotnet ef migrations has-pending-model-changes --project backend --no-build`: model ve migration eşleşiyor.
- `dotnet run --project tests/Travela.ReservationTests --no-build -- --seat-unit`: 26 kontrol geçti. Kadın/erkek, koridor/tekli, özel numaralama, eski cinsiyet, aktif/süresi dolmuş tutma, ekinoks referansı, UTC+3 eşdeğerliği, gündüz/gece ve yön değiştiren rota.
- SQL Server geçici test veritabanında entegrasyon paketi: **185 kontrol geçti**. Aynı koltuk ve farklı cinsiyetli eş koltuk yarışları, sefer bağımsızlığı, plan/geometri yetkileri, bilet kesinleştirme, ödeme/iade/PNR/bildirim regresyonları dahil. iyzico denetimleri yerel sahte sağlayıcı ile yapıldı; gerçek iyzico ağı veya gerçek ödeme denenmedi.
- `npm.cmd run build`: başarılı. Vite büyük JS parçası uyarısı veriyor; derleme hatası yok.
- `npm.cmd test`: 28 test geçti.
- `npx.cmd playwright test seat-rules.spec.mjs group-purchases.spec.mjs legal-consent.spec.mjs journey.spec.mjs campaign-loading.spec.mjs --global-timeout=180000`: **29 test geçti**. Tarayıcı testleri izole API fixture'ları kullanır; üretim doluluğunu değiştirmez. 390/1440 px, 2+1/2+2, yatay kaydırma, koridor oranı, cinsiyet değişimi, hesap doldurma, koltuk kaldırma, sipariş/kupon/sözleşme kontrolleri. Ekran görüntüleri `tmp/ticket-qa/seat-rules-*.png` içinde görsel olarak incelendi.
- Migration yerel veritabanına uygulandı: 23 tablonun eski sütunlarındaki tüm verilerin önce/sonra SHA-256 parmak izleri eşleşti. Var olan kayıtlar silinmedi veya cinsiyet/geometri ile doldurulmadı.
- Yerel API güncel sürümle `http://localhost:5062` üzerinde yeniden başlatıldı. `http://localhost:5173/trips/21` HTTP 200 dönüyor.

## Sefer #21 için gereken gerçek veri

Kontrol sırasında bu seferde `SeatLayout = Unspecified`, kapasite 46, `SeatPlanAvailable = false`, `CanReserve = false`, `SunExposure.Side = Unknown` döndü. Kalkış zamanı geçmemiş. Firma/yönetici gerçek fiziksel planı tanımlamalı; güneş hesabı için ayrıca doğrulanmış yol geometrisi girilmeli. 46 sayısı bu seferin mevcut verisidir; plan bileşeninde sabit kapasite kullanılmaz.

Referans görsel konuşmaya eklenmediği için görselle birebir eşleşme doğrulanamadı; yazılı yatay otobüs ve renk tarifine göre uygulandı. Gerçek araç planı veya gerçek yol geometrisi doğrulanmış gibi raporlanmadı.
