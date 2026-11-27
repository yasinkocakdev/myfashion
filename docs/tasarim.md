# myfashion: Tasarım (v3)

- **Durum:** Onaylandı, 2026-11-27.
- **v2'den farkı:** Proje öğrenme planına bağlı aylık katmanlar yerine şimdi tek seferde inşa ediliyor. Instagram'dan indirme, yedek bir hesapla ve güvenlik kurallarıyla geri geldi. Ad `myfashion` oldu.

## Amaç

Bir Instagram hesabının postlarındaki, reels'lerindeki ve öne çıkanlarındaki kıyafetleri analiz edip o kişinin stilini her yönüyle çıkarmak: hangi parçalar, hangi renkler, hangi markalar, hangi kombin formülleri. Sonra bunu kullanıcıya göre bir alışveriş listesine ve kombin önerilerine dönüştürmek.

- **Kullanıcı:** Tek kişi (projenin sahibi). Uygulama kendi bilgisayarında çalışır.
- **Başarı ölçütleri:** Bilinen güvenlik açığı yok (OWASP Top 10). Build uyarısız, testler yeşil, kritik akışlar test altında. Hız önce/sonra ölçülmüş. Arayüz tutarlı ve erişilebilir. README ile 10 dakikada kurulum.

## Kapsam

**Dahil:** Kıyafet, ayakkabı, çanta, aksesuar; renk, desen, kesim; görünen marka (logo, yazı, açıklamadaki etiket); stil etiketleri.

**Hariç:** Yüz tanıma, kimlik, konum; kişinin internette araştırılması; kullanıcının ana Instagram hesabının kullanılması.

## Girdi kaynakları

| Kaynak | Nasıl | Not |
|--------|-------|-----|
| Instagram (yedek hesap) | instaloader, kullanıcının bir kez giriş yaptığı **yedek hesabın** oturum dosyasıyla çalışır. Postlar, reels ve öne çıkanlar indirilir. | Ana hesap hiç kullanılmaz. Oturum dosyası repo dışında, `600` izinle tutulur. |
| Instagram (girişsiz) | instaloader oturumsuz, sadece herkese açık postlar. | Öne çıkanlar girişsiz indirilemez. |
| Manuel klasör | Ekran görüntüsü ve ekran kaydı. | Her zaman çalışan yedek yol. |

**İndirme kuralları:** Bot korumasını atlatma girişimi yok (proxy, sahte kimlik, hız sınırını aşma yok). İstekler sırayla, instaloader'ın kendi hız sınırlayıcısıyla gider. Giriş, doğrulama (checkpoint/challenge), 401 ya da 429 sinyali gelirse indirme durur ve kullanıcıya bildirilir. Instagram'ın kullanım koşulları otomatik toplamayı yasaklar; yedek hesap bu riski büyük ölçüde ana hesaptan ayırır ama ortadan kaldırmaz.

## Mimari

```
Instagram linki / klasör
      │
      ▼
IMediaSource ──► Ön işleme ──────────► IVisionAnalyzer ──► SQLite ──► StyleProfileBuilder ──► IRecommendationGenerator ──► Rapor (Blazor)
(instaloader,    (ffmpeg: kare çıkar,    (Ollama, qwen3-vl,    (EF Core)   (saf C#: palet,          (Ollama: alışveriş listesi,
 manuel klasör)   küçült, dHash ile       JSON şemalı çıktı,                parça, marka, formül)    kombinler; linkleri kod üretir)
                  benzerleri ele)         önbellek)
```

| Proje | Sorumluluk |
|-------|------------|
| `MyFashion.Core` | Modeller, arayüzler, saf iş mantığı (link ayrıştırma, özet motoru, mağaza linkleri, yol koruması) ve analiz akışını yöneten `AnalysisRunner`. Dış bağımlılığı yok. |
| `MyFashion.Infrastructure` | Dış dünyaya dokunan her şey: süreç çalıştırma, instaloader, ffmpeg, Ollama HTTP istemcisi, SQLite (EF Core). |
| `MyFashion.Web` | Blazor (Interactive Server) arayüzü, arka plan iş kuyruğu. Yalnızca `127.0.0.1` üzerinde dinler. |
| `MyFashion.Tests` | xUnit v3 ve bUnit testleri. |

## Teknoloji

- .NET 10 (LTS), C# 14, Nullable açık, uyarılar hata sayılır.
- Ollama + `qwen3-vl:8b`; kütüphanesiz, `HttpClient` ile `/api/chat`, JSON şemalı çıktı. Performans adımında `qwen3-vl:4b` ile karşılaştırılır.
- SQLite + EF Core 10. Sağlayıcı EF Core arkasında olduğu için ileride SQL Server ya da PostgreSQL'e geçiş bağlantı ayarı ve paket değişikliğidir.
- instaloader ve ffmpeg harici araç olarak, shell kullanılmadan `ProcessStartInfo.ArgumentList` ile çağrılır.

## Veri modeli (SQLite)

| Tablo | Alanlar (özet) |
|-------|----------------|
| `Accounts` | Id, Username, CreatedAt |
| `Runs` | Id, AccountId, Source, Status, StartedAt, FinishedAt, Error, sayaçlar |
| `MediaItems` | Id, RunId, Kind (Post/Reel/Highlight/Manual), RelativePath, Sha256, DHash, Caption, AnalysisJson, Model |
| `Garments` | Id, MediaItemId, Category, Type, Color, Pattern, Fit, Brand, BrandEvidence, Confidence |
| `UserProfiles` | Id, Gender, Sizes, Budget, Stores (kombin önerileri için) |

## Güvenlik ilkeleri

- Kullanıcı adı `^[A-Za-z0-9._]{1,30}$` ile doğrulanır; harici araçlara shell olmadan argüman listesiyle gider.
- Dosya yolları `data` kökünün altında olacak şekilde doğrulanır (path traversal).
- Model çıktısı ve Instagram açıklamaları yalnızca veri: JSON şemasıyla doğrulanır, arayüzde otomatik kodlanır, `MarkupString` kullanılmaz; CSP başlığı vardır.
- Uygulama ve Ollama yalnızca localhost'ta dinler.
- Oturum dosyası, fotoğraflar, veritabanı ve raporlar repoya girmez.
- İndirmeden önce boş disk alanı kontrol edilir.

## Arayüz

Sade, koyu tema (Linear/Vercel tadında), sistem yazı tipi ailesi. Renk, yazı ve boşluk değerleri tek bir `tokens.css` dosyasında. Yükleniyor, boş ve hata durumları ayrı tasarlanır. Klavyeyle tam kullanım, WCAG AA kontrast, mobil uyum, 150-300 ms animasyonlar ve `prefers-reduced-motion` desteği.

## Açık sorular

1. instaloader bugün öne çıkanları yedek hesapla sorunsuz indiriyor mu? (Uygulamanın ilk adımında denenir.)
2. `qwen3-vl:8b` mi `4b` mi? (Performans adımında ölçülerek karar verilir.)
