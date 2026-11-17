namespace BoAdmin.Web.Authorization;

/// <summary>
/// appsettings.json > "Authorization" bölümü.
/// IOptionsMonitor ile okunduğu için dosya değişince uygulamayı yeniden başlatmaya gerek yoktur.
/// </summary>
public sealed class ModuleAuthorizationOptions
{
    public const string SectionName = "Authorization";

    public Dictionary<string, ModuleAccessRule> Modules { get; set; } = new();

    /// <summary>Modül kuralını büyük/küçük harf duyarsız bulur. Tanımlı değilse null döner (erişim yok).</summary>
    public ModuleAccessRule? GetRule(string moduleName) =>
        Modules.FirstOrDefault(kv => string.Equals(kv.Key, moduleName, StringComparison.OrdinalIgnoreCase)).Value;
}

public sealed class ModuleAccessRule
{
    /// <summary>Sicil (sAMAccountName) listesi. "A12345" veya "DOMAIN\A12345" yazılabilir.</summary>
    public List<string> AllowedUsers { get; set; } = new();

    /// <summary>AD grup listesi. "DOMAIN\Grup" önerilir; domain yazılmazsa kullanıcının domain'i varsayılır.</summary>
    public List<string> AllowedGroups { get; set; } = new();
}
