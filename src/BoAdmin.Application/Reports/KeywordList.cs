using System.Globalization;
using BoAdmin.Application.Reports.Dtos;

namespace BoAdmin.Application.Reports;

/// <summary>
/// Rapor keyword'lerinin okunması, birleştirilmesi ve yazılması.
/// Keyword'ler virgül, noktalı virgül veya satır sonu ile ayrılır; yazarken ", " ile birleştirilir.
/// Aynı keyword (büyük/küçük harf farkı gözetmeden) bir kez tutulur, ilk görülen yazımı korunur.
/// </summary>
public static class KeywordList
{
    /// <summary>Güvenli üst sınır (karakter).</summary>
    public const int MaxLength = 2000;

    private static readonly StringComparer Comparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);
    private static readonly char[] Separators = { ',', ';', '\n', '\r' };

    public static IReadOnlyList<string> Parse(string? value)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(Comparer);
        if (string.IsNullOrWhiteSpace(value)) return result;

        foreach (var part in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var keyword = part.Trim();
            if (keyword.Length > 0 && seen.Add(keyword)) result.Add(keyword);
        }
        return result;
    }

    public static string Format(IEnumerable<string> keywords) => string.Join(", ", keywords);

    /// <summary>İşlem sonrası keyword metni.</summary>
    public static string Apply(string? current, string? requested, KeywordMode mode)
    {
        var requestedList = Parse(requested);
        if (mode == KeywordMode.Replace) return Format(requestedList);

        var merged = new List<string>(Parse(current));
        var seen = new HashSet<string>(merged, Comparer);
        foreach (var k in requestedList)
            if (seen.Add(k)) merged.Add(k);
        return Format(merged);
    }

    /// <summary>İki keyword metni aynı kümeyi mi içeriyor (sıra ve büyük/küçük harf önemsiz).</summary>
    public static bool SameSet(string? a, string? b)
    {
        var left = new HashSet<string>(Parse(a), Comparer);
        return left.SetEquals(Parse(b));
    }

    /// <summary>Excel'deki "İşlem" değeri: boş / "Ekle" / "Add" -> Ekle, "Değiştir" / "Replace" -> Değiştir.</summary>
    public static KeywordMode? ParseMode(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        return v switch
        {
            "" or "ekle" or "add" or "append" => KeywordMode.Add,
            "değiştir" or "degistir" or "değistir" or "replace" or "yenile" => KeywordMode.Replace,
            _ => null
        };
    }
}
