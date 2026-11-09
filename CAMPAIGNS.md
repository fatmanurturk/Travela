# Kuponlar ve kampanyalar

Firma paneli: `/company/campaigns`. Yönetici paneli: `/admin/campaigns`.
Yolcu kampanyaları: `/campaigns`; ana sayfa ve üst menüden erişilir.

## Kullanım

- Firma yalnızca kendi kampanyalarını oluşturur/düzenler; yönetici firma seçebilir. Firma sonradan değiştirilemez.
- Kupon kodu girilirse kodlu kampanya oluşur. Kod boşsa uygun sepete otomatik indirim uygulanır. Kodlar firma içinde benzersizdir; 3–32 Latin harfi/rakam/tire/alt çizgiden oluşur ve büyük harfe normalleştirilir.
- Yüzde (%0,01–99) veya sipariş başına sabit TL indirimi seçilir. Minimum sepet, indirim tavanı, toplam sipariş limiti ve hesap başına sipariş limiti ayarlanabilir.
- Satın alma başlangıç/bitişi, isteğe bağlı güzergâh ve kalkış tarih aralığı belirlenir. Yönetim formundaki saatler Türkiye saatidir; veritabanında UTC saklanır. Başlangıç dahil, bitiş hariçtir.
- Kampanya duraklatılabilir ve herkese açık listeden gizlenebilir. Gizli kupon kodunu bilen yolcu koşulları sağlarsa kullanabilir. Gizli otomatik kampanya da uygun sepete uygulanır. Kayıt silinmez; geçmiş siparişlerle ilişkisi korunur.
- Yolcu koltuklarını seçip sefer detayındaki kupon alanına kodu girer. Kod yoksa kullanılabilir otomatik kampanyalar arasından en yüksek indirim seçilir; eşitlikte önce oluşturulan kazanır. Kod girilirse yalnızca o kod değerlendirilir. İndirimler birleştirilmez.
- Fiyat sunucuda hesaplanır. Önizleme kontenjan ayırmaz. Sipariş oluşturulurken koşullar ve kullanım limitleri tekrar doğrulanır; gösterilen toplam değişmişse yolcunun güncel fiyatı tekrar görmesi istenir.
- Sipariş oluşturulduktan ve sağlayıcı ödeme tutarı belirlendikten sonra kupon değiştirilemez. Devam eden eski tek-bilet ödeme sayfalarının fiyatı korunur; kampanyalar yeni çoklu-yolcu satın alma akışında kullanılır (tek yolcu da bu akıştadır).

## Ödeme, limitler ve iadeler

İndirim öncesi tutar, indirim, kampanya adı ve kupon kodu sipariş ve her bilette saklanır. Kampanyanın sonraki değişiklikleri geçmiş fiyatları değiştirmez. Gerçek ödeme sağlayıcısına indirimli sipariş ve satır tutarları gönderilir; doğrulama da aynı tutarlar üzerinden yapılır. Mevcut sağlayıcıların geliştirme/sandbox sınırları korunur.

İndirim koltuk numarasına göre kuruş cinsinden eşit paylaştırılır; artan kuruşlar ilk koltuklara dağıtılır. Satırların toplamı sipariş tutarına tam eşittir. Ödeme sağlayıcısı pozitif satır tutarı gerektirdiğinden indirim, her bilette en az 0,01 TL kalacak şekilde sınırlandırılır. Sabit indirim yolcu başına tekrar uygulanmaz.

Kullanım bir sipariş için sayılır. Geçerli bekleyen ödeme tutmaları kontenjanı geçici ayırır. Başarısız veya süresi dolan tutmalar kullanım hakkı bırakır. Ödemesi doğrulanıp bileti düzenlenen sipariş kullanımı kalıcıdır; kısmi/tam iptal hakkı geri yüklemez. Süresi geçmiş ödemede bilet düzenlenemeyip telafi iadesi açılırsa kullanım kalıcı sayılmaz. Bir biletin iadesi yalnızca o bilet için gerçekten ödenen indirimli tutardır.

