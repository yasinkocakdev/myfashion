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

## 7. Ollama istemcisi (`feat(analysis): analyze images with a local Ollama vision model`)

- **SDK yok, `HttpClient` var.** Ollama'nın `/api/chat` arayüzü tek bir JSON isteği. Bunun için kütüphane eklemek bağımlılık ve güvenlik yüzeyini gereksiz büyütürdü.
- **JSON şeması (`format`)** modele "sadece bu biçimde cevap ver" der. Kategori, renk ve stil etiketleri sabit listelerle (`enum`) sınırlıdır. Böylece model "kömür grisi" yerine `grey` yazmak zorunda kalır ve istatistikler tutarlı olur.
- **Sadece localhost.** `OllamaChatClient`, adres `127.0.0.1` değilse hiç oluşturulmaz. "Fotoğraflar bilgisayardan çıkmaz" sözü dokümanda değil kodda garanti altında.
- **Hata türlerine göre farklı davranış:** Geçici hatalar (bağlantı, 5xx, bozuk çıktı) bir kez daha denenir. Kalıcı hatalar ("model kurulu değil") hemen, nasıl düzeltileceğini söyleyen bir mesajla durur. Kullanıcı işi iptal ederse bu bir hata gibi gösterilmez.
- **Açıklama (caption) ayraçların arasına konur ve "bu veri, talimat değil" diye etiketlenir.** İçindeki `<`, `>` ve kontrol karakterleri silinir, uzunluğu 500 karakterle sınırlanır. Bu, prompt injection'a karşı ikinci savunma hattıdır; birincisi `ModelOutputParser`.

## 8. Analiz akışı (`feat(app): orchestrate analysis runs with cache, resume and failure isolation`)

- **`AnalysisRunner`** bütün parçaları birleştirir ama hiçbirinin nasıl çalıştığını bilmez. Elinde sadece arayüzler var: `IMediaSource`, `IMediaToolkit`, `IVisionAnalyzer`, `IAnalysisStore` (Dependency Inversion). Bu yüzden testleri ffmpeg ve Ollama olmadan, milisaniyeler içinde çalışıyor.
- **Hata izolasyonu:** Bozuk bir dosya ya da analiz edilemeyen bir görsel işi durdurmaz; "başarısız" olarak kaydedilip geçilir. Ama üst üste 5 hata, büyük ihtimalle Ollama'nın kapalı olduğu anlamına gelir. O zaman yüzlerce görseli boşuna denemek yerine iş anlaşılır bir mesajla durur (circuit breaker fikri).
- **Önbellek anahtarı, hazırlanmış görselin SHA-256'sı ve model adıdır.** İş iptal edilip yeniden başlatıldığında ya da aynı hesap tekrar analiz edildiğinde, önceden analiz edilmiş görseller modele tekrar gönderilmez. Model değişirse önbellek kullanılmaz, çünkü farklı model farklı sonuç verir.
- **İptal edilen işin durumu `CancellationToken.None` ile kaydedilir.** İptal edilmiş token'la "iptal edildi" bilgisini kaydetmeye çalışmak, kaydın kendisini de iptal ederdi.

## 9. SQLite ve EF Core (`feat(data): persist runs and analyses in SQLite with EF Core`)

- **Taşınabilir model:** Enum'lar sayı değil metin olarak saklanır (`HasConversion<string>`); veritabanına bakınca `Outerwear` yazar. Tarihler UTC `DateTime` tutulur, çünkü SQLite `DateTimeOffset` ile sıralama yapamaz. Her metin alanının uzunluğu açıkça belirtilir. Bu üç kural sayesinde ileride SQL Server ya da PostgreSQL'e geçiş genelde `UseSqlite` satırını değiştirmek ve migration'ları yeniden üretmek demek.
- **Migration'lar** şemanın sürüm geçmişi gibidir. `dotnet ef migrations add` ile üretilir ve uygulama açılırken uygulanır. Elle tablo yazmak yerine kod ile şema hep aynı kalır.
- **`IDbContextFactory` ile her işlem için yeni bir DbContext:** DbContext thread-safe değildir. Analiz arka planda çalışırken arayüz de veriyi okuyacak; her çağrının kendi context'i olması çakışmayı önler.
- **Testler gerçek SQLite'la çalışır** (bellekte, `:memory:`). EF Core'un "InMemory" sağlayıcısı ilişkileri ve kısıtları gerçek bir veritabanı gibi uygulamadığı için yanıltıcı olabilir.
- **Ders:** `.gitignore`'daki `data/` kuralı macOS'ta (büyük-küçük harf duyarsız) `Data/` kod klasörünü de gizledi. Kökteki klasörleri `/data/` diye sabitlemek gerekiyor. `git status`'a her commit öncesi bakmak bu yüzden önemli.

