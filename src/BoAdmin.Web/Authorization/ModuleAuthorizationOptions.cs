namespace BoAdmin.Web.Authorization;

/// <summary>
/// Ortam bazlı modül yetkileri. "SapBo" bölümünden okunur; her ortamın "Authorization" alt bölümü:
///
/// "SapBo": {
///   "Environments": {
///     "TEST": { ..., "Authorization": { "Reports": { "AllowedUsers": [...], "AllowedGroups": [...] } } },
///     "PROD": { ..., "Authorization": { "Reports": { ... } } }
///   }
/// }
///
/// IOptionsMonitor ile okunduğu için dosya değişince uygulamayı yeniden başlatmaya gerek yoktur.
/// </summary>
public sealed class ModuleAuthorizationOptions
{
    public const string SectionName = "SapBo";

    public Dictionary<string, EnvironmentAuthorization> Environments { get; set; } = new();

    /// <summary>Ortam + modül kuralını büyük/küçük harf duyarsız bulur. Tanımlı değilse null (erişim yok).</summary>
    public ModuleAccessRule? GetRule(string environment, string moduleName)
    {
        var env = Environments
            .FirstOrDefault(kv => string.Equals(kv.Key, environment, StringComparison.OrdinalIgnoreCase)).Value;

        return env?.Authorization
            .FirstOrDefault(kv => string.Equals(kv.Key, moduleName, StringComparison.OrdinalIgnoreCase)).Value;
    }
}

public sealed class EnvironmentAuthorization
{
    /// <summary>Modül adı (Reports, Users, Groups) -> kural</summary>
    public Dictionary<string, ModuleAccessRule> Authorization { get; set; } = new();
}

public sealed class ModuleAccessRule
{
    /// <summary>Sicil (sAMAccountName) listesi. "A12345" veya "DOMAIN\A12345" yazılabilir.</summary>
    public List<string> AllowedUsers { get; set; } = new();

    /// <summary>AD grup listesi. "DOMAIN\Grup" önerilir; domain yazılmazsa kullanıcının domain'i varsayılır.</summary>
    public List<string> AllowedGroups { get; set; } = new();
}
