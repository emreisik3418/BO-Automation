namespace BoAdmin.Web.Authorization;

/// <summary>Windows hesap adı yardımcıları: "DOMAIN\A12345" veya "a12345@domain.local".</summary>
public static class AccountNameHelper
{
    /// <summary>"DOMAIN\A12345" -> "A12345", "a12345@domain.local" -> "a12345"</summary>
    public static string GetSamAccountName(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName)) return string.Empty;

        var slash = identityName.IndexOf('\\');
        if (slash >= 0) return identityName[(slash + 1)..];

        var at = identityName.IndexOf('@');
        return at > 0 ? identityName[..at] : identityName;
    }

    /// <summary>"DOMAIN\A12345" -> "DOMAIN". Domain yoksa boş döner.</summary>
    public static string GetDomain(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName)) return string.Empty;
        var slash = identityName.IndexOf('\\');
        return slash > 0 ? identityName[..slash] : string.Empty;
    }
}
