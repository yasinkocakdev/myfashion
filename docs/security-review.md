# Güvenlik denetimi

- **Tarih:** 28 Kasım 2026
- **Kapsam:** `improve/mvp` dalındaki bütün kod; Instagram API kaynağı dahil.
- **Ölçüt:** OWASP Top 10:2025 ve OWASP Top 10 for LLM Applications (2025).
- **Yöntem:** Her bulgu için girdinin gerçekten saldırganın kontrolünde olup olmadığına ve tehlikeli noktaya ulaşıp ulaşamadığına bakıldı. Yalnızca desene uyan ama ulaşılamayan kod bulgu sayılmadı.

## Tehdit modeli

myfashion **tek kullanıcılı, yerel** bir uygulama: Yalnızca `127.0.0.1:5080`'de dinler, kullanıcı hesabı ya da giriş sistemi yoktur.

Güvenilmeyen girdiler (saldırganın kontrol edebileceği şeyler):
1. Analiz edilen fotoğraf ve videolar: dosya adları, içerik, görseldeki yazılar.
2. Instagram açıklamaları (caption) ve Graph API yanıtları: medya adresleri, sayfa işaretleri, hata metinleri.
3. Yapay zekâ modelinin çıktısı.
4. Kullanıcının tarayıcısında açık olan **başka siteler**: localhost'a istek atmayı deneyebilirler.
5. Aynı bilgisayardaki başka kullanıcılar ve süreçler.

## Bulgular ve düzeltmeler

| # | Önem | Bulgu | Yol | Durum |
|---|------|-------|-----|-------|
| 1 | Orta | **Siteler arası WebSocket ele geçirme / CSRF (A01).** Blazor Server, SignalR WebSocket'i üzerinden çalışır. WebSocket'ler CORS'a tabi değildir ve SignalR varsayılan olarak `Origin` kontrol etmez. Tarayıcıda açık kötü niyetli bir sayfa `ws://127.0.0.1:5080/_blazor`'a bağlanmayı deneyebilir. Sömürmek zor, çünkü devre (circuit) tanımlayıcısı sayfadan okunamaz; yine de kapatılması ucuz. | Tarayıcıdaki başka site → `/_blazor` | **Düzeltildi:** `SameOriginGuard`, `Origin` başlığı farklı bir siteyi gösteren her isteği 403 ile reddeder. Test: `SameOriginGuardTests`. |
| 2 | Orta | **Hatanın yanlış işlenmesi (A10).** API kaynağında tek bir indirme hatası (ağ kopması, CDN hatası) bütün analizi düşürüyordu. | CDN yanıtı → `InstagramApiMediaSource` | **Düzeltildi:** O dosya atlanır, analiz devam eder. Test: `A_failed_download_skips_that_file_and_keeps_the_rest`. |
| 3 | Orta | **Kaynak tükenmesi (A06).** Tasarımdaki "boş disk alanı kontrolü" kodda yoktu. Büyük bir hesap ya da klasör diski doldurabilirdi. | Klasör / API → ffmpeg çıktısı | **Düzeltildi:** Her dosyadan önce boş alan kontrol edilir; 2 GB'ın altında iş durur ve anlaşılır bir mesaj verir. Test: `Stops_with_a_clear_message_when_the_disk_is_almost_full`. |
| 4 | Düşük | **Sınırsız tüketim (LLM10).** Model takılırsa zaman aşımına (3 dk) kadar token üretebilirdi. | Model çıktısı | **Düzeltildi:** `num_predict` analiz için 1024, öneriler için 3072 token. |
| 5 | Düşük | **Yanlış bilgi (LLM09).** Kullanıcı yapay zekâ çıktısını kesin bilgi sanabilir. | Rapor | **Düzeltildi:** Rapora "tahmindir, yanılabilir" notu eklendi. Kanıtsız markalar zaten gösterilmiyordu. |

## OWASP Top 10:2025: madde madde