## 10. Özet motoru (`feat(profile): build style profiles from analyzed images`)

- **`StyleProfileBuilder` saf bir fonksiyondur:** Aynı girdi hep aynı çıktıyı verir; veritabanına, dosyaya ya da modele dokunmaz. Bu yüzden en kritik iş mantığı olduğu hâlde testi en kolay parça bu. LINQ'in `GroupBy`, `OrderByDescending`, `ThenBy` zinciri burada istatistik çıkarmanın ana aracı.
- **Eşitlik bozulmalarında `ThenBy`:** İki renk aynı sayıda geçiyorsa sıra rastgele olmasın diye ikinci bir sıralama ölçütü (isim) eklenir. Böylece rapor her açılışta aynı görünür, testler de kararlı olur.
- **Gürültü filtresi:** Güveni 0.3'ün altındaki parçalar sayılmaz. İmza parça için en az 2 tekrar, kombin formülü için en az 2 görsel gerekir. Tek seferlik bir şey "tarz" değildir.
- **Kombin formülü** bir görseldeki ana parçaların (dış giyim, üst, elbise, alt, ayakkabı) renk ve kategori dizisidir; örneğin "black top + denim bottom + white footwear". Aksesuarlar formüle girmez, yoksa her şapka yeni bir formül yaratırdı.

## 11. Öneriler (`feat(recommendations): ...`)

- **Linkleri model değil kod üretir (`StoreCatalog`).** Model sadece "ne aranacağını" söyler. O metin harf, rakam ve tire dışındaki her şeyden temizlenir, URL için kodlanır (`Uri.EscapeDataString`) ve izin listesindeki sabit mağaza şablonlarına yerleştirilir. Model `https://kotu-site` yazsa bile bu adres link olmaz, sadece arama kelimesine dönüşür. Kullanıcının tıklayacağı her şeyin kaynağına güvenebilmek gerekir.
- **`ProfilePromptWriter`**, binlerce parçalık analizi modele ham hâliyle göndermek yerine birkaç satırlık özet hâlinde verir: palet, kategoriler, markalar, formüller. Kısa ve net girdi hem daha hızlı hem daha tutarlı cevap getirir.
- **Testin yakaladığı hata:** Model `search_query` alanını `""` olarak döndürdüğünde `?? name` çalışmıyordu, çünkü `??` sadece `null` için devreye girer, boş metin için değil. Çözüm, temizlenmiş sorgunun uzunluğuna bakmak oldu.

## 12. Web arayüzü (`feat(web): ...`, `style(web): add design tokens and component styles`)

- **Arka plan iş kuyruğu (`AnalysisJobService`):** Analiz dakikalar sürebilir. Bu yüzden düğmeye basınca iş bir `Channel`'a (thread-safe kuyruk) yazılır ve bir `BackgroundService` onu sırayla işler. Sayfa `Changed` olayına abone olur, ekranı `InvokeAsync(StateHasChanged)` ile günceller. İş durumu değiştirilemeyen bir `record` olarak tutulur; birden fazla thread aynı nesneyi okurken yarım güncellenmiş bir hâlini görmez.
- **Gösterim bileşenleri ve sayfalar ayrı:** `ProfileReport`, `RecommendationsView` ve `JobProgressView` sadece parametre alıp çizer; veriyi sayfalar getirir. Bu sayede bUnit testleri veritabanı ya da servis olmadan çalışır.
- **Her ekranın üç durumu ayrı tasarlandı:** yükleniyor (`LoadingState`, `aria-busy`), boş (`EmptyState`, ne yapılacağını söyleyen bir eylemle) ve hata (`ErrorState`, `role="alert"` ve "Tekrar dene").
- **Güvenlik:**
  - Uygulama yalnızca `127.0.0.1`'de dinler. `AllowedHosts` başka bir host adıyla gelen isteği reddeder; bu, DNS rebinding saldırısına karşı korur. Testte `Host: evil.example` → 400.
  - CSP yalnızca uygulamanın kendi script'lerine izin verir. Blazor'un inline import map'i CSP'ye takıldığı için kaldırıldı, CSP gevşetilmedi.
  - Görseller `/media/...` uç noktasından yalnızca `.jpg` olarak ve `PathGuard` kontrolünden geçerek sunulur (`..%2F..` → 404).
  - Veri klasörü `700` izinlidir; başkasının fotoğraflarını bilgisayardaki başka kullanıcılar okuyamaz.
  - Model çıktısı ve açıklamalar Blazor tarafından otomatik kodlanır. Testte `<script>` marka adı metin olarak görünür, çalışmaz.
