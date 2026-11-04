# iyzico sandbox ve ortak sipariş

## Kullanım ve kurallar

- Yolcu sefer planında 1–10 farklı koltuk seçer. Her koltuk için ad soyad, e-posta ve uluslararası biçimde telefon girer. Hesap sahibi, seçili koltuk için kendi profil bilgisini de kullanabilir. Bilet hesabın sahibinin erişiminde kalır; bilette girilen yolcu adı saklanır. Yolcunun başka hesabı/e-postası olması bilete erişim yetkisi vermez.
- Koltuklar aynı sefer üzerinde transaction/kilit altında birlikte ayrılır. Bir koltuk dolu, kapalı veya geçersizse sepetin hiçbir koltuğu ayrılmaz. Fiyat sunucudan alınır, toplam bilet fiyatlarının toplamıdır. Tek işlemde azami 10 koltuk vardır.
- Tek `PurchaseGroup` ortak tahsilatı temsil eder. Her koltuğun ayrı rezervasyonu, ödeme satırı, PNR'si, bildirim ve iade durumu bulunur. Tutarlar raporlarda yolcu başına ödeme satırından hesaplanır; grup toplamı tekrar tahsilat sayılmaz.
- Ödeme doğrulandığında tüm tutmalar geçerliyse tüm biletler düzenlenir. Bir tutma bile geçersizleşmişse hiçbir bilet düzenlenmez ve her satır için tam telafi iadesi başlar. Geç ödeme, iptal edilmiş/sona ermiş tutmayı yeniden açmaz.
- Ödeme sonrası her bilete normal iptal kuralı veya sefer değişikliği nedeniyle verilen ek hak ayrı uygulanır. Tek bilet iptali diğer bileti etkilemez; yalnızca iptal edilen satır tutarı iade edilir. Grup ekranında talep edilen ve sağlayıcıdan doğrulanan iade toplamları ayrı gösterilir.
- `RequestKey` yeniden denemede aynı bilgilerle aynı siparişi döndürür. Koltuk veya yolcu değiştirilmiş aynı anahtar reddedilir. Başlatma yanıtı kaybolduğunda uygulama otomatik ikinci iyzico oturumu oluşturmaz; belirsiz işlem destek/mutabakat takibinde kalır.
- Ortak sipariş `/purchase/:id` ekranındadır. Biletlerim ekranındaki bekleyen ödeme bağlantısı aynı siparişe döner. Yönetici merkezi bilet aramasında ortak sipariş kimliğiyle tüm satırları bulabilir.

## Sağlayıcı seçimi

Varsayılan `Payments:Provider=LocalTest` korunur; anahtarlar eksikken mevcut uygulama çalışmaya devam eder. Bu mod gerçek iyzico çağrısı yapmaz. Mevcut tek koltuk siparişleri de `LocalTest` sağlayıcısıyla çalışmayı sürdürür. Sağlayıcı geçmiş ödeme satırında saklandığından yeni siparişler iyzico'ya geçirilince eski LocalTest işlemleri yanlış sağlayıcıya gönderilmez.

iyzico sandbox kullanmak için backend user-secrets veya ortam değişkenlerine aşağıdakileri yerel olarak tanımlayın; anahtarları kaynak koduna, loga veya sohbete yazmayın:

| Ayar | Değer |
|---|---|
| `Payments:Provider` | `IyzicoSandbox` |
| `Iyzico:ApiKey` | Kendi sandbox API anahtarınız |
| `Iyzico:SecretKey` | Kendi sandbox gizli anahtarınız |
| `Iyzico:CallbackBaseUrl` | Backend'e ulaşan HTTPS kök adresi |
| `AccountEmail:FrontendBaseUrl` | Kullanıcının döneceği ön yüz adresi |

Ortam değişkenlerinde `:` yerine `__` kullanılır; örneğin `Payments__Provider`. `dotnet user-secrets` işlemlerinde `--project backend` seçilir. Ayarlar değişince API yeniden başlatılır. Kimlik doğrulama ve şifrelenmiş alıcı verisi için ASP.NET Data Protection anahtarlarını kalıcı tutun.

Sandbox hesabı açılması ve dışarıdan erişilebilen HTTPS adresinin hazırlanması bu repodaki kod tarafından yapılmaz. iyzico hesabında webhook adresini `https://<backend>/api/payments/iyzico/webhook` olarak tanımlayın; V3 imza özelliğinin hesapta etkin olması gerekir. Dönüş adresi uygulama tarafından `/api/payments/iyzico/callback/{groupId}` şeklinde oluşturulur. HTTPS tünel kullanılıyorsa yalnızca geliştirme API'sine yönlendirilmelidir.

