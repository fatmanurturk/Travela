# Hesap erişimi ve şifre işlemleri

`IsActive` yalnızca yöneticinin hesabı açık/kapalı tutma kararıdır. `ActivationPending` ilk şifre kurulumu için aktivasyon bağlantısı beklendiğini, `PasswordResetRequired` zorunlu şifre sıfırlama beklendiğini, `MustChangePassword` geçici şifreyle ilk girişte yeni şifre seçilmesi gerektiğini belirtir. Yönetici listesi ve detayı bu alanları ayrı döndürür.

Şifresiz davet oluşturmak `ActivationPending=true` yapar; yönetici iznini kapatmaz. Şifre verilerek hesap oluşturulması da istekteki `IsActive=false` değerini değiştirmez. Yönetici tarafından şifre sıfırlama başlatılması `PasswordResetRequired=true` yapar, mevcut oturumları güvenlik sürümüyle iptal eder ve eski şifreyle girişi engeller. Kapalı hesaba sıfırlama başlatılmaz; önce yöneticinin hesabı açması gerekir.

`AccountAccess` sorguları normal girişte, ilk şifre değişiminde, sıfırlamada, JWT sonrası middleware'de ve controller kullanıcı sorgularında ortak kuralları uygular. Company hesabı aktif bir firmaya bağlı olmalıdır. Aktivasyon veya sıfırlama bekleyen hesap normal giriş yapamaz; ilk şifre değişimi gereken hesap yalnızca şifre değişimi yanıtı alır, normal oturum token'ı alamaz. Middleware bütün bu durumları mevcut oturumlarda da kontrol eder.

`reset-password` yalnızca süresi geçmemiş, kullanılmamış token hash'i ve güncel hesap durumuyla koşullu veritabanı güncellemesi yapar. Aynı token'ı eş zamanlı kullanan isteklerden yalnızca biri başarılı olur. İşlem aktivasyon ve şifre gereksinimlerini temizler; **IsActive değerini değiştirmez**. Hesabı kapatmak bekleyen bağlantıyı iptal eder; yeniden açmak aktivasyon veya şifre gereksinimini kaldırmaz. Firma kapatılırken firma kullanıcılarının güvenlik sürümleri aynı transaction içinde artırılır; firma yeniden açılsa bile önceki oturumlar kullanılamaz.

## Migration ve mevcut kayıtlar

`20261104064425_SeparateAccountAccessStates` yalnızca iki adet `bit NOT NULL DEFAULT 0` alanı ekler. Mevcut aktif/pasif kararları, şifreler, token'lar, güvenlik sürümleri ve ilk şifre değişimi gereksinimleri korunur. Eski `IsActive=false` kayıtlarının neden kapalı olduğu tahmin edilmez; hiçbiri otomatik açılmaz. Önceki sürümde davet veya sıfırlama nedeniyle pasif kalan hesaplar için yönetici hesabı inceleyerek açmalı ve gerektiğinde yeni sıfırlama bağlantısı üretmelidir. Yeni alanlarda bekleyen durum varken geri migration engellenir.

Proje kökünden mevcut veritabanına yalnızca bu migration'ı uygulamak ve önceki hesap alanlarını/kayıt sayılarını karşılaştırmak için:

```powershell
dotnet run --project tests/Travela.ProfileTests -c AccountStateVerification -- --migrate-account-state
```

Komut beklenmeyen başka migration varsa durur. Karşılaştırılan şifre/token hash'leri yalnızca bellekte tutulur, çıktıya veya dosyaya yazılmaz. Yeni API sürümünün kullanılabilmesi için çalışan backend yeniden başlatılmalıdır.

## Doğrulama

```powershell
dotnet run --project tests/Travela.ProfileTests -c AccountStateVerification
dotnet run --project tests/Travela.ReservationTests -c AccountPaymentVerification
dotnet run --project tests/Travela.ReportingTests -c AccountReportingVerification
cd frontend
npm.cmd run build
npm.cmd test
npm.cmd run test:browser -- account-states.spec.mjs company-profile.spec.mjs
```

SQL/HTTP testleri birbirinden ayrı geçici test veritabanları oluşturup temizler. Tarayıcı testleri sentetik API yanıtlarıyla yönetici durumlarını, filtrelemeyi, davet oluşturmayı ve firma profili regresyonunu doğrular.

## Aktivasyon ve şifre kurtarma

Yeni yolcu kaydı artık aktivasyon bekler. Kayıt sırasında seçilen şifre korunur; `/activate-account` sayfasındaki onayla e-posta doğrulanır. Yönetici şifresiz davet gönderdiğinde aynı sayfa yeni şifre ve tekrarını ister. Bağlantıyı yalnızca açmak veya kontrol etmek hesabı aktifleştirmez; kullanıcı onayı gerekir. Önceden kayıtlı aktif hesaplar yeniden aktivasyona zorlanmaz.

