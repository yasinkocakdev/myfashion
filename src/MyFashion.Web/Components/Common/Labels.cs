using System.Globalization;
using MyFashion.Core.Analysis;
using MyFashion.Core.Application;
using MyFashion.Core.Media;

namespace MyFashion.Web.Components.Common;

/// <summary>Turkish display names for the English values stored in the database.</summary>
public static class Labels
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        // Colors
        ["black"] = "siyah", ["white"] = "beyaz", ["cream"] = "krem", ["grey"] = "gri", ["beige"] = "bej", ["brown"] = "kahverengi",
        ["navy"] = "lacivert", ["blue"] = "mavi", ["light blue"] = "açık mavi", ["denim"] = "denim", ["green"] = "yeşil",
        ["olive"] = "zeytin yeşili", ["khaki"] = "haki", ["red"] = "kırmızı", ["burgundy"] = "bordo", ["pink"] = "pembe",
        ["purple"] = "mor", ["yellow"] = "sarı", ["orange"] = "turuncu", ["gold"] = "altın", ["silver"] = "gümüş",
        ["multicolor"] = "çok renkli", ["other"] = "diğer",

        // Patterns and fits
        ["solid"] = "düz", ["striped"] = "çizgili", ["checked"] = "ekose", ["floral"] = "çiçekli", ["graphic"] = "grafik baskılı",
        ["logo"] = "logolu", ["slim"] = "dar", ["regular"] = "normal", ["oversized"] = "bol (oversize)", ["unknown"] = "belirsiz",

        // Categories (also used inside formulas: "black top")
        ["top"] = "üst", ["outerwear"] = "dış giyim", ["bottom"] = "alt", ["dress"] = "elbise", ["footwear"] = "ayakkabı",
        ["bag"] = "çanta", ["accessory"] = "aksesuar",

        // Style tags
        ["minimal"] = "minimal", ["streetwear"] = "sokak stili", ["old money"] = "old money", ["casual"] = "günlük",
        ["smart casual"] = "smart casual", ["sporty"] = "sportif", ["athleisure"] = "athleisure", ["techwear"] = "techwear",
        ["vintage"] = "vintage", ["preppy"] = "preppy", ["y2k"] = "Y2K", ["workwear"] = "workwear", ["formal"] = "resmi",
        ["bohemian"] = "bohem", ["edgy"] = "sıra dışı", ["classic"] = "klasik", ["romantic"] = "romantik", ["grunge"] = "grunge",
    };

    /// <summary>Translates a known word, or a "color category" pair from formulas; anything else is shown as-is.</summary>
    public static string Translate(string value)
    {
        if (Words.TryGetValue(value, out var word))
        {
            return word;
        }

        // Formula pieces and signature pieces look like "black top" or "black hoodie": translate the color word.
        foreach (var (key, translated) in Words)
        {
            if (value.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = value[(key.Length + 1)..];
                return $"{translated} {(Words.TryGetValue(rest, out var restWord) ? restWord : rest)}";
            }
        }

        return value;
    }

    public static string Category(GarmentCategory category) => Translate(category.ToString());

    public static string Kind(MediaKind kind) => kind switch
    {
        MediaKind.Post => "Gönderi",
        MediaKind.Reel => "Reels",
        MediaKind.Highlight => "Öne çıkan",
        _ => "Manuel",
    };

    public static string Evidence(BrandEvidence evidence) => evidence switch
    {
        BrandEvidence.Logo => "logo",
        BrandEvidence.Text => "yazı",
        BrandEvidence.Caption => "açıklama",
        _ => "-",
    };

    public static string Status(RunStatus status) => status switch
    {
        RunStatus.Running => "Sürüyor",
        RunStatus.Completed => "Tamamlandı",
        RunStatus.Failed => "Başarısız",
        _ => "İptal edildi",
    };

    public static string Date(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR")) ?? "-";
}
