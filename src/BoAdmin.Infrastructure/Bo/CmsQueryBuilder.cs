using System.Globalization;
using System.Text;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// CMS sorgu metni için güvenli yardımcılar.
/// Kullanıcı girdisi doğrudan sorguya eklenmez; önce Sanitize edilir.
/// </summary>
internal static class CmsQueryBuilder
{
    /// <summary>
    /// CMS sorgusundaki tarih literal formatı: '2026.01.15.00.00.00' (doğrulanmalı).
    /// Farklı çıkarsa yalnızca bu sabiti değiştirin.
    /// </summary>
    public const string DateFormat = "yyyy.MM.dd.HH.mm.ss";

    /// <summary>
    /// Metin filtrelerini temizler. CMS sorgu dilinde tırnak kaçışı sürüme göre farklı
    /// olabileceği için (doğrulanmalı) riskli karakterler tamamen çıkarılır:
    /// ' " \ ; ve kontrol karakterleri. LIKE joker karakterleri (% _) bırakılır;
    /// rapor adlarında "_" sık geçer ve joker olarak kendisiyle de eşleşir.
    /// </summary>
    public static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (ch is '\'' or '"' or '\\' or ';' || char.IsControl(ch)) continue;
            sb.Append(ch);
        }

        var result = sb.ToString();
        return result.Length > 200 ? result[..200] : result;
    }

    /// <summary>SI_NAME LIKE '%x%'</summary>
    public static string LikeContains(string field, string value) =>
        $"{field} LIKE '%{Sanitize(value)}%'";

    public static string DateLiteral(DateTime value) =>
        $"'{value.ToString(DateFormat, CultureInfo.InvariantCulture)}'";

    /// <summary>SI_ID IN (1,2,3) - yalnızca sayı kabul eder.</summary>
    public static string InList(string field, IEnumerable<int> ids) =>
        $"{field} IN ({string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)))})";

    /// <summary>SI_KIND IN ('Webi','CrystalReport') - yalnızca sabit tür listesinden çağrılır.</summary>
    public static string InList(string field, IEnumerable<string> values) =>
        $"{field} IN ({string.Join(",", values.Select(v => $"'{Sanitize(v)}'"))})";
}
