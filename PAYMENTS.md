# Bilet satın alma, ödeme ve iade

Bu sürüm **yalnızca Development/Testing ortamındaki LocalTest adaptörünü** içerir. Gerçek para tahsil veya iade edilmez. Yerel ekran kart bilgisi istemez. Canlı sağlayıcı bulunmadığından Production başlangıcı bilinçli olarak engellenir; test sağlayıcısı sessizce canlı ortama geçmez.

## Çalıştırma ve migration

Proje kökünde mevcut SQL Server bağlantısı ve JWT geliştirme ayarlarıyla:

```powershell
dotnet build backend -c PaymentVerification --no-restore
dotnet ef database update --project backend --configuration PaymentVerification --no-build
dotnet run --project backend -c PaymentVerification --no-build --launch-profile http
```

Ayrı terminal:

```powershell
cd frontend
npm.cmd ci
npm.cmd run dev
```

API `http://localhost:5062`, Vite varsayılan olarak `http://localhost:5173` adresindedir. Vite `/api` proxy hedefi değişmedi. JWT anahtarları mevcut user secrets/ortam değişkenlerinde tutulur; kaynak koda eklenmez.

Yeni migration'lar `AddTicketPayments` ve `HardenPaymentInvariants` birlikte uygulanır. Hiçbir mevcut rezervasyon, kullanıcı veya sefer silinmez. Mevcut kayıtların `Kind=Legacy` olması sağlanır; bu kayıtlara PNR, ödeme ya da sahte tahsilat kaydı eklenmez. Ödeme/bilet geçmişi oluşmuşsa veri kaybına yol açacak geri migration engellenir.

## Kullanıcı akışı

Sefer → boş koltuk → sunucudan fiyatlandırılan sipariş → açıkça etiketlenmiş yerel test ödeme ekranı → sağlayıcı doğrulaması → PNR. `/tickets` sayfası **Biletlerim**'dir; eski `/reservations` adresi buraya yönlenir. `/test-payment/:orderId` yalnızca siparişin sahibi Passenger hesabıyla açılır. Test ekranındaki başarısız/başarılı düğmeler sağlayıcı test defterini değiştirir; ardından imzalı bildirim ve sunucudan sağlayıcı sorgusu çalışır. Tarayıcı yönlendirmesi, URL parametresi veya ön yüzden tutar gönderilmesi bilet üretemez.

Eski ücretsiz kayıtlar Biletlerim altında ayrı bölümde korunur. Bunlar ödenmiş bilet değildir; halen aktif olan kayıt koltuğu tutmaya devam eder. Yolcu, gelecekteki seferin ücretsiz kaydını serbest bırakıp ödeme akışından yeni satın alma yapabilir. Eski kaydın serbest bırakılmasında tahsilat/iade yoktur. Bağımsız yeni rezervasyon POST endpoint'i `410` döner.

Firma panelinde PNR, yolcu, sefer, koltuk, tutar, bilet/kayıt, ödeme ve iade durumları; hata ve destek takip bilgileri görüntülenir. Firma kapsamı backend'de oturumdan alınır. Firma yalnızca kendi seferlerine erişebilir.

## Durumlar ve tutarlılık

