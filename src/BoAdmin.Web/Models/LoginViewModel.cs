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
    public string Initials => BoAdmin.Web.Infrastructure.UserInitials.From(DisplayName, Sicil);
}
