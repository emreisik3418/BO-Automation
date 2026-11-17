using System.Globalization;

namespace BoAdmin.Web.Models;

/// <summary>Giriş ekranında seçilebilecek bir ortam.</summary>
public sealed record LoginEnvironmentOption(
    string Key,
    string DisplayName,
    string Color,
    bool IsSelected,
    IReadOnlyList<string> Modules);

public sealed class LoginViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public string Sicil { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string? ReturnUrl { get; init; }
    public IReadOnlyList<LoginEnvironmentOption> Environments { get; init; } = Array.Empty<LoginEnvironmentOption>();

    public bool HasAccess => Environments.Count > 0;

    /// <summary>Avatar için baş harfler: "Emre Işık" -> "Eİ"</summary>
    public string Initials
    {
        get
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var letters = parts.Length switch
            {
                0 => Sicil.Length > 0 ? Sicil[..1] : "?",
                1 => parts[0][..1],
                _ => string.Concat(parts[0][..1], parts[^1][..1])
            };
            return letters.ToUpper(tr);
        }
    }
}