Satın alma aynı serializable transaction içinde önce seferi, sonra kampanyaları ID sırasıyla `UPDLOCK, HOLDLOCK` ile kilitler. Kullanım sayımı ve sipariş kaydı aynı transaction içindedir; farklı seferlerden eş zamanlı gelen istekler kampanya limitini bu kilit üzerinden paylaşır. İşlem anahtarı tekrarları yeni kullanım oluşturmaz. Yönetim güncellemeleri rowversion ile çakışma kontrolü yapar.

Yönetim ekranı tamamlanan kullanımı, geçerli ödeme tutmalarını ve toplam indirimi gösterir. Kampanya bazında son 50 siparişin tutar/indirim/ödeme/iade geçmişi görüntülenebilir. Genel finans raporları mevcut `PaymentOrder.Amount` üzerinden net tahsilatı hesaplamayı sürdürür.

## Veritabanı geçişi

Migration: `20261105131137_AddCampaignDiscounts`.

Yeni `Campaigns` tablosu ve sipariş/bilet indirim alanları eklenir. Mevcut kayıtların yalnızca yeni `OriginalAmount` alanı mevcut `Amount` değerinden doldurulur. Önceki ödeme, iade, koltuk veya yolcu alanları değiştirilmez. Kampanya/indirim geçmişi varsa veri kaybettiren geri migration engellenir.

```powershell
dotnet ef database update AddCampaignDiscounts --project backend/Travela.API.csproj
```

Migration aracı API'yi ve gönderim worker'larını başlatmaz. Şema güncellendikten sonra backend yeni kodla yeniden başlatılmalıdır.

### Geliştirmede kampanya endpoint'leri 404 dönüyorsa

Frontend geliştirmede `/api/campaigns` ve `/api/campaigns/options` için GET kullanır. `VITE_API_BASE_URL` tanımlı değilse istekler Vite üzerinden `http://localhost:5062` adresine iletilir. Controller `Program.cs` içindeki mevcut `AddControllers`/`MapControllers` kaydıyla otomatik bulunur.

Profil çalışırken iki kampanya endpoint'i de 404 dönüyorsa, doğrudan backend yanıtını ve `/swagger/v1/swagger.json` içindeki route'ları kontrol edin. Başka bir configuration'a yapılan derleme çalışan Debug sürecini güncellemez; eski backend'i durdurup güncel kodla yeniden başlatın. Kimlik doğrulamasız yönetim çağrısının 401 dönmesi beklenir. Proxy adresini veya yetkilendirmeyi bu durumda değiştirmeyin.

React StrictMode geliştirmede effect'leri yeniden çalıştırır; temizlikte eski istek çiftinin `AbortController` ile iptal edilmesi normaldir. Gerçek yükleme hatasında sayfa tablo ve istatistik göstermez; `Tekrar dene` her iki kaynağı yeniden yükler. Boş kampanya mesajı yalnızca iki çağrı da başarıyla tamamlandığında gösterilir.

## API

- `GET /api/campaigns/public?tripId=...`: yayınlanan ve tarih/kontenjan koşullarını sağlayan kampanyalar; yönetim istatistikleri/versiyon bilgisi yayınlanmaz.
- `POST /api/campaigns/quote`: yolcu için `{ tripId, passengerCount, couponCode }` ile fiyat önizlemesi.
- `GET /api/campaigns`, `GET /api/campaigns/options`: firma/yönetici yönetim listesi ve seçimleri.
- `POST /api/campaigns`, `PUT /api/campaigns/{id}`: oluşturma/güncelleme. Güncellemede listeden alınan `version` gönderilir.
- `GET /api/campaigns/{id}/usage`: yetkili firma/yönetici için son 50 sipariş.
- `POST /api/purchases`: mevcut yolcu bilgilerine ek olarak `couponCode` ve isteğe bağlı `expectedAmount` kabul eder. Önceki kuponsuz işlem anahtarlarıyla uyumludur.

Bu geliştirmede test paketleri çalıştırılmadı; derleme, lint ve migration SQL üretimi kullanıldı. Gerçek ödeme veya mesaj gönderimi başlatılmadı.
