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
