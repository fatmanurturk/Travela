# Yolculuk bildirimleri ve kontrollü sefer değişiklikleri

## Bildirimler

Satın alma onayı/PNR, kalkıştan önceki 24 saat içinde hatırlatma, sefer iptali, doğrulanmış iade sonucu ve kontrollü sefer değişikliği için kalıcı bildirim kaydı oluşturulur. Kayıt ödeme/sefer işlemiyle aynı veritabanı transaction'ında yazılır. Yinelenen sağlayıcı bildirimleri aynı olay/rezervasyon/kanal kaydını çoğaltmaz.

Profilde e-posta ve SMS kanalları ayrı seçilir. E-posta varsayılan açık, SMS kapalıdır; mevcut hatırlatma, iptal ve iade tercihleri korunur. Bilet onayı ve sefer değişikliği seçili kanallara gider; hatırlatma/iptal/iade ayrıca ilgili tür tercihine bağlıdır. İletişim bilgisi bilet alınırken kaydedilen yolcu bilgisinden okunur. Kapalı tercih veya eksik alıcı `Skipped` olarak gerekçesiyle izlenir. Kuyruktaki tercih ve hatırlatmanın güncelliği gönderimden önce yeniden kontrol edilir.

Yerel geliştirme ayarları:

- E-postalar hesap işlemleriyle aynı `AccountEmail` ayarlarını ve `EmailTransport` taşıyıcısını kullanır. `tmp/account-mail` altında `.eml` ve `.html` oluşturulur.
- SMS mesajları `TravelSms:Mode=Pickup` ile `tmp/travel-sms` altında `.txt` oluşturur.
- Bunlar `Preview` durumundadır; gerçek teslimat olarak gösterilmez. Yerel dosyalar iletişim bilgisi ve bilet ayrıntısı içerir; kaynak kontrolüne eklemeyin.

SMTP ile gerçek e-posta için `AccountEmail:DeliveryMode=Smtp`, `SmtpHost`, `SmtpPort`, `SmtpUserName`, `SmtpPassword`, `FromAddress` alanlarını ortam değişkenleri veya user-secrets üzerinden ayarlayın. TLS kullanılır. SMTP kabulü `Accepted` olarak izlenir; gelen kutusu teslimatı iddiasında bulunulmaz.

Gerçek SMS için `TravelSms:Mode=Twilio`, `AccountSid`, `AuthToken` ve uluslararası biçimde `From` gerekir. Hedef numara da `+ülkeKodu...` biçiminde olmalıdır. Oluşturulan mesajın SID'si saklanır; teslimat durumu sağlayıcıdan sorgulanır. `queued`/`sent` teslimat sayılmaz; `delivered` yanıtıyla `Delivered` olur. API sözleşmesi: [Twilio Message resource](https://www.twilio.com/docs/messaging/api/message-resource). Kimlik bilgilerini repoya yazmayın. Testler sahte HTTP taşıyıcısıyla çalışır; gerçek SMS göndermez.

Arka plan çalışanı başlangıçta hemen, iş bulunduğunda 15 saniye, kuyruk boşken 30 saniye, döngü hatasında 30 saniye bekleyerek kuyruğu işler. Hatırlatma taraması başlangıçta ve ardından en az 60 saniye arayla yapılır. Bu süreler `Notifications:Worker` altındaki `PollIntervalSeconds`, `IdleIntervalSeconds`, `ErrorIntervalSeconds`, `ReminderIntervalSeconds` ayarlarıyla değiştirilebilir; beklemeler işlem tamamlandıktan sonra başlar (`Notifications:WorkerEnabled=false` ile testlerde durdurulabilir). Geçici hatalar 8 denemeye kadar artan beklemeyle tekrar edilir. Deneme geçmişi kalır. Sağlayıcıya gönderim sonucu kaybolmuş SMS otomatik tekrar edilmez; `Unknown` olarak işaretlenir. Çalışan gönderim sırasında kesilirse süreli kilit sonunda aynı belirsizlik kontrolü uygulanır. SMS sağlayıcı kabulünden sonra teslimat dakikada bir, en fazla 2880 sorguyla izlenir. SMTP ve harici SMS hizmetleriyle uygulama veritabanı arasında dağıtık transaction yoktur; mutlak tek teslim garantisi verilmez.