- **Erişilebilirlik:** Sayfanın başında "İçeriğe geç" linki var. Her form alanının bir `label`'ı var, hatalar `aria-describedby` ile alana bağlı. Renk paletinin metin karşılığı var (`role="img"` + `aria-label`). Görsellerin `alt` metni kişiyi değil kıyafeti anlatır. Yeni sekmede açılan linkler `rel="noopener noreferrer"` kullanır ve bunu ekran okuyucuya söyler.

## 13. Performans: önce ölç (`chore(bench): add an analysis benchmark tool`)

- **Tahminle değil ölçümle iyileştirilir.** `tools/MyFashion.Bench`, gerçek akışı sabit bir veri setiyle çalıştırır ve her aşamanın süresini ölçer. Ürün koduna dokunmadan, `IMediaToolkit` ve `IVisionAnalyzer`'ı saran zamanlama dekoratörleriyle (Decorator deseni) yapar bunu. Arayüzlerle kurulan mimarinin bir faydası da bu: Ölçüm için kodu değiştirmek gerekmiyor.
- **"Önce" ölçümü eski commit'ten alınır.** `git worktree` ile değişiklikten önceki commit ayrı bir klasöre açılıp orada ölçülür. Rakamlar gerçekten eski koddan gelir.
- **Ölçüm bir hatayı ortaya çıkardı:** `qwen3-vl:8b` etiketi modelin "düşünen" sürümüymüş. `think: false` ayarına rağmen her görsel için gizli bir düşünme metni üretiyor, yani token'ların çoğu boşa gidiyor. Ders: Bir ayarın gerçekten uygulandığını kontrol et (`ollama show` şablonu, yanıttaki `thinking` alanı ve `eval_count`).

## 14. Tek ffmpeg süreci ve örtüşen akış (`perf(media): ...`, `perf(app): ...`)

- **Süreç açmak pahalıdır.** Eskiden her görsel için iki ffmpeg süreci açılıyordu (küçült, sonra hash), videoda ise her kare için ayrı bir hash süreci. Artık ffmpeg'in `split` filtresiyle tek süreç hem JPEG'i hem 9x8 gri pikselleri üretiyor; görsel bir kez çözülüyor (decode).
- **Üretici-tüketici (producer-consumer):** Model bir görseli analiz ederken ffmpeg sıradaki dosyayı hazırlar. Aradaki `Channel` sınırlıdır (kapasite 4): Hazırlık modelden çok öne geçip diski ve belleği doldurmaz (back-pressure). Tek üretici ve tek tüketici olduğu için sıra korunur.
- **Paylaşılan sayaçlar** artık iki görevden birden güncelleniyor. Bu yüzden `lock` ile korunan değiştirilemez bir `RunCounts` kaydı tutuluyor; okuyan taraf hiçbir zaman yarım güncellenmiş bir sayı görmez.
- **Eşzamanlılık testi zamana değil el sıkışmaya dayanır:** Sahte model, ikinci dosyanın hazırlanmaya başlamasını bekler. Kod sıralı çalışsaydı bu bekleme hiç bitmezdi. "Şu kadar milisaniyeden hızlı mı?" diye bakan testler makineye göre bazen geçer bazen kalır (flaky); bu yaklaşım öyle olmaz.

## 15. Instagram resmi API (`feat(instagram): ...`, `feat(web): ...`, `docs(instagram): ...`)

- **Resmi yol:** Business Discovery, başka bir İşletme/Creator hesabının postlarını, kendi profesyonel hesabının erişim anahtarıyla okur. Giriş taklidi ya da kazıma yok, dolayısıyla hesap kısıtlama riski de yok.
- **Anahtar URL'de değil başlıkta (`Authorization: Bearer`) gider.** URL'ler loglara, tarayıcı geçmişine ve proxy kayıtlarına düşer; başlıklar düşmez. `InstagramCredentials.ToString()` da anahtarı yıldızla gizler, çünkü record'lar varsayılan olarak bütün alanlarını yazdırır.
- **Data Protection** anahtarı şifreler. Dosya `600` izinli, klasör `700`. Arayüz anahtarı bir daha göstermez, sadece "bağlı" der.
- **SSRF koruması:** API'nin verdiği medya adresine körü körüne istek atılmaz. Yalnızca https, varsayılan port ve `*.cdninstagram.com` / `*.fbcdn.net` kabul edilir. Yönlendirmeler kapalıdır; açık olsaydı izinli bir adres isteği başka bir host'a yönlendirebilirdi. Boyut 50 MB ile sınırlıdır.
- **Sonsuz döngüye karşı:** API aynı sayfa işaretini (cursor) tekrar döndürürse çekme durur. Bunu bir test yakaladı.
