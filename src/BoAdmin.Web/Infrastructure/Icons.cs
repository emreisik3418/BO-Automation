using Microsoft.AspNetCore.Html;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Satır içi SVG ikonlar (harici ikon kütüphanesi/CDN gerektirmez).
/// Kullanım: @Icons.Get("search")
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = "<path d=\"M3 10.5 12 3l9 7.5\"/><path d=\"M5 9.5V20a1 1 0 0 0 1 1h4v-6h4v6h4a1 1 0 0 0 1-1V9.5\"/>",
        ["reports"] = "<path d=\"M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z\"/><path d=\"M14 3v6h6\"/><path d=\"M8 17v-3M12 17v-5M16 17v-2\"/>",
        ["search"] = "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-3.5-3.5\"/>",
        ["layers"] = "<path d=\"m12 3 9 5-9 5-9-5z\"/><path d=\"m3 13 9 5 9-5\"/>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>",
        ["user"] = "<circle cx=\"12\" cy=\"8\" r=\"4\"/><path d=\"M4 21a8 8 0 0 1 16 0\"/>",
        ["users"] = "<circle cx=\"9\" cy=\"8\" r=\"3.5\"/><path d=\"M2.5 20a6.5 6.5 0 0 1 13 0\"/><path d=\"M16 4.5a3.5 3.5 0 0 1 0 7M18 14.5a6.5 6.5 0 0 1 3.5 5.5\"/>",
        ["logout"] = "<path d=\"M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3\"/><path d=\"M10 8l-4 4 4 4M6 12h10\"/>",
        ["menu"] = "<path d=\"M4 6h16M4 12h16M4 18h16\"/>",
        ["close"] = "<path d=\"M6 6l12 12M18 6 6 18\"/>",
        ["chevron-down"] = "<path d=\"m6 9 6 6 6-6\"/>",
        ["chevron-right"] = "<path d=\"m9 6 6 6-6 6\"/>",
        ["arrow-right"] = "<path d=\"M5 12h14M13 6l6 6-6 6\"/>",
        ["check"] = "<path d=\"m5 12.5 4.5 4.5L19 7.5\"/>",
        ["x"] = "<path d=\"M7 7l10 10M17 7 7 17\"/>",
        ["alert"] = "<path d=\"M12 3 2 20h20z\"/><path d=\"M12 10v4M12 17h.01\"/>",
        ["trash"] = "<path d=\"M4 7h16M10 11v6M14 11v6\"/><path d=\"M6 7l1 13a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1l1-13M9 7V4h6v3\"/>",
        ["archive"] = "<rect x=\"3\" y=\"4\" width=\"18\" height=\"4\" rx=\"1\"/><path d=\"M5 8v11a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8M10 12h4\"/>",
        ["copy"] = "<rect x=\"8\" y=\"8\" width=\"12\" height=\"12\" rx=\"2\"/><path d=\"M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2\"/>",
        ["folder"] = "<path d=\"M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z\"/>",
        ["upload"] = "<path d=\"M12 16V4M7 9l5-5 5 5\"/><path d=\"M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3\"/>",
        ["download"] = "<path d=\"M12 4v12M7 11l5 5 5-5\"/><path d=\"M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3\"/>",
        ["filter"] = "<path d=\"M4 5h16l-6 8v5l-4 2v-7z\"/>",
        ["calendar"] = "<rect x=\"3\" y=\"5\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M3 10h18M8 3v4M16 3v4\"/>",
        ["activity"] = "<path d=\"M3 12h4l3-8 4 16 3-8h4\"/>",
        ["shield"] = "<path d=\"M12 3 4 6v6c0 4.5 3.4 8.3 8 9 4.6-.7 8-4.5 8-9V6z\"/>",
        ["server"] = "<rect x=\"3\" y=\"4\" width=\"18\" height=\"7\" rx=\"2\"/><rect x=\"3\" y=\"13\" width=\"18\" height=\"7\" rx=\"2\"/><path d=\"M7 7.5h.01M7 16.5h.01\"/>",
        ["sparkle"] = "<path d=\"M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2.5 2.5M15.5 15.5 18 18M6 18l2.5-2.5M15.5 8.5 18 6\"/>",
        ["edit"] = "<path d=\"M4 20h4L19 9l-4-4L4 16z\"/><path d=\"m13.5 6.5 4 4\"/>",
        ["sheet"] = "<rect x=\"4\" y=\"3\" width=\"16\" height=\"18\" rx=\"2\"/><path d=\"M4 9h16M4 15h16M10 3v18\"/>",
        ["tag"] = "<path d=\"M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9z\"/><circle cx=\"7.5\" cy=\"7.5\" r=\"1.5\"/>",
        ["lock"] = "<rect x=\"4\" y=\"11\" width=\"16\" height=\"10\" rx=\"2\"/><path d=\"M8 11V7a4 4 0 0 1 8 0v4\"/>",
    };

    public static IHtmlContent Get(string name, int size = 18, string? cssClass = null)
    {
        var path = Paths.TryGetValue(name, out var p) ? p : Paths["sparkle"];
        var cls = string.IsNullOrEmpty(cssClass) ? "icon" : $"icon {cssClass}";
        return new HtmlString(
            $"<svg class=\"{cls}\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
            $"stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{path}</svg>");
    }
}
