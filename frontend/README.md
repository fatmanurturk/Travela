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
