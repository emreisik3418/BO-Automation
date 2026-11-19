using System.Text.RegularExpressions;

namespace BoAdmin.Application.Reports;

public sealed record CuidParseResult(
    IReadOnlyList<string> Cuids,
    IReadOnlyList<string> InvalidEntries,
    int DuplicateCount);

/// <summary>
/// Yüklenen TXT dosyasından CUID listesi çıkarır.
/// Kabul edilen biçim: her satıra bir CUID. Aynı satırda virgül, noktalı virgül,
/// boşluk veya sekme ile ayrılmış birden fazla CUID de olabilir.
/// "#" ile başlayan kısım açıklama sayılır ve yok sayılır.
/// </summary>
public static partial class CuidListParser
{
    /// <summary>BO CUID'leri harf, rakam, alt çizgi, tire ve noktadan oluşur (örn. AaBbCc1234567890xYz).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_\-\.]{8,64}$")]
    private static partial Regex CuidPattern();

    [GeneratedRegex(@"[,;\s]+")]
    private static partial Regex Separators();

    /// <summary>Tek bir değerin CUID biçimine uyup uymadığı.</summary>
    public static bool IsValidCuid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CuidPattern().IsMatch(value.Trim());

    public static CuidParseResult Parse(string content)
    {
        var cuids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal); // CUID büyük/küçük harf duyarlı
        var invalid = new List<string>();
        var duplicates = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];

            foreach (var token in Separators().Split(line))
            {
                var value = token.Trim().Trim('"', '\'', '﻿');
                if (value.Length == 0) continue;

                if (!CuidPattern().IsMatch(value))
                {
                    invalid.Add(value.Length > 80 ? value[..80] + "..." : value);
                    continue;
                }

                if (seen.Add(value)) cuids.Add(value);
                else duplicates++;
            }
        }

        return new CuidParseResult(cuids, invalid, duplicates);
    }
}
