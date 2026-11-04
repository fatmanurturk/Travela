# Koltuk planları ve terminal bilgileri

## Kullanım

1. Yönetici `/admin/terminals` ekranında otogar veya biniş noktasını şehir, ad ve açık adresle ekler. Terminal pasife alınabilir; kullanılan terminalin şehri değiştirilemez.
2. Firma `/company/buses` ekranında fiziksel kapasiteyi ve `2+1` / `2+2` düzenini seçer. Önizlemedeki koltuklara tıklayarak varsayılan satış blokajını belirler. Düzen solda iki, sağda bir veya iki koltuk ve arada koridor olacak biçimde oluşturulur; son sıra kapasiteye göre tamamlanır.
3. Firma sefer oluştururken güzergâhın şehirlerine uygun kalkış/varış terminalini seçer; peron, buluşma yeri veya erken gelme süresini biniş açıklamasına yazar. Toplu oluşturma ve kopyalama aynı bilgileri taşır.
4. Sefer detayındaki koltuk düzenleyicisi yalnızca o seferin boş koltuklarını satışa kapatıp açar. Satılmış veya ödeme için tutulmuş koltuklar kapatılamaz. Kalkışı gelmiş sefer düzenlenemez.
5. Yolcu sefer detayında aynı fiziksel planı, dolu ve kapalı koltukları görür. Terminal adresleri, harita bağlantıları ve biniş açıklaması sefer, ödeme ve bilet ekranlarında; adres ve açıklamalar bilet PDF'sinde bulunur.

## Veri ve satış kuralları

- Sefer oluşturulurken otobüs planı, fiziksel kapasite ve terminal ad/adresleri kopyalanır. Otobüs veya terminal kataloğunun sonraki değişiklikleri mevcut seferi ve alınmış bileti değiştirmez. Rezervasyon kaydı bulunan seferin genel bilgileri değiştirilemez.
- Satış kapasitesi fiziksel kapasiteden kapalı koltuklar çıkarılarak hesaplanır. Ödeme tutması, başarısız ödeme, süre aşımı, iptal ve raporlama aynı satış kapasitesini kullanır. Koltuk blokajı ve satın alma aynı sefer üzerinde işlem kilidi alır.
- Pasif terminale bağlı sefer için yeni satın alma açılmaz. Bu sırada gelen geç başarılı ödeme, mevcut telafi iadesi akışıyla ele alınır; mevcut bilet adresleri korunur.
- Harita adresleri saklanan açık adresten Google Maps arama bağlantısı olarak üretilir; kullanıcıdan serbest URL alınmaz.
- Mevcut veride fiziksel yerleşim bilinmediği için migration eski otobüs ve seferleri `Unspecified` olarak işaretler. Koltuk numarası/kontenjan korunur, sahte pencere/koridor veya terminal bilgisi eklenmez. Eski seferi düzenlemek için web formunda terminallerin seçilmesi gerekir. Eski istemciler için API her iki terminal kimliğinin de boş olmasına izin verir; tek taraflı veya yanlış şehir seçimini reddeder.
- Bu sürüm kalkış ve varış noktalarını kapsar. Ara duraklar arasında parça parça koltuk satışı ve yol üzeri yeniden kullanım uygulanmaz.

## Geçiş ve doğrulama

Migration: `20261104080859_AddSeatPlansAndTerminals`.

Yerel veritabanına kayıtları karşılaştırarak uygulamak:

```powershell
dotnet run --project tests/Travela.ReservationTests -- --migrate-journey
```

Yardımcı yalnızca bu migration bekliyorsa ilerler; hesap, yolcu, bilet, ödeme/iade, otobüs ve sefer verilerinin korunduğunu denetler. Gerçek plan veya terminal verisi varsa veri kaybettiren geri migration reddedilir.

Doğrulama: `Travela.ReservationTests` içindeki `JourneyChecks`, firma/yönetici yetkileri, 2+1/2+2 konumları, kapalı koltuk satın alma girişimi, aktif tutma blokajı, kontenjanın geri açılması, sefer anlık kopyaları, pasif terminal ve PDF çıktısını kapsar. Frontend `seat-plans.test.mjs` konum hesaplarını; `journey.spec.mjs` mobil/masaüstü yolcu seçimini ve firma/yönetici formlarını doğrular.
