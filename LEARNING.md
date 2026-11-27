# LEARNING.md

Projedeki önemli kararların kısa notları. Her not: **bu neydi, neden böyle yapıldı.** Öğrenme aşamasında buradan çalışılacak; her başlığın yanında ilgili commit var.

## 1. Solution yapısı ve kalite kapısı (`chore: scaffold solution with quality gates`)

- **Dört proje var:** `Core` saf iş mantığını tutar ve hiçbir dış şeye bağlı değildir. `Infrastructure` dış dünyaya (dosya, süreç, HTTP, veritabanı) dokunur. `Web` arayüzdür. `Tests` testlerdir. Bağımlılık hep içe doğrudur (Web → Infrastructure → Core). Böylece iş mantığı, sahte (fake) parçalarla hızlıca test edilebilir. `SolutionSmokeTests` bu kuralı bir testle korur.
- **`Directory.Build.props`** tüm projelerin ortak ayarlarını tek yerde tutar. `TreatWarningsAsErrors` her uyarıyı hata yapar; "build uyarısız" hedefi böylece pazarlığa açık olmaz. `AnalysisLevel` .NET'in kod analizörlerini açar. `NuGetAudit` bilinen açığı olan bir paket eklenirse build'i kırar.
- **`Directory.Packages.props`** (Central Package Management) her NuGet sürümünü tek dosyada toplar. İki projenin aynı paketin farklı sürümlerini kullanması böylece imkânsız hâle gelir.
- **`global.json`** SDK sürümünü sabitler ve testleri yeni Microsoft Testing Platform ile çalıştırır. .NET 10'da eski VSTest yolu xUnit v3 ile artık desteklenmiyor.
