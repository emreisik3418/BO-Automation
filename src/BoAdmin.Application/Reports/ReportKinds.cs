namespace BoAdmin.Application.Reports;

public sealed record ReportKindInfo(string Kind, string DisplayName);

/// <summary>
/// Uygulamanın "rapor" saydığı BO nesne türleri (SI_KIND).
/// Arama ve silme yalnızca bu türlerle sınırlıdır; böylece yanlışlıkla klasör,
/// kullanıcı, evren vb. nesneler silinemez.
/// </summary>
public static class ReportKinds
{
    public const string Webi = "Webi";
    public const string Crystal = "CrystalReport";

    public static readonly IReadOnlyList<ReportKindInfo> All = new[]
    {
        new ReportKindInfo(Webi, "Web Intelligence"),
        new ReportKindInfo(Crystal, "Crystal Reports"),
        new ReportKindInfo("Excel", "Excel"),
        new ReportKindInfo("Pdf", "PDF"),
        new ReportKindInfo("Word", "Word"),
        new ReportKindInfo("Agnostic", "Diğer dosya"),
    };

    public static bool IsSupported(string? kind) =>
        kind is not null && All.Any(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public static string GetDisplayName(string? kind) =>
        All.FirstOrDefault(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? kind ?? string.Empty;

    /// <summary>Doğru yazımı döndürür ("webi" -> "Webi").</summary>
    public static string? Normalize(string? kind) =>
        All.FirstOrDefault(k => string.Equals(k.Kind, kind, StringComparison.OrdinalIgnoreCase))?.Kind;
}
