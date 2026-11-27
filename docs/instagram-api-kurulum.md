# Instagram resmi API kurulumu (isteğe bağlı)

myfashion, bir **İşletme ya da İçerik Üreticisi (Creator)** hesabının post ve reels'lerini Instagram'ın resmi Graph API'si üzerinden, **Business Discovery** ile alabilir. Resmi ve belgelenmiş bir yol olduğu için hiçbir hesaba giriş yapılmaz ve kazıma (scraping) yapılmaz.

**Sınırlar:**
- Öne çıkanlar (highlights) API ile alınamaz; onlar için ekran kaydı kullanılır.
- Kişisel hesaplar ve yaş sınırlı hesaplar okunamaz.
- Telif hakkı olan müzik içeren videolarda ve sahibi indirmeyi kapattıysa reels'lerde video verilmez. Bu durumda program kapak görselini kullanır.

Bu adımlar 27 Kasım 2026'da Meta'nın şu belgelerine göre yazıldı: [Business Discovery](https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-facebook-login/business-discovery), [IG User business_discovery referansı](https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-user/business_discovery), [Başlangıç](https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-facebook-login/get-started) ve [Genel bakış](https://developers.facebook.com/documentation/instagram-platform/overview). Meta arayüzü ya da kuralları değiştirirse önce bu sayfalara bak.

## 1. Kendi profesyonel hesabını hazırla

1. Instagram'da (ana hesabın ya da yedek hesabın) **Ayarlar → Hesap türü ve araçlar → Profesyonel hesaba geç** yolunu izle ve **İçerik Üreticisi** ya da **İşletme** seç.
2. Bu hesabı bir **Facebook Sayfası**'na bağla. Sayfanın yoksa Facebook'ta ücretsiz bir sayfa oluştur. Facebook hesabının bu sayfada yetkisi olmalı.

## 2. Meta uygulaması oluştur

1. [developers.facebook.com](https://developers.facebook.com) adresinde geliştirici hesabı aç ve **Uygulama oluştur** ile yeni bir uygulama kaydet.
2. Uygulamaya **Facebook Login (for Business)** ürününü ekle.
3. Uygulama yalnızca senin kullanımın için olduğundan **Standard Access** yeterli olmalı. Meta'nın genel bakış sayfası, sadece uygulamada rolü olan kişilerin kullandığı uygulamalar için Standard Access'i öneriyor. App Review ve işletme doğrulaması, uygulamayı başkaları kullanacaksa gerekir. Meta, Standard Access'te bazı özelliklerin düzgün çalışmayabileceğini de belirtiyor; Business Discovery'yi ilk denemede doğrulayacağız.

## 3. Erişim anahtarı (access token) al

1. [Graph API Explorer](https://developers.facebook.com/tools/explorer)'ı aç ve kendi uygulamanı seç.
2. Şu izinleri ekleyip **Generate Access Token** ile anahtar üret:
   - `instagram_basic`
   - `instagram_manage_insights`
   - `pages_read_engagement`
   - `pages_show_list`
   - Sayfa rolün Business Manager üzerinden geldiyse ek olarak `ads_read`
3. Bu anahtar **1 saat** geçerlidir. Uzun süreli anahtar 60 gün geçerlidir ve süresi bitmeden yenilenebilir. Nasıl alınacağı Meta'nın "Long-lived access tokens" sayfasında anlatılıyor; uzun süreli anahtar kullanmanı öneririm.

## 4. Kendi Instagram hesap kimliğini bul

Graph API Explorer'da sırayla:

1. `GET me/accounts`: Sayfalarını listeler. Hesabını bağladığın sayfanın `id` değerini al.
2. `GET <sayfa-id>?fields=instagram_business_account`: Dönen `instagram_business_account.id` (genelde `1784…` ile başlayan uzun bir sayı) senin **Instagram profesyonel hesap kimliğin**.

## 5. myfashion'a gir

1. Uygulamada **Ayarlar → Instagram bağlantısı** bölümüne kimliği ve erişim anahtarını gir, **Kaydet ve test et**'e bas. "Bağlandı: @hesabın" yazısını görmelisin.
2. **Analizler** sayfasında hesap linkini gir ve **Post ve reels: Instagram API** kutusunu işaretle. Öne çıkanlar için klasörü de seçebilirsin.

## Güvenlik notları

- Erişim anahtarı bilgisayarında **şifreli** (ASP.NET Core Data Protection) ve yalnızca senin okuyabileceğin (`600`) bir dosyada saklanır. Arayüzde bir daha gösterilmez, loglara yazılmaz ve isteklerde URL'de değil `Authorization` başlığında gönderilir.
- Medya yalnızca Instagram/Facebook CDN adreslerinden, https üzerinden, yönlendirme izlenmeden ve dosya başına en fazla 50 MB olarak indirilir.
- Anahtarını kimseyle paylaşma. Sızdığını düşünürsen Meta uygulama panelinden geçersiz kıl ve yenisini al.

## Sorun giderme

| Mesaj | Anlamı |
|-------|--------|
| "İşletme/İçerik Üreticisi hesabı değil" | Hedef hesap kişisel ya da yaş sınırlı. Ekran kaydı yolunu kullan. |
| "Erişim anahtarı geçersiz ya da süresi dolmuş" | Yeni anahtar al (adım 3) ve Ayarlar'dan tekrar gir. |
| "Kullanım sınırına ulaşıldı" | Meta'nın platform hız sınırı. Bir süre bekle; o ana kadar alınan medya analiz edilir. |
| "İzni yok" | Adım 3'teki izinlerden biri eksik. |
| İstekler "Cannot parse access token" ile reddediliyor | Graph API'nin bu uç noktası `Authorization: Bearer` başlığını kabul etmiyor olabilir. Bu sorunu bildir; anahtarı URL'ye taşımak güvenlik açısından son çare. |