- Aynı `Reservations` tablosu koltuk sahipliğini korur: `Legacy`, `Hold`, `Ticket`. Yalnızca aktif kayıtlar için `(TripId, SeatNumber)` benzersiz indeksi vardır. Sipariş oluşturma, ödeme doğrulama, yolcu/sefer iptali ve tutma sonlandırma önce aynı sefer satırını `UPDLOCK, HOLDLOCK` ile kilitler. Koltuk ve durum tek transaction'da değişir.
- Dahili `Hold` en fazla 10 dakika sürer ve kalkıştan sonraya uzamaz. Worker 20 saniyelik döngüyle süresi dolan tutmaları bırakır; yeni checkout ve detay okuma sırasında da süresi dolmuş tutmalar temizlenir. Bu mekanizma kullanıcıya rezervasyon ürünü olarak sunulmaz.
- Sipariş `(PassengerId, RequestKey)`, sağlayıcı işlem referansı, bildirim `(Provider, EventId)` ve PNR benzersizdir. Tekrar tıklamalar aynı sağlayıcı oturumuna gider. Yerel bildirim HMAC ve 5 dakikalık zaman penceresiyle doğrulanır; ödeme durumu/tutarı/para birimi/test ortamı ayrıca sunucudan sorgulanır.
- Ödeme `Pending / Paid / Failed`, bilet `Active / Cancelled`, iade `None / Pending / Succeeded / Failed` olarak ayrıdır. Bilet iptali iadenin tamamlandığını ifade etmez. Test işlemlerinin tüm ekranlarında gerçek para olmadığı belirtilir.
- Yolcu iptali `DepartureTimeUtc - ServerUtcNow >= 2 saat` olduğunda kabul edilir; **tam iki saatte kabul edilir**. İptal son zamanı Türkiye saatinde gösterilir. Daha az süre veya geçmiş kalkışta iptal/iade reddedilir. Firma sefer iptali bu iki saat kuralından bağımsız tam iade planlar.
- Kabul edilen iptal bileti iptal edip koltuğu açar ve aynı transaction içinde tam iade outbox durumunu (`Pending`, tutar, sabit iade anahtarı) yazar. Tekrar iptal ikinci koltuk artışı veya iade oluşturmaz.
- `PaymentRecoveryWorker` kayıp ödeme bildirimlerini sağlayıcı sorgusuyla tamamlar, kalıcı iade işlerini işler. İade çalışanı süreli DB lease alır; sağlayıcı çağrısı DB transaction'ı dışında yapılır. Sonucu lease token'ıyla kaydeder. Sağlayıcı aynı iade anahtarını her retry'da idempotent olarak uygulamalıdır. Ağ yanıtının kaybı ikinci iade anlamına gelmez.
- Başarısız/bekleyen iade için 30 saniyeden başlayıp en fazla bir saate çıkan bekleme uygulanır. Sekiz işleme denemesinden sonra `NeedsAttention` destek takibi olarak işaretlenir. Bekleyen/başarısız kayıt silinmez, başarılı referans olmadan tamamlandı gösterilmez. Bankaya yansıma süresi vaat edilmez.
- Ödeme başarılı ama tutma süresi dolmuş, koltuk bırakılmış veya sefer iptal edilmişse PNR oluşturulmaz. Tahsilat doğrulanmış olarak saklanır; tam telafi iadesi ve takip nedeni kaydedilir. Ödeme sonrası DB yazımı başarısız olursa pending sipariş/provider defteri korunur; worker yeniden mutabakat yapar. Canlı sağlayıcıda teslimat retry ve mutabakat sözleşmesi ayrıca doğrulanmalıdır.

## Testler

```powershell
dotnet run --project tests/Travela.ReservationTests -c PaymentVerification
cd frontend
npm.cmd run build
npm.cmd test
npm.cmd run lint
```

Mevcut test projesi ödeme akışına güncellendi. Gerçek SQL Server üzerinde `TravelaPaymentTests_<rastgele>` adlı ayrı geçici veritabanı ve gerçek HTTP istekleri kullanılır; yalnızca kendi oluşturduğu test veritabanı temizlenir. Uygulama veritabanına ödeme/test bileti eklenmez. Sabit test saatiyle 121/120/119 dakika sınırları test edilir. Diğer kontroller başarılı/başarısız/yarım ödeme, geç ödeme telafisi, aynı koltuk ve aynı işlem anahtarıyla eş zamanlı tıklama, bildirim tekrarları, iade lease'i ve yeniden denemeler, yetkisiz yolcu/firma erişimi, iki saatten az kalan seferin firma tarafından iptali ve eski verilerin migration ile korunmasını kapsar.

Çalışan yerel uygulamada mevcut seferleri salt okunur doğrulama:

```powershell
dotnet run --project tests/Travela.ReservationTests -c PaymentVerification -- --verify-existing
```

## Gerçek entegrasyon için gerekenler

1. Seçilen sağlayıcı, sandbox hesabı, işyeri/satıcı kimliği, sandbox API adresleri ve belgeleri; test API kimlik bilgileri ve webhook imza doğrulama anahtarı. Bunlar secret store/ortam değişkenlerinde tutulmalı.
2. `IPaymentProvider` için sağlayıcının resmi SDK/API'siyle adaptör: idempotent hosted checkout oluşturma, sunucudan işlem sorgulama, ham webhook gövdesi/imzası/zaman penceresi doğrulama, tam iade başlatma ve sonuç sorgulama. Yerel HMAC şeması gerçek sağlayıcı protokolü değildir.
3. HTTPS webhook adresi, sağlayıcı panelinde bildirim kaydı, izin verilen hosted ödeme alan adı ve dönüş/iptal URL'leri. Ön yüzdeki `safeCheckoutPath` şu an yalnızca yerel test yoluna izin verir; canlı hosted URL için sağlayıcı alan adına bağlı açık izin listesi eklenmeli.
4. Sağlayıcının işlem/oturum referanslarının saklanması, idempotency saklama süresi, zaman aşımı, geç bildirim, bekleyen iade, başarısız iade ve yeniden sorgulama davranışlarının sandbox sözleşme testleri. Worker ölçeklendirme, alarm ve sekiz deneme sonrasındaki yetkili manuel mutabakat/yeniden deneme operasyonu.
5. Canlı yayına geçiş için ayrı inceleme ve açık yetkilendirme. Bu değişiklik gerçek tahsilat veya gerçek iade başlatmaz. Kart/PAN/CVV alınmaz, veritabanına veya loglara yazılmaz; gerçek akış sağlayıcının güvenli ödeme ekranında olmalıdır.
