# myfashion MVP Uygulama Planı

**Amaç:** Instagram linkinden ya da klasörden gelen fotoğraf ve videoları lokal yapay zekâyla analiz edip stil profili, alışveriş listesi ve kombin önerisi gösteren bir C# uygulaması.

**Tasarım:** [docs/tasarim.md](../../tasarim.md)

**Çalışma kuralları:** `improve/mvp` dalı; her mantıklı değişiklik ayrı commit (Conventional Commits); her adımda build ve test yeşil olmadan commit yok; force push yok; gizli bilgi commitlenmez. Yeni kütüphane, şema değişikliği ve modül yeniden yazımı için onay alınır (onaylananlar: xUnit v3, bUnit, EF Core SQLite; Ollama, qwen3-vl).

## Genel kısıtlar

- .NET 10, `TreatWarningsAsErrors`, Nullable açık, `AnalysisLevel` latest-recommended.
- Harici süreçler yalnızca `ArgumentList` ile; shell yok.
- Web ve Ollama yalnızca `127.0.0.1`.
- Veri kökü: `~/Library/Application Support/MyFashion` (ayarlanabilir), repo dışında.

## İnceleme odağı (testlerle sabitlenecek riskler)

1. Gizli, var olmayan ya da geçersiz hesap linki → anlaşılır hata, çökme yok (Görev 3).
2. API hız sınırı, süresi dolmuş anahtar ya da İşletme/Creator olmayan hedef hesap → çekme durur, anlaşılır mesaj, o ana kadarki medya analiz edilebilir (Görev 3).
3. İçinde kıyafet olmayan kare ya da modelin bozuk JSON döndürmesi → kare atlanır, akış devam eder (Görev 5).
4. Türkçe karakterli ya da boşluklu dosya yolları → doğru işlenir (Görev 4).
5. İşin yarıda iptal edilmesi → yarım sonuçlar tutarlı kalır, tekrar başlatınca önbellekten devam eder (Görev 6).

## Görevler

| # | Görev | Dosyalar | Test |
|---|-------|----------|------|
| 1 | Solution iskeleti ve kalite kapısı (CI dahil) | `MyFashion.slnx`, `Directory.Build.props`, `src/*`, `tests/*`, `.github/workflows/ci.yml` | `dotnet build` 0 uyarı, `dotnet test` yeşil |
| 2 | Core modelleri ve saf yardımcılar: `InstagramProfileLink`, `PathGuard`, renk normalleştirme | `src/MyFashion.Core/*` | Birim testleri (geçerli/geçersiz linkler, enjeksiyon girdileri, traversal) |
| 3 | Manuel klasör kaynağı (ekran kaydı, ekran görüntüsü) ve Instagram Graph API kaynağı (Business Discovery, sayfalama, hata kodları, şifreli anahtar saklama) | `Infrastructure/Media/FolderMediaSource.cs`, `Infrastructure/Instagram/*` | Klasör taraması (Türkçe yol, desteklenmeyen dosya), sahte `HttpMessageHandler` ile API yanıtları: sayfalama, hız sınırı, yetki hatası, Creator olmayan hesap |
| 4 | Ön işleme: ffmpeg ile kare çıkarma, küçültme, dHash, benzer kare eleme | `Infrastructure/Media/*`, `Core/Media/DHash.cs` | Üretilmiş test videosu ve görselleriyle kare sayısı, Hamming mesafesi, Türkçe yol |
| 5 | Ollama istemcisi ve görsel analizi (JSON şema, doğrulama, zaman aşımı, yeniden deneme) | `Infrastructure/Ollama/*`, `Core/Analysis/*` | Sahte `HttpMessageHandler` ile geçerli, bozuk, zaman aşımı yanıtları; Ollama varsa entegrasyon testi |
| 6 | SQLite kalıcılığı ve `AnalysisRunner` akışı (önbellek, iptal, ilerleme) | `Infrastructure/Data/*`, `Core/Application/AnalysisRunner.cs` | In-memory SQLite, sahte kaynak/analizcilerle uçtan uca akış, iptal ve devam testleri |
| 7 | Özet motoru `StyleProfileBuilder` | `Core/Profile/*` | Birim testleri (palet, sıklıklar, formüller, boş girdi) |
| 8 | Öneriler: `OllamaRecommendationGenerator`, `ShoppingLinkBuilder` | `Infrastructure/Ollama/*`, `Core/Recommendations/*` | Link kodlama ve izin listesi testleri, şema doğrulama |
| 9 | Web arayüzü: analiz başlatma, ilerleme, rapor, karşılaştırma, ayarlar; arka plan kuyruğu | `src/MyFashion.Web/*` | bUnit bileşen testleri, duman testi |
| 10 | Güvenlik denetimi (OWASP Top 10 + LLM01) ve sertleştirme | `Web/Program.cs`, ilgili sınıflar, `docs/security-review.md` | Enjeksiyon, traversal, XSS, başlık testleri |
| 11 | Performans: sabit örnek setle önce/sonra ölçüm ve iyileştirme | Ön işleme ve analiz katmanı, `docs/performance.md` | Ölçüm komutu |
| 12 | Görsel tasarım (token'lar, durumlar, erişilebilirlik), README, LEARNING.md | `Web/wwwroot/*`, `README.md`, `LEARNING.md` | bUnit, axe/kontrast kontrolü, temiz klonla kurulum süresi |