| Madde | Durum ve kanıt |
|-------|----------------|
| **A01 Bozuk erişim kontrolü** | Giriş sistemi yok; erişim sınırı ağ seviyesinde: Kestrel yalnızca `127.0.0.1`'e bağlanır (`appsettings.json`). `AllowedHosts` başka host adlarını reddeder (DNS rebinding'e karşı; `Host: evil.example` → 400). `SameOriginGuard` siteler arası istekleri keser. `/media` uç noktası yalnızca veri klasöründeki `.jpg` dosyalarını sunar ve `PathGuard`'dan geçer (`..%2F..` → 404; `PathGuardTests`). Veri klasörü `700` izinlidir. |
| **A02 Yanlış yapılandırma** | Güvenlik başlıkları: CSP (`script-src 'self'`, inline script yok), `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy`. Hata sayfası istisna ayrıntısı göstermez. Ollama istemcisi localhost dışındaki adresleri reddeder (`Remote_ollama_address_is_rejected`). |
| **A03 Tedarik zinciri** | Paket sürümleri merkezi olarak sabitlenmiş (`Directory.Packages.props`). `NuGetAudit` açık ve uyarılar hata sayıldığı için açığı bilinen bir paket build'i kırar; bu kural CI'da da çalışır. Ön yüzde CDN script'i yok. Harici araçlar (ffmpeg, Ollama) Homebrew'dan gelir; güncel tutulmaları kullanıcının sorumluluğunda (README). |
| **A04 Kriptografik hatalar** | Instagram erişim anahtarı ASP.NET Core Data Protection (AES-256-CBC + HMACSHA256) ile şifreli ve `600` izinli bir dosyada. Instagram ile iletişim yalnızca https üzerinden. |
| **A05 Enjeksiyon** | **Komut:** ffmpeg ve sips shell olmadan, `ArgumentList` ile çağrılır (`Arguments_are_passed_literally_without_a_shell`). **SQL:** yalnızca EF Core LINQ; ham SQL yok. **XSS:** Blazor her çıktıyı kodlar, `MarkupString` kullanılmaz (`ProfileReport_escapes_untrusted_text`); CSS değerleri sabit paletten gelir. **URL:** Graph API alanı yalnızca doğrulanmış kullanıcı adıyla ve kodlanarak kurulur; mağaza linkleri izin listesindeki şablonlardan üretilir. **Yol:** dosya adları `PathGuard` ya da yalnızca rakamdan oluşan kimliklerle sınırlı. |
| **A06 Güvensiz tasarım** | İş kuyruğu en fazla 10 bekleyen işle sınırlı. Video başına en fazla 120 kare, analiz başına en fazla 200 API medyası. İndirme en fazla 50 MB, süreç çıktısı en fazla 64 MB. Her harici çağrıda zaman aşımı var. Disk kontrolü (bulgu 3). |
| **A07 Kimlik doğrulama hataları** | Uygulamada kullanıcı girişi yok (tasarım gereği yerel). Instagram anahtarı arayüzde bir daha gösterilmez; `ToString()` anahtarı gizler (`Credentials_never_print_the_token`). |
| **A08 Yazılım/veri bütünlüğü** | Güvenilmeyen veri yalnızca tipli kayıtlara JSON olarak açılır; tip bilgisi taşıyan serileştirme yok. Data Protection dosyası değiştirilirse "bağlı değil" sayılır (`Clear_disconnects_and_a_tampered_file_counts_as_not_connected`). |
| **A09 Loglama** | İş hataları yapılandırılmış logla kaydedilir. Erişim anahtarı URL'de olmadığı için HttpClient logları anahtarı içermez. Yerel uygulama için merkezi log ya da alarm gerekmiyor. |
| **A10 İstisnaların yanlış işlenmesi** | Bozuk dosya ya da başarısız analiz yalnızca o öğeyi etkiler; üst üste 5 hata işi durdurur. İptal edilen iş tutarlı bir durumda kaydedilir. İndirme hatası artık işi düşürmüyor (bulgu 2). |

## OWASP LLM Top 10 (2025)

| Madde | Durum |
|-------|-------|
| **LLM01 Prompt injection** | Açıklama "güvenilmeyen veri, talimat değil" etiketiyle ayraçlar arasına konur; `<`, `>` ve kontrol karakterleri silinir, uzunluk 500 karakterle sınırlanır. Model JSON şemasıyla kısıtlanır. Modelin hiçbir aracı yok (LLM06), bu yüzden yönlendirilse bile yapabileceği tek şey yanlış bir JSON yazmaktır. |
| **LLM05 Çıktının yanlış işlenmesi** | `ModelOutputParser` ve `RecommendationParser` her alanı doğrular: enum, uzunluk, aralık, kanıtsız marka. Model çıktısı hiçbir zaman komut, SQL, HTML ya da link olarak kullanılmaz. |
| **LLM06 Aşırı yetki** | Modele araç ya da yetki verilmez; sadece metin üretir. |
| **LLM07 Sistem prompt'u sızıntısı** | Prompt'larda gizli bilgi yok. |
| **LLM10 Sınırsız tüketim** | Zaman aşımı, tek yeniden deneme, ardışık hata eşiği ve `num_predict` sınırı (bulgu 4). |

## Bağımlılık taraması

```
dotnet list package --vulnerable --include-transitive
```

Sonuç aşağıya eklenecek.

## Kalan riskler (bilinçli kararlar)

1. **Klasör alanı, kullanıcının okuyabildiği her klasörü okutabilir.** Uygulama tek kullanıcılı ve yalnızca localhost'ta çalıştığı için bu bir özellik. Aynı Mac'te başka bir kullanıcı `127.0.0.1:5080`'e erişebilirse, senin adına çalışan uygulamayla senin görsellerini analiz ettirip görebilir. Tek kullanıcılı bir MacBook için risk düşük. Paylaşılan bir bilgisayarda uygulamayı açık bırakma.
2. **ffmpeg güvenilmeyen medyayı çözer.** ffmpeg'te geçmişte görülmüş bellek hataları bu yoldan tetiklenebilir. Önlemler: ffmpeg güncel tutulur (`brew upgrade ffmpeg`), zaman aşımı ve çıktı sınırı var. Sandbox (izole çalıştırma) bir sonraki adım olabilir.
3. **Data Protection anahtarları diskte şifresiz,** `700` izinli `keys` klasöründe duruyor. Senin kullanıcınla çalışan kötü amaçlı bir yazılım bunları okuyabilir; bu durumda zaten bütün dosyalarına erişebilir. macOS Anahtar Zinciri ile koruma daha ileri bir adım olabilir.
4. **Uygulama `dotnet run` ile Development ortamında açılır.** Bu ortamda hata ayrıntıları yalnızca yerel tarayıcıda görünür. README, günlük kullanım için Production ortamını önerir.
5. **Analiz edilen kişilerin fotoğrafları yerelde saklanır.** Arayüzde henüz bir hesabın verilerini silme düğmesi yok; şimdilik silmek için veri klasörü elle silinebilir. Sonraki iş olarak önerildi.
