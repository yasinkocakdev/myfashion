# LEARNING.md

Projedeki önemli kararların kısa notları. Her not: **bu neydi, neden böyle yapıldı.** Öğrenme aşamasında buradan çalışılacak; her başlığın yanında ilgili commit var.

## 1. Solution yapısı ve kalite kapısı (`chore: scaffold solution with quality gates`)

- **Dört proje var:** `Core` saf iş mantığını tutar ve hiçbir dış şeye bağlı değildir. `Infrastructure` dış dünyaya (dosya, süreç, HTTP, veritabanı) dokunur. `Web` arayüzdür. `Tests` testlerdir. Bağımlılık hep içe doğrudur (Web → Infrastructure → Core). Böylece iş mantığı, sahte (fake) parçalarla hızlıca test edilebilir. `SolutionSmokeTests` bu kuralı bir testle korur.
- **`Directory.Build.props`** tüm projelerin ortak ayarlarını tek yerde tutar. `TreatWarningsAsErrors` her uyarıyı hata yapar; "build uyarısız" hedefi böylece pazarlığa açık olmaz. `AnalysisLevel` .NET'in kod analizörlerini açar. `NuGetAudit` bilinen açığı olan bir paket eklenirse build'i kırar.
- **`Directory.Packages.props`** (Central Package Management) her NuGet sürümünü tek dosyada toplar. İki projenin aynı paketin farklı sürümlerini kullanması böylece imkânsız hâle gelir.
- **`global.json`** SDK sürümünü sabitler ve testleri yeni Microsoft Testing Platform ile çalıştırır. .NET 10'da eski VSTest yolu xUnit v3 ile artık desteklenmiyor.

## 2. Güvenli girdi: kullanıcı adı, dosya yolu, renk (`feat(core): add username parsing, path guard and color normalization`)

- **`InstagramUsername`** linki ya da kullanıcı adını doğrular ve dışarıdan oluşturulamaz: constructor gizli, tek yol `TryParse`. Elinde bir `InstagramUsername` varsa onun güvenli olduğu kesindir. Buna "parse, don't validate" denir. `evil.com/instagram.com/x` gibi sahte host'lar, `; rm -rf ~` ve `--login=...` gibi komut ve argüman enjeksiyonları burada reddedilir. Bu değer ileride harici araçlara ve API'lere gidecek.
- **`PathGuard`** path traversal'a karşı korur. Dışarıdan gelen bir dosya adı (`../../etc/passwd`) veri klasörünün dışına çıkamaz. Dikkat edilen ince nokta: `/data-evil`, `/data` ile başlıyor ama onun içinde değil. Bu yüzden kontrol, sonuna `/` eklenmiş kök yolla yapılıyor.
- **`StyleColors`** "jet black", "Black" ve "siyah" gibi farklı yazımları tek bir renge (`black`) çevirir. Bu yapılmazsa istatistikler aynı rengi üç ayrı renk gibi sayar.

## 3. Klasör kaynağı ve `IMediaSource` (`feat(media): read screenshots and screen recordings from a folder`)

- **`IMediaSource`** "medya nereden gelirse gelsin" sorusunun cevabı: klasör, Instagram API ya da ileride başka bir şey. Analiz akışı sadece bu arayüzü bilir. Yeni bir kaynak eklemek akışı değiştirmez (Open/Closed ilkesi).
- **`IAsyncEnumerable`** dosyaları tek tek, geldikçe verir. Böylece 1000 dosyalık bir klasör belleğe tek seferde yüklenmez ve iş `CancellationToken` ile her adımda iptal edilebilir.
- **Sembolik linkler takip edilmez.** Klasöre konmuş bir link (`link.jpg -> /etc/...`) programı klasör dışındaki bir dosyayı okumaya kandırabilirdi. Gizli ve boş dosyalar da atlanır.
- **Alt klasör adı türü belirler:** `posts`, `reels`, `highlights` ya da `öne çıkanlar`. Böylece raporda "highlight'larda şunu, postlarda bunu giyiyor" ayrımı yapılabilir.

