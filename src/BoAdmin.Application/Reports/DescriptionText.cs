namespace BoAdmin.Application.Reports;

/// <summary>Rapor açıklaması (description) için ortak kurallar.</summary>
public static class DescriptionText
{
    /// <summary>Açıklama için güvenli üst sınır.</summary>
    public const int MaxLength = 2000;

    /// <summary>Baştaki/sondaki boşlukları atar, satır sonlarını "\n" yapar.</summary>
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    /// <summary>İki açıklama aynı mı (normalize edilerek, büyük/küçük harf duyarlı).</summary>
    public static bool Same(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>Yeni açıklama kuralları. Hata yoksa null.</summary>
    public static string? Validate(string? value)
    {
        var text = Normalize(value);
        if (text.Length == 0) return "Yeni açıklama boş.";
        if (text.Length > MaxLength) return $"Açıklama en fazla {MaxLength} karakter olabilir ({text.Length}).";
        if (text.Any(c => char.IsControl(c) && c != '\n' && c != '\t')) return "Açıklama görünmeyen kontrol karakteri içeriyor.";
        return null;
    }
}