Yönetici `/admin/notifications`, firma `/company/notifications` üzerinden PNR/alıcı/firma, durum ve kanalla arar. Firma yalnızca kendi kayıtlarını görür. Yönetici başarısız bildirimi en az 10 karakterlik gerekçeyle tekrar kuyruğa alabilir. `Unknown` durumunda önce sağlayıcıyı kontrol ettiğini ve olası çift gönderimi ayrıca onaylar. Gerekçe, yönetici kimliği ve deneme geçmişi saklanır.

## Sefer değişiklikleri

Normal sefer düzenlemesinde yalnızca aktif bilet, aktif eski ücretsiz rezervasyon ve süresi dolmamış aktif ödeme tutması engeldir. Süresi dolmuş tutma serbest bırakılır. İptal edilmiş kayıtlar engel değildir. İşlem geçmişi bulunan sefer başka firmaya aktarılamaz.

Aktif yolcular varsa sefer detayındaki **Kontrollü saat / araç değişikliği** kullanılır:

1. Yeni aktif araç, Türkiye saatinde kalkış/varış ve en az 10 karakter gerekçe girilir.
2. Önizleme eski/yeni araç ve saatleri, etkilenen biletleri, sonlandırılacak ödeme tutmalarını ve tam iade son zamanını gösterir. Henüz veri değiştirmez.
3. Uygulamada sefer sürümü ve etkilenen bilet sayısı yeniden doğrulanır. Araç çakışması, firmanın araç sahipliği ve mevcut koltukların yeni planda aynı düzen/numarayla satışa açık olması denetlenir. Uyuşmayan araç değişikliği reddedilir; otomatik rastgele koltuk atanmaz.
4. Aktif ödeme tutmaları kapatılır. Geç başarılı ödeme yeni bilet üretmez, mevcut telafi iadesi akışına girer. Aktif ödenmiş biletler aynı koltukta kalır; yeni yolculuk bilgisi kaydedilir.
5. Değişiklik geçmişi, işlemi yapan kullanıcı ve etkilenen biletler saklanır. Yolculara seçili kanallarda bildirim kuyruğa alınır. Her etkilenen yolcu yeni kalkış anına kadar, normal iki saat kuralından bağımsız tam iade isteyebilir. Para iadesi yine sağlayıcı sonucuyla tamamlanır.

Güzergâh, firma, terminal ve fiyat bu kontrollü işlemde değiştirilemez. Başka yerleşime geçiş/koltuk yeniden atama bu sürüme dahil değildir. Önizleme onayıyla aynı işlem anahtarını tekrar göndermek işlemi çoğaltmaz.

Biletin yolculuk kopyası aktif bilette kontrollü değişiklikle güncellenir. İptal edilmiş bilet kopyası korunur; aynı seferin sonraki genel düzenlemesi eski bileti değiştirmez. Yönetici/firma bilet listeleri, yolcu bileti ve PDF bu kopyayı kullanır. Yolcu bilet detayında kendi değişiklik ve bildirim geçmişini görebilir.

## Geçiş ve testler

Migration `20261104084810_AddTravelNotificationsAndTripChanges` mevcut tercihlere, PNR, ödeme/iade ve kontenjan kayıtlarına dokunmaz. Eski satın almalara onay e-postası göndermez. Çalışan açıldığında yaklaşan aktif seferler için tercihlere göre hatırlatma oluşturabilir.

```powershell
dotnet run --project tests/Travela.ReservationTests -- --migrate-notifications
dotnet run --project tests/Travela.ReservationTests
dotnet run --project tests/Travela.ProfileTests
dotnet run --project tests/Travela.ReportingTests
cd frontend
npm.cmd test
npm.cmd run test:browser -- notification-changes.spec.mjs
```

Geçiş yardımcısı yalnızca bu migration bekliyorsa ilerler ve mevcut iş verilerini karşılaştırır. Bildirim/değişiklik geçmişi, bilet yolculuk kopyası veya kanal tercih değişikliği varsa veri kaybettiren geri migration engellenir.