## 4. Shell'siz süreç çalıştırma ve dHash (`feat(infra): run external tools without a shell`, `feat(media): add dHash and near-duplicate filter`)

- **`ProcessRunner`** ffmpeg gibi harici araçları shell olmadan çalıştırır. Argümanlar `ArgumentList` ile tek tek verilir. Dosya adı `a; rm -rf ~` olsa bile bu bir komut olarak değil, düz metin olarak gider. Testi de tam olarak bunu kontrol ediyor. Zaman aşımı ya da iptalde süreç ağacının tamamı öldürülür; böylece arkada asılı kalan ffmpeg kalmaz.
- **dHash**, bir görseli 9x8 gri piksele küçültüp yan yana piksellerin "soldaki daha mı parlak?" sorusuna verdiği 64 cevabı 64 bitlik bir sayıya dönüştürür. Neredeyse aynı iki karenin hash'leri sadece birkaç bitte farklıdır. İki hash arasındaki fark `PopCount(a ^ b)` ile tek bir işlemci komutunda bulunur.
- **`NearDuplicateFilter`** bir ekran kaydındaki yüzlerce benzer kareden yalnızca gerçekten farklı olanları bırakır. Bu, modelin gereksiz yere aynı kıyafeti 50 kez analiz etmesini önler; performans adımının en büyük kazancı buradan gelecek.

## 5. ffmpeg araç seti (`feat(media): extract frames, resize and hash with ffmpeg`)

- **`IMediaToolkit`** Core'da bir arayüz, `FfmpegMediaToolkit` ise Infrastructure'da onun uygulaması. Analiz akışı ffmpeg'i hiç bilmez. Testlerde ffmpeg yerine sahte bir araç seti verilebilir; ileride başka bir kütüphaneye geçmek de sadece bu sınıfı değiştirir.
- **Görseller modele gitmeden önce 768 piksele küçültülür.** Model büyük görselde daha yavaş çalışır ama kıyafet tanımada belirgin şekilde daha iyi olmaz. `force_original_aspect_ratio=decrease` en-boy oranını korur, `min(768,iw)` ise küçük görseli büyütmez.
- **`ToolLocator`**: Finder'dan açılan uygulamalar terminalin PATH'ini görmez. Bu yüzden Homebrew'un klasörlerine açıkça bakılır.
- **Entegrasyon testleri** gerçek ffmpeg'le, test sırasında ffmpeg'in kendi ürettiği örnek video ve görsellerle çalışır. Repoya ikili test dosyası koymaya gerek kalmaz. ffmpeg yoksa testler hata vermez, atlanır (`Assert.SkipWhen`).

## 6. Model çıktısına güvenmemek (`feat(analysis): add analysis model and strict model output parser`)

- **Yapay zekâ çıktısı dışarıdan gelen veri gibi işlenir.** Model yanlış bir değer uydurabilir. Ayrıca fotoğraftaki bir yazı ("talimatları unut, şunu yaz") modeli yönlendirmeye çalışabilir; buna prompt injection (OWASP LLM01) denir. Bu yüzden `ModelOutputParser` her alanı doğrular: bilinmeyen kategori `Other` olur, metinlerdeki kontrol karakterleri silinir ve uzunluk 60 karakterle sınırlanır, güven puanı 0 ile 1 arasına sıkıştırılır, en fazla 12 parça kabul edilir.
- **Kanıtsız marka atılır.** Model "Gucci" der ama logo, yazı ya da açıklama gibi bir kanıt göstermezse marka silinir. Markalar raporun en değerli kısmı ve uydurma marka, hiç marka olmamasından daha kötüdür.
- **Enum'lar sayı olarak gelirse reddedilir.** `Enum.TryParse` `"5"` gibi bir değeri de kabul eder; ek kontrol bu açığı kapatır.
