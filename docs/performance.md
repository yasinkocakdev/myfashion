# Performans: önce / sonra

## Yöntem

- **Makine:** MacBook Air, Apple M5, 16 GB RAM. Ollama 0.40.18, `qwen3-vl:8b`, görseller 768 px.
- **Sabit veri seti:** ffmpeg'in ürettiği 40 farklı görsel (1080×1350) ve hikâye kaydını taklit eden, her biri 4 sahneli 20 saniyelik 2 video (1080×1920). Kıyafet içermediği için model kısa yanıt verir. Gerçek kıyafet fotoğraflarında yanıt daha uzun olacağından model süresi artar; bu yüzden ölçülen kazanç **ffmpeg ve akış tarafının** kazancıdır.
- **Araç:** `tools/MyFashion.Bench`. Gerçek `AnalysisRunner`, gerçek ffmpeg ve gerçek Ollama ile çalışır. Her ölçüm sıfır bir veritabanıyla başlar. `IMediaToolkit` ve `IVisionAnalyzer` zamanlama dekoratörleriyle sarılır; ürün kodu ölçüm için değişmez.
- **Soğuk başlangıç:** Her ölçümden önce model bellekten boşaltılır (`keep_alive: 0`), yani ilk görsel modelin yüklenme süresini de içerir.

```
dotnet run --project tools/MyFashion.Bench -c Release -- run <klasör> --cold
dotnet run --project tools/MyFashion.Bench -c Release -- parallel <klasör> --count 12
```

Tablo sütunları: geçiş, toplam süre, dosya, görsel/kare, elenen benzer kare, model çağrısı, önbellekten gelen, hatalı, dakikada işlenen görsel/kare.
