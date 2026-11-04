# Sabit yolcu bilgileri ve merkezi bilet yönetimi

Yeni checkout, hesabın adını, e-posta adresini ve telefonunu `Reservations` üzerindeki `PassengerNameSnapshot`, `PassengerEmailSnapshot`, `PassengerPhoneSnapshot` alanlarına kaydeder. `PassengerCapturedAt` UTC zamanını, `PassengerSnapshotSource=Checkout` kaynağı belirtir. Tekrar kullanılan sipariş anahtarı aynı bilgileri korur. Yolcu sahibi `PassengerId` olarak kalır; bu sürüm başkası adına satın alma formu eklemez.

Ödeme ekranı, bilet detayı, PDF'nin yolcu adı, firma listesi ve yönetici bilet bilgileri bu kayıtlardan okunur; profil değişikliklerinde güncellenmez. Yolcu bilgisi eksikse güncel profilden sessizce alınmaz. Bilet sahipliği ve PDF erişimi aynı hesap kimliği kontrollerini korur.

## Eski kayıtların geçişi

`20261104073635_AddTicketPassengerSnapshotsAndPaymentHistory` mevcut yolcu bilgilerini geçiş anındaki kullanıcı kayıtlarından doldurur, kaynağı `Migration` olarak işaretler. Geçmiş satın alma anındaki bilgiler geri getirilemez; ekranlarda bu sınır açıklanır. Mevcut ödeme kayıtlarına `ImportedState` hareketi eklenir; bu geçmişte gerçekleştiği varsayılan bir olay değil, geçiş sırasında görülen durumdur.

Kullanıcılar, PNR'ler, bilet/ödeme/iade durumları, koltuklar ve tutarlar değiştirilmez. Sabit yolcu bilgisi veya ödeme hareketi varsa bunları silecek geri migration engellenir. Proje kökünden yalnızca bu migration'ı uygulamak ve eski alanları karşılaştırmak için:

```powershell
dotnet run --project tests/Travela.ReservationTests -c TicketOperationsVerification -- --migrate-ticket-operations
```

## Yönetici akışı

`/admin/tickets` yalnızca aktif Admin oturumuna açıktır. PNR, sipariş kimliği, kayıt numarası, sabit yolcu adı/iletişimi, güncel hesap adı/e-postası ve firma adı aranabilir. Firma, yolcu kimliği (API), ödeme/iade durumu, destek ihtiyacı ve Türkiye saatine göre kayıt tarihi filtreleri vardır. Sonuçlar sayfalıdır; eski ücretsiz kayıtlar da ayrı tür olarak görünür. Liste/detay yanıtları önbelleğe alınmaz.

Detay, bilet yolcusunu güncel hesap sahibinden ayırır; sağlayıcı referanslarını, iade denemelerini, hata bilgisini, mevcut takvimleri ve sayfalı işlem geçmişini gösterir. On saniyede bir yenilenir. Yönetici yolcuya özel PDF endpoint'ine taşınmaz; bu ekran bilet sahipliğini değiştirmez.

- `POST /api/admin/tickets/{reservationId}/retry-refund`: yalnızca ödenmiş, bekleyen/başarısız ve o anda lease ile işlenmeyen bir iade talebini yeniden kuyruğa alır. Deneme bütçesi sıfırlanır; orijinal sağlayıcı iade anahtarı ve tutarı korunur. Tamamlanmış iade yeniden açılmaz.
- `POST /api/admin/tickets/{reservationId}/reconcile`: yalnızca sonucu beklenen ödemeyi sağlayıcı sorgulamasına planlar; tahsilat veya PNR'yi yönetici isteğiyle başarılı işaretlemez.

Her iki istek `reason` (kırpılmış en az 10, en fazla 1000 karakter) ve boş olmayan `requestKey` ister. Aynı yöneticinin aynı anahtarla aynı isteği tekrar etmesi yeni işlem yaratmaz; farklı içerikle anahtar kullanımı reddedilir. Aynı sipariş/işlem için 30 saniyelik yeniden talep aralığı vardır. Başarılı yanıt kuyruğa alındığı anlamına gelir; sağlayıcı sonucu ayrıca beklenir.

İşlem, önce sefer satırını kilitleyen transaction içinde planlanır ve yönetici kimliği/adı, gerekçe, zaman ve durumlarla `PaymentEvents` tablosuna yazılır. Ödeme doğrulaması, bilet düzenleme, iade talebi ve iade denemesi/sonucu da hareket olarak kaydedilir. Önceki kayıtlardaki tarihsel hareketler uydurulmaz.

`PaymentRecoveryWorker` çalışıyor olmalıdır (`Payments:RecoveryEnabled` varsayılan açık). Bekleyen ödemelerin sonraki sorgulama zamanı saklanır; eski sonuçsuz ilk 100 kayıt diğer siparişleri sürekli engellemez. Sekiz sorgulama sonrasında destek ihtiyacı işaretlenir. Son iade denemesinde kesinti yaşanmış kayıtlar da lease süresi geçince destek takibine alınır. Bu sürüm hâlâ LocalTest kullanır; gerçek tahsilat/iade entegrasyonu eklemez.

## Doğrulama

```powershell
dotnet run --project tests/Travela.ReservationTests -c TicketOperationsVerification
dotnet run --project tests/Travela.ProfileTests -c TicketProfileVerification
dotnet run --project tests/Travela.ReportingTests -c TicketReportingVerification
cd frontend
npm.cmd run build
npm.cmd test
npm.cmd run test:browser -- admin-tickets.spec.mjs
```

SQL/HTTP testleri geçici test veritabanları kullanır. Yönetici araması, yetki sınırları, profil değişikliğine rağmen sabit bilet bilgileri, eş zamanlı işlem anahtarları ve tek iade transferi doğrulanır. Tarayıcı testleri arama, doğru işlem seçimi, ağ hatasında aynı anahtarla yeniden deneme, eski kayıtlar ve mobil görünümü kapsar.
