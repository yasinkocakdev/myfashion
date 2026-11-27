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
