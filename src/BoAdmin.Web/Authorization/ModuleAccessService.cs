using System.Security.Claims;
using BoAdmin.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace BoAdmin.Web.Authorization;

/// <summary>
/// Kullanıcının hangi ortamda hangi modüllere erişebildiğini hesaplar.
/// Hem authorization handler hem de menü/sekme/ortam seçici bu servisi kullanır,
/// böylece "menüde görünen" ile "erişilebilen" her zaman aynıdır.
/// </summary>
public interface IModuleAccessService
{
    bool HasAccess(ClaimsPrincipal user, string environment, string moduleName);

    IReadOnlyList<string> GetAccessibleModules(ClaimsPrincipal user, string environment);

    /// <summary>En az bir modüle erişimi olan ortamlar (menüdeki sırayla).</summary>
    IReadOnlyList<EnvironmentInfo> GetAccessibleEnvironments(ClaimsPrincipal user);
}

public sealed class ModuleAccessService : IModuleAccessService
{
    private readonly IOptionsMonitor<ModuleAuthorizationOptions> _options;
    private readonly EnvironmentCatalog _catalog;
    private readonly ILogger<ModuleAccessService> _logger;

    public ModuleAccessService(
        IOptionsMonitor<ModuleAuthorizationOptions> options,
        EnvironmentCatalog catalog,
        ILogger<ModuleAccessService> logger)
    {
        _options = options;
        _catalog = catalog;
        _logger = logger;
    }

    public bool HasAccess(ClaimsPrincipal user, string environment, string moduleName)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(environment)) return false;

        if (string.Equals(moduleName, ModuleNames.AnyModule, StringComparison.OrdinalIgnoreCase))
            return ModuleNames.All.Any(m => HasAccess(user, environment, m));

        var rule = _options.CurrentValue.GetRule(environment, moduleName);
        if (rule is null)
        {
            _logger.LogDebug("{Environment}/{Module} için yetki tanımı yok, erişim reddedildi.", environment, moduleName);
            return false;
        }

        var identityName = user.Identity?.Name ?? string.Empty;

        // 1) Sicil kontrolü
        if (IsUserAllowed(identityName, rule.AllowedUsers))
        {
            _logger.LogDebug("{User} -> {Environment}/{Module}: sicil listesi ile yetkili.", identityName, environment, moduleName);
            return true;
        }

        // 2) AD grup kontrolü
        var matchedGroup = FindMatchingGroup(user, identityName, rule.AllowedGroups);
        if (matchedGroup is not null)
        {
            _logger.LogDebug("{User} -> {Environment}/{Module}: '{Group}' grubu ile yetkili.", identityName, environment, moduleName, matchedGroup);
            return true;
        }

        return false;
    }

    public IReadOnlyList<string> GetAccessibleModules(ClaimsPrincipal user, string environment) =>
        ModuleNames.All.Where(m => HasAccess(user, environment, m)).ToList();

    public IReadOnlyList<EnvironmentInfo> GetAccessibleEnvironments(ClaimsPrincipal user) =>
        _catalog.All.Where(e => HasAccess(user, e.Key, ModuleNames.AnyModule)).ToList();

    private static bool IsUserAllowed(string identityName, IEnumerable<string> allowedUsers)
    {
        var sam = AccountNameHelper.GetSamAccountName(identityName);

        foreach (var entry in allowedUsers)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var value = entry.Trim();

            // "DOMAIN\A12345" yazılmışsa tam eşleşme, "A12345" yazılmışsa sicil eşleşmesi
            var matches = value.Contains('\\')
                ? string.Equals(value, identityName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(value, sam, StringComparison.OrdinalIgnoreCase);

            if (matches) return true;
        }

        return false;
    }

    private string? FindMatchingGroup(ClaimsPrincipal user, string identityName, IEnumerable<string> allowedGroups)
    {
        var userDomain = AccountNameHelper.GetDomain(identityName);

        foreach (var entry in allowedGroups)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var group = entry.Trim();

            // Domain yazılmamışsa kullanıcının domain'ini ekle: "BO_Admins" -> "DOMAIN\BO_Admins"
            if (!group.Contains('\\') && userDomain.Length > 0)
                group = $"{userDomain}\\{group}";

            try
            {
                // WindowsPrincipal.IsInRole iç içe (nested) grupları da kapsar
                if (user.IsInRole(group)) return group;
            }
            catch (Exception ex)
            {
                // Grup adı AD'de çözümlenemezse (yazım hatası vb.) diğer gruplara devam et
                _logger.LogWarning(ex, "AD grubu kontrol edilemedi: {Group}", group);
            }
        }

        return null;
    }
}
