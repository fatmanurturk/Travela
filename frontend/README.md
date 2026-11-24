# Travela

React, TypeScript ve Vite ile geliştirilmiş Travela otobüs bileti arayüzü.

## Geliştirme

Bağımlılıkları yükleyip arayüzü başlatın:

```sh
npm install
npm run dev
```

Vite geliştirme sunucusu `/api` isteklerini `http://localhost:5062` adresindeki ASP.NET Core API'ye yönlendirir. API farklı bir adreste çalışıyorsa `VITE_API_BASE_URL` ortam değişkenini tanımlayın; değişken boş bırakıldığında Vite proxy kullanılır.

Üretim derlemesi ve lint:

```sh
npm run build
npm run lint
```

Ana sayfa ve arama sonuçları aktif güzergâh, firma, otobüs ve sefer kayıtlarını API üzerinden okur. Sefer arama, yaklaşan seferler ve öne çıkan güzergâhlar herkese açık salt-okunur API uçlarını kullanır; firma ve yönetici işlemlerinin yetkilendirmesi değişmeden kalır.

Genel menü mevcut `travela_token` / `travela_user` oturumunu ilk render sırasında okur. Yolcu oturumunda `Biletlerim`, diğer durumlarda `Biletimi Sorgula` gösterilir; masaüstü ve mobil aynı menüyü kullanır. `/tickets` başlığındaki PNR bağlantısı boş ve dolu listelerde `/bilet-sorgula` adresine gider. Bu adres giriş yapmış yolculara da açıktır; mevcut PNR + satın alma iletişim bilgisi ve iptal doğrulaması sürer, sorgulama bilet sahipliğini değiştirmez. `passenger-navigation.spec.mjs` menü, giriş/çıkış, yenileme, roller ve yardımcı bağlantıyı denetler; `guest-purchases.spec.mjs` sayfa içindeki isteğe bağlı girişte menü güncellenmesini de kontrol eder.

## Firma profili

`/company/profile` ekranı firma iletişim bilgileri için `/api/company/me`, yetkili kullanıcı bilgileri için `/api/company/user` kullanır. Yetkili kullanıcı GET ile kendi bilgilerini okur, PUT ile yalnızca ad soyadını günceller. `POST /api/company/user/change-password` mevcut şifreyi doğrular; eski oturumları ve sıfırlama bağlantılarını geçersizleştirip mevcut oturumun bitiş süresini koruyan yeni token döndürür. Ekran bu token'ı sonraki isteklerde kullanır. Bu uçlar yalnızca aktif bir firmaya bağlı aktif Company kullanıcılarına açıktır; yolcu profili `/api/profile` altında kalır. Yeni migration gerekmez.

Firma profili SQL Server/HTTP kontrolleri, yolcu profilinin mevcut regresyon testleriyle birlikte ayrı geçici test veritabanında çalışır. Uygulama veritabanı değiştirilmez. Proje kökünden:

```powershell
dotnet run --project tests/Travela.ProfileTests -c CompanyProfileVerification
```

Frontend dizininden firma ekranı tarayıcı kontrolleri (API yanıtları test verisiyle sağlanır):

```powershell
npm.cmd run test:browser -- company-profile.spec.mjs
```

## Şoför ve muavin profilleri

`/company/staff` ekranı kişisel/mesleki bilgileri ve belgeleri gerçek API ile saklar. `?detail=PERSONEL_ID` doğrudan profili açar. Meslek yılı ve işe giriş tarihi isteğe bağlıdır; eski kayıtlar boş alanlarla kullanılabilir. Eski JSON personel oluşturma/güncelleme uçları korunur; yeni profil alanları ve fotoğraf, `POST /api/company/staff/profile` / `PUT /api/company/staff/{id}/profile` uçlarına multipart `profile` (JSON), `photo` (isteğe bağlı dosya), `removePhoto` alanlarıyla gönderilir. Belgeler ayrı multipart `/{id}/documents` uçlarında yönetilir; güncellemede dosya gönderilmezse mevcut dosya korunur.

API'yi durdurduktan sonra proje kökünden şema değişikliğini uygulayıp yeniden başlatın:

```powershell
dotnet ef database update --project backend
```

`ExtendStaffProfiles` migration'ı eski personeli koruyarak isteğe bağlı alanları ve özel `StaffPhotos` / `StaffDocuments` tablolarını ekler. Dosyalar SQL Server'da `varbinary(max)` olarak metadata ile aynı transaction'da saklanır; ayrı dizin, herkese açık dosya adresi veya depolama servisi ayarı gerekmez. Veritabanı yedekleri dosyaları da kapsar. Fotoğraf sınırı 5 MB, belge sınırı dosya başına 10 MB'dir. Ters proxy kullanılıyorsa multipart istekler için en az 12 MB gövdeye izin verilmelidir. Okuma ve indirmede firma kapsamı, geçerli oturum ve `trip.view`; değişikliklerde `trip.create` / `trip.update` izinleri uygulanır. Dosya URL'leri de Bearer yetkilendirmesi gerektirir; istemci dosyayı yetkili fetch ile alıp geçici blob önizlemesi açar.

