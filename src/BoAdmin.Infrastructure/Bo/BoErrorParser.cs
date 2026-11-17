using System.Text.Json;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// BO REST hata gövdesini okur. Beklenen biçimler (doğrulanmalı):
///   { "error_code": "RWS 00008", "message": "..." }
///   { "error": { "error_code": "...", "message": "..." } }
/// JSON değilse ham metnin ilk 500 karakteri mesaj olarak döner.
/// </summary>
internal static class BoErrorParser
{
    public static async Task<(string? Code, string Message)> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return (null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (string.IsNullOrWhiteSpace(body))
            return (null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (TryGet(root, "error", out var inner) && inner.ValueKind == JsonValueKind.Object)
                root = inner;

            var code = TryGet(root, "error_code", out var c) ? c.ToString() : null;
            var message = TryGet(root, "message", out var m) ? m.ToString() : null;

            if (!string.IsNullOrWhiteSpace(message))
                return (code, message);
        }
        catch (JsonException)
        {
            // JSON değil (ör. HTML hata sayfası)
        }

        return (null, body.Length > 500 ? body[..500] : body);
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;

        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }
}