Giriş ekranındaki **Şifremi unuttum** bağlantısı `/forgot-password` formuna gider. `/reset-password` sayfası yeni şifreyi ve tekrarını doğrular; işlem tamamlanınca kullanıcı yeniden giriş yapar. Herkese açık sıfırlama talebi mevcut şifreyi/oturumları kapatmaz; bunlar ancak bağlantı başarıyla kullanıldığında geçersizleşir. Yöneticinin zorunlu sıfırlama işlemi önceki davranışını korur. `/resend-activation` süresi dolan veya gelmeyen aktivasyon bağlantısını yeniler.

Anonim istekler kayıtlı, bilinmeyen ve kapalı hesaplar için aynı genel mesajı döndürür. Hesap başına bir dakikalık gönderim aralığı DB üzerinde, IP başına dakikada 20 bağlantı isteği uygulama üzerinde sınırlandırılır. Yeni bağlantı eski token'ı geçersizleştirir. Aktivasyon bağlantısı 48 saat, herkese açık sıfırlama bağlantısı bir saat, yönetici sıfırlaması 24 saat geçerlidir. API yanıtlarında ham aktivasyon/sıfırlama token'ı gösterilmez.

Bağlantının token'ı URL fragment'inde (`#token=...`) taşınır; API'ye yalnızca POST gövdesinde gönderilir. Sayfa açıldıktan sonra adres çubuğundan temizlenir ve tarayıcı depolamasına yazılmaz. Sayfa yenilenirse e-postadaki bağlantı tekrar açılabilir. Şifre sıfırlama endpoint'i `token`, `newPassword`, `confirmPassword` alanlarını ister.

### Yerel e-posta önizlemesi

Seçilen geliştirme modu `AccountEmail:DeliveryMode=Pickup` değeridir. Kuyruk başlangıçta hemen işlenir; iş bulunduğunda iki saniye, boşken on saniye, döngü hatasında otuz saniye beklenir. Süreler `AccountEmail:Worker` altındaki `PollIntervalSeconds`, `IdleIntervalSeconds`, `ErrorIntervalSeconds` ayarlarıyla değiştirilebilir; beklemeler işlem tamamlandıktan sonra başlar. E-postalar `tmp/account-mail/` altında `.eml` ve `.html` olarak oluşur. HTML dosyasını tarayıcıda açıp bağlantıya tıklayın. Bunlar yerel dosyalardır; dışarıya e-posta gönderilmez ve statik API dosyaları olarak yayımlanmaz. Klasör git tarafından yok sayılır. Bağlantılar varsayılan olarak `http://localhost:5173` adresine gider; frontend farklı portta çalışıyorsa `AccountEmail:FrontendBaseUrl` ayarlanmalıdır.

`AccountEmailMessages` tablosu gönderim kuyruğudur. E-posta gövdesi ASP.NET Data Protection ile şifrelenir; kullanıcı tablosunda token'ın yalnızca hash'i saklanır. Hesap/token değişikliği ve kuyruk kaydı aynı transaction'da yazılır. Worker süreli lease ile işi alır; geçersizleşmiş veya kapalı hesaba ait bağlantıları göndermeden iptal eder. Teslim edilen/iptal edilen gövde kuyruktan temizlenir; hatalar artan beklemeyle sekiz kez denenir, sonra `Failed` durumunda saklanır. SMTP teslimi kesinti anında tekrar edebilir; bağlantılar tek kullanımlı kalır. Kuyrukta bekleyen mesajlar ve worker yeniden başlangıçları için Data Protection anahtarları korunmalıdır.

`Pickup` yalnızca Development/Testing ortamında kullanılabilir. Gerçek e-posta gönderimine geçerken `AccountEmail:DeliveryMode=Smtp`, `SmtpHost`, `SmtpPort` (varsayılan 587), `FromAddress` ve HTTPS `FrontendBaseUrl` ayarlanır. Gerekiyorsa `SmtpUserName` ve `SmtpPassword` user-secrets/ortam değişkenleriyle sağlanır; kaynak koda eklenmez. SMTP bağlantısı TLS kullanır. Mevcut gerçek ödeme sağlayıcısı gereksinimi bu geliştirmeyle değişmez.

### E-posta migration'ı ve testler

`20261104071454_AddAccountEmailOutbox` yalnızca yeni kuyruk tablosunu ekler; mevcut hesaplar veya biletler değiştirilmez. E-posta geçmişi varsa tabloyu silecek geri migration engellenir. Mevcut geliştirme veritabanına yalnızca bu migration'ı uygulayıp hesap durumlarını ve kayıt sayılarını karşılaştırmak için:

```powershell
dotnet run --project tests/Travela.ProfileTests -c AccountEmailVerification -- --migrate-account-email
```

```powershell
dotnet run --project tests/Travela.ProfileTests -c AccountEmailVerification
cd frontend
npm.cmd run test:browser -- account-recovery.spec.mjs account-states.spec.mjs
```

SQL testleri `tmp/account-email-qa/<test-veritabanı>/` altında yalnızca sentetik e-posta önizlemeleri üretir. Dışarıya e-posta gönderilmez.