Belge durumları saklanmaz; her istekte `Europe/Istanbul` takvim günüyle hesaplanır. Son geçerlilik günü dahildir. Profil, personel listesi ve firma Genel Bakış → İşlem gerektiren durumlar alanı güncel uyarıları gösterir.

Proje kökünden SQL/HTTP testleri (Windows SQL kimlik doğrulaması erişimi bulunan oturumda; yalnızca yeni, geçici test veritabanına yazar):

```powershell
dotnet run --project tests/Travela.ReservationTests -c StaffProfiles -- --staff-profile-tests
dotnet run --project tests/Travela.ReservationTests -c StaffProfiles -- --staff-tests
dotnet run --project tests/Travela.ReservationTests -c StaffProfiles -- --staff-profile-unit
```

İlk test eski personel şemasından migration'ı, profil/fotoğraf/belge işlemlerini, dosya saklama hatasında geri almayı, firma/rol/izin sınırlarını ve tarih durumlarını doğrular. İkinci test mevcut sefer ekibi davranışını kontrol eder. Frontend dizininden, API yanıtları test verisiyle sağlanan 390/1440 piksel tarayıcı kontrolleri:

```powershell
npm run test:browser -- staff-profiles.spec.mjs staff.spec.mjs
```

## Admin ve firma sefer oluşturma tarihi ve Excel aktarımı

Admin `/admin/trips` ve firma `/company/trips` ekranlarında oluşturma tarihi filtresi, özel gün aralığı ve sıralanabilir Oluşturulma sütunu bulunur. `GET /api/trips` ile `GET /api/trips/export` aynı `TripManagementFilters` ve `TripManagementQuery` kodunu kullanır. Yeni sorgu alanları: `createdPeriod=all|today|yesterday|last7days|thismonth|custom`, özel aralıkta `createdFrom=YYYY-MM-DD` ve `createdTo=YYYY-MM-DD`, sıralamada `sortBy=createdAt` ve `sortDirection=asc|desc`. Varsayılan kalkış sıralaması korunur. Oluşturma tarihleri `Europe/Istanbul` takvim günlerinden UTC başlangıç dahil / bitiş hariç sınırlarına çevrilir. Boş oluşturulma tarihi belirli tarih filtrelerinden dışlanır ve iki sıralama yönünde de sona alınır.

Excel indirmesi listeyle aynı `[Authorize(Policy = "trip.view")]` politikasını kullanır. Ek JWT `Roles` koşulu yoktur; geçerli oturum, güncel izin ve veritabanındaki Admin/Company rolü `ForSession`, izin handler'ı ve ortak `ManagementQuery` ile doğrulanır. Böylece JWT rol eşlemesi nedeniyle liste izinliyken Excel'in 403 dönmesi engellenir. Firma kullanıcısı yalnızca oturum firmasının seferlerini indirebilir; farklı `companyId` gönderilirse HTTP 403 döner. Firma seçimi gönderilmese de firma kapsamı sunucuda uygulanır ve Rapor Bilgisi sayfasında gösterilir. Sunucu sayfalama alanlarını aktarıma uygulamaz; sorgudan gelen satırları Open XML SDK ile özel geçici dosyaya sırayla yazar. Dosya tamamlandığında indirilir, hata/iptal/indirme sonunda geçici dosya kapatılıp kaldırılır. Gerçek `.xlsx` dosyasında `Seferler` ve `Rapor Bilgisi` sayfaları bulunur. Tarihler Türkiye yerel saatini temsil eden sayısal Excel tarihleri, fiyat/koltuklar sayısal değerler, metinler formül çalıştırmayan inline metin hücreleridir. Toplam koltuk mevcut listeyle aynı kapasite anlık görüntüsünden (yoksa otobüsten), boş koltuk mevcut sefer envanterinden alınır; dolu = toplam − boş, bloke koltuklar ve envanterde yer tutan rezervasyonlar dahildir. Tek Excel sayfasının biçim sınırı başlık hariç 1.048.575 seferdir; bu sınır aşılırsa filtreleri daraltmayı isteyen hata döner.

Kurulum: API'yi durdurun, proje kökünden bağımlılıkları ve migration'ı uygulayın, API'yi yeniden başlatın:

