using System.Globalization;
using System.Text.Json;

namespace BoAdmin.Infrastructure.Bo.Models;

/// <summary>
/// cmsquery sonucundaki tek bir kayıt. Alan adları büyük/küçük harf duyarsızdır
/// (BO sürümüne göre "SI_ID" veya "si_id" gelebilir - doğrulanmalı).
/// </summary>
public sealed class CmsEntry
{
    private readonly Dictionary<string, JsonElement> _props;

    public CmsEntry(JsonElement element)
    {
        _props = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object) return;

        foreach (var p in element.EnumerateObject())
            _props[p.Name] = p.Value.Clone();
    }

    public IReadOnlyDictionary<string, JsonElement> Properties => _props;

    public bool Has(string name) => _props.ContainsKey(name);

    public string? GetString(string name)
    {
        if (!_props.TryGetValue(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText()
        };
    }

    public int? GetInt(string name)
    {
        if (!_props.TryGetValue(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String &&
            int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
        return null;
    }

    public bool? GetBool(string name)
    {
        if (!_props.TryGetValue(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : null,
            _ => null
        };
    }

    /// <summary>
    /// BO tarih formatı sürüme/locale'e göre değişebilir (doğrulanmalı).
    /// Bilinen biçimler sırayla denenir.
    /// </summary>
    public DateTime? GetDateTime(string name)
    {
        var s = GetString(name);
        if (string.IsNullOrWhiteSpace(s)) return null;

        string[] formats =
        {
            "MMM d, yyyy h:mm tt",        // Jun 15, 2021 3:44 PM
            "MMM d, yyyy h:mm:ss tt",
            "yyyy-MM-dd'T'HH:mm:ss.fffK", // ISO 8601
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy.MM.dd.HH.mm.ss",        // CMS sorgu formatı
        };

        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var dt))
            return dt;

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
            return dt;

        return null;
    }
}

/// <summary>cmsquery sayfa sonucu.</summary>
public sealed record CmsQueryResult(IReadOnlyList<CmsEntry> Entries, bool HasMore);