Entegrasyon yalnızca `https://sandbox-api.iyzipay.com` API'sine gider. Tarayıcıya yönlendirme, yalnızca doğrulanmış sandbox checkout alan adları için açılır. Kart numarası, CVV veya son kullanma tarihi Travela formuna girilmez; iyzico hosted sayfasında işlenir. Gerekli alıcı kimlik/adres bilgileri uygulamada Data Protection ile şifreli olarak yalnızca başlatmaya kadar saklanır; başarılı başlatmada veya tutma süresi dolduğunda silinir. `GET /api/purchases/:id` alıcı kimlik/adres bilgilerini döndürmez. Sağlayıcı token'ı ayrı alan olarak dönmez; oturum sahibine verilen ödeme URL'sinde bulunabileceğinden bu URL de loglara veya herkese açık sayfalara yazılmamalıdır.

## Doğrulama ve iade

- İstekler IYZWSv2 HMAC-SHA256 ile imzalanır. Checkout başlatma, ödeme sorgulama ve iade yanıtlarının imzaları sabit zamanlı karşılaştırılır. Para değerlerinde imza için ardıl sıfırlar standartlaştırılır.
- Bilet düzenlemeden önce token, conversation/basket kimliği, para birimi, toplam ve her sepet satırının kimliği/tutarı denetlenir. Fraud incelemesindeki ödeme bilet oluşturmaz. Tarayıcı callback'i yalnızca sunucudan doğrulanmış sorgulamayı tetikler; bir token veya tarayıcı sonucu tek başına ödeme kanıtı değildir.
- Webhook `X-IYZ-SIGNATURE-V3` HPP imzasını ve kayıtlı sipariş token'ını doğrular. Sonuç yine sağlayıcıdan sorgulanır. Yinelenen bildirim bilet çoğaltmaz. Callback kullanıcıyı yalnızca yapılandırılmış ön yüz sipariş adresine döndürür.
- İade `paymentTransactionId` ile bilet satırı üzerinden yapılır. `conversationId` tek başına idempotency garantisi olarak kullanılmaz. Her denemeden önce sağlayıcının raporlama API'sinde aynı iade anahtarı/tutarı sorgulanır. İade yanıtı kaybolursa sorguyla doğrulanmadan ikinci transfer yapılmaz. Yalnızca sağlayıcının açıkça tekrar denenebilir dediği reddedilmiş istek yeniden gönderilebilir.
- Tekil ödeme, ortak sipariş ve iade kuyrukları ayrı ve sınırlı paralel çalışır. Her kayıt kendi DbContext'ini kullanır; yavaş eski tekil sorgular yeni sepetleri veya iadeleri engellemez. Eski sonuçlanmayan işlemler ileri tarihe planlanır ve destek takibine işaretlenir.

Bu sürüm sandbox entegrasyonudur. `Production` ortamında LocalTest veya sandbox ile canlı satış açılmaz. Canlı iyzico ortamı için ayrı anahtar/alan adı, hesap onayı ve gerçek hizmete karşı kabul testleri gerekir.

## Testler ve veritabanı

Migration: `20261104135339_AddGroupPurchasesAndIyzicoSandbox`.

```powershell
dotnet run --project tests/Travela.ReservationTests -- --migrate-group-purchases
dotnet run --project tests/Travela.ReservationTests
dotnet run --project tests/Travela.ProfileTests
dotnet run --project tests/Travela.ReportingTests
cd frontend
npm.cmd test
npm.cmd run test:browser -- group-purchases.spec.mjs journey.spec.mjs
```

Geçiş yardımcısı eski hesap, PNR, yolcu/yolculuk, ödeme/iade, bildirim ve sefer değişikliği verilerini karşılaştırır. Önceki tekil kayıtlar toplu siparişe dönüştürülmez. Ortak sipariş veya sağlayıcı iade kaydı varsa veri kaybettiren geri migration engellenir.

Otomatik testler gerçek kart veya para kullanmaz: iyzico HTTP sözleşmesi sahte HTTP taşıyıcısıyla; webhook/callback gerçek yerel API üzerinden; koltuk yarışları, tek satır iadesi, kayıp iade yanıtı, fraud ve imza hataları SQL/servis testleriyle doğrulanır. Yavaş eski ödeme sorgusunun yeni ortak siparişi durdurmadığı gerçek worker üzerinde kontrollü beklemeyle test edilir. Gerçek sandbox hesabına bağlanarak uçtan uca onay için yukarıdaki yerel anahtarlar ve HTTPS adresi gerekir.

## Resmî sözleşmeler

- [CheckoutForm](https://docs.iyzico.com/en/getting-started/preliminaries/api-reference-beta/payment-methods/checkoutform)
- [HMAC doğrulaması](https://docs.iyzico.com/en/getting-started/preliminaries/authentication/hmacsha256-auth)
- [Yanıt imzaları](https://docs.iyzico.com/ek-servisler/imza-yanitinin-dogrulanmasi)
- [Webhook V3](https://docs.iyzico.com/en/advanced/webhook)
- [İade](https://docs.iyzico.com/en/advanced/refund-and-cancel)
- [İade geçmişi raporlama](https://docs.iyzico.com/en/advanced/reporting-service)
