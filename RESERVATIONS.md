# Eski ücretsiz rezervasyon kayıtları

Bağımsız yeni rezervasyon oluşturma kapatıldı. Güncel satın alma, test ödeme, bilet, iptal/iade ve çalıştırma belgeleri [PAYMENTS.md](PAYMENTS.md) dosyasındadır.

Önceki rezervasyonlar `Reservations` tablosunda `Kind=Legacy` olarak korunur. Ödenmiş bilet sayılmaz; bunlara otomatik PNR veya ödeme kaydı eklenmez. Biletlerim ekranındaki **Eski ücretsiz kayıtlar** bölümünde görüntülenir. Gelecekteki aktif ücretsiz kayıt sahibi tarafından serbest bırakılabilir; bu işlemde ücret/iade yoktur. Yeni bilet alımı için ödeme akışı ayrıca tamamlanmalıdır.

Eski migration geçmişi korunur: `AddSeatReservations` rezervasyon tablosunu; `AddReservationCancellation` kayıt silmeden iptal durumu ve filtreli aktif-koltuk indeksini eklemiştir. Yeni `AddTicketPayments` ve `HardenPaymentInvariants` migration'ları bu mevcut yapıyı veri silmeden genişletir. Veritabanı sıfırlanmaz.

`/reservations` adresi artık `/tickets` adresine yönlenir. Eski rezervasyon oluşturma POST isteği `410` döner; eski kayıtları listeleme ve sahiplik kontrollü serbest bırakma işlemleri yalnızca geçmiş ücretsiz kayıtlar içindir.