```powershell
dotnet restore backend
dotnet ef database update --project backend
```

`AllowUnknownTripCreationDate` mevcut `Trips.CreatedAt` alanını nullable yapar ve indeks ekler. Güvenilir mevcut tarihler korunur; yalnızca `0001-01-01` boş tarih işareti null yapılır. Eski kayıtlara tahmini tarih atanmaz. Oluşturma/kopyalama sunucuda yeni tarih alır, düzenleme tarihi değiştirmez. Ayrı Excel/Office kurulumu gerekmez. Farklı API origin'i kullanılırken dosya adı için `Content-Disposition` CORS üzerinden açılmıştır.

SQL/HTTP ve dosya yapısı testleri izole geçici veritabanında, Windows SQL erişimi bulunan oturumda çalışır; uygulama veritabanını değiştirmez:

```powershell
dotnet run --project tests/Travela.ReservationTests -c TripExport -- --trip-export-tests
dotnet run --project tests/Travela.ReservationTests -c TripExport -- --trip-export-unit
```

Tarih sınırları/gece yarısı/ay-yıl geçişleri, migration, ortak filtreler, tüm sonuçların dışa aktarılması, sıralama, bilinmeyen tarih, oluşturma/düzenleme, sayısal hücreler, formül güvenliği ve indirme yetkisi doğrulanır. Frontend dizininden mobil/masaüstü kontrolleri (API yanıtları test verisiyle sağlanır):

```powershell
npm run test:browser -- trip-export.spec.mjs staff.spec.mjs
```

## Koltuk seçimi ve yolcu bilet kartları

`/trips/:id` (ara durak parametreleri dahil) 1180 px ile sınırlı, ortalanmış bir ana alan kullanır. Kroki altındaki araç çubuğunda durum açıklamaları ve yenileme düğmesi birlikte yer alır. Güneş sonucu tek bilgi satırında, gerekçe ve örnekleme açıklaması klavyeyle açılabilen Detaylar alanındadır. Yolcu başlığında yolcu sayısı ve aynı sunucu bitiş zamanını kullanan kompakt sayaç bulunur; ayırma/ödeme açıklaması açılabilir yardımcı satırdadır. Kartların altında masaüstünde solda ortak iletişim, sağda kupon/kampanya, fiyat özeti ve ödeme düğmesi yer alır; dar ekranlarda bu bölümler sırayla alt alta geçer. Yolcu formu sağ sütunda bulunmaz. `PassengerTicketCard` masaüstünde sefer / düzenlenebilir yolcu formu / kesikli ayırıcıyla koltuk ve fiyat bölümlerinden oluşur; tablet ve mobilde alt alta yerleşir. Her kart koltuk numarasıyla anahtarlanır; mevcut `passengerDetailsReducer` profil doldurma, temizleme, koltuk çıkarma ve süre dolmasında aynı koltuğa ait geçici bilgileri koruma davranışını yönetmeye devam eder. Kimlik ve iletişim bilgileri yalnız bileşen belleğinde tutulur.

Kart fiyatı mevcut sefer/kesim verisindeki indirim öncesi birim fiyattır. Kartlarda sipariş indirimi hesaplanmaz veya paylaştırılmaz. `CouponCheckout` mevcut sunucu teklifini alır; `CampaignPriceSummary` aynı teklifin toplam/indirim/ödenecek tutarını fiyat özetinde gösterir. Ödeme payload'ı, satış onayları, idempotency anahtarı, cinsiyet/komşuluk kuralları ve sunucu koltuk oturumu değişmemiştir. Sayaç her saniye ekran okuyucu bildirimi üretmez; süre dolması ve geçersiz seçim uyarıları görünür kalır. Koltuklar en az 44 px dokunma boyutunu korur; gerekirse yalnız kroki yatay kaydırılır. Yeni veritabanı değişikliği veya yapılandırma gerekmez.

Frontend dizininden:

```powershell
npm run build
npm run test:browser -- passenger-ticket-cards.spec.mjs passenger-autofill.spec.mjs seat-holds.spec.mjs seat-rules.spec.mjs group-purchases.spec.mjs segment-sales.spec.mjs
```

Tarayıcı testleri test API yanıtlarıyla çalışır; gerçek banka tahsilatı yapmaz. Kart testleri 390/768/1440 pikselde bölüm sırası, iki sütun, taşma, odak, güneş/ayırma detaylarının klavye kullanımı, değişmeyen ayırma bitiş zamanı, koltuk anahtarları, ortadaki kartı çıkarma, profil işlemleri, alan hataları, kuponlu toplam ve ödeme adımına gönderilen form payload'ını doğrular.
