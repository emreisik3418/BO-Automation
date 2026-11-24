using System.Security.Claims;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Infrastructure.AppUsers;
using BoAdmin.Web.Modules;
using Microsoft.Extensions.Options;

namespace BoAdmin.Web.Authorization;

/// <summary>
/// Kullanıcının hangi ortamda hangi sayfalara erişebildiğini hesaplar.
///  - Yöneticiler (appsettings AccessControl:Admins): tüm ortamlar, tüm sayfalar + kullanıcı yönetimi
///  - Diğerleri: "Uygulama Kullanıcıları" ekranında atanan ortam × sayfa yetkileri
/// Menü, ortam seçici, policy'ler ve sayfa filtresi bu servisi kullandığı için
/// "menüde görünen" ile "erişilebilen" her zaman aynıdır.
/// </summary>
public interface IModuleAccessService
{
    bool IsAdmin(ClaimsPrincipal user);

    /// <summary>Modül erişimi: seçili ortamda modülün en az bir sayfasına yetki.</summary>
    bool HasAccess(ClaimsPrincipal user, string environment, string moduleName);

    bool HasPageAccess(ClaimsPrincipal user, string environment, string pageKey);

    IReadOnlyList<string> GetAccessibleModules(ClaimsPrincipal user, string environment);

    /// <summary>En az bir sayfaya erişimi olan ortamlar (menüdeki sırayla).</summary>
    IReadOnlyList<EnvironmentInfo> GetAccessibleEnvironments(ClaimsPrincipal user);
}

public sealed class ModuleAccessService : IModuleAccessService
{
    private readonly IOptionsMonitor<AccessControlOptions> _options;
    private readonly AppUserStore _store;
    private readonly ModuleRegistry _registry;
    private readonly EnvironmentCatalog _catalog;
    private readonly ILogger<ModuleAccessService> _logger;

    public ModuleAccessService(
        IOptionsMonitor<AccessControlOptions> options,
        AppUserStore store,
        ModuleRegistry registry,
        EnvironmentCatalog catalog,
        ILogger<ModuleAccessService> logger)
    {
        _options = options;
        _store = store;
        _registry = registry;
        _catalog = catalog;
        _logger = logger;
    }

    public bool IsAdmin(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        var admins = _options.CurrentValue.Admins;
        var identityName = user.Identity?.Name ?? string.Empty;
        return IsUserListed(identityName, admins.Users) || FindMatchingGroup(user, identityName, admins.Groups) is not null;
    }

    public bool HasAccess(ClaimsPrincipal user, string environment, string moduleName)
    {
        if (user.Identity?.IsAuthenticated != true) return false;

        if (string.Equals(moduleName, ModuleNames.Admin, StringComparison.OrdinalIgnoreCase))
            return IsAdmin(user);

        if (string.IsNullOrEmpty(environment)) return false;
        if (IsAdmin(user)) return true;

        var appUser = FindAppUser(user);
        if (appUser is null || !appUser.IsActive) return false;

        var pages = appUser.PagesIn(environment);
        if (string.Equals(moduleName, ModuleNames.AnyModule, StringComparison.OrdinalIgnoreCase))
            return pages.Count > 0;

        var module = _registry.Find(moduleName);
        return module is not null && module.Items.Any(i => pages.Contains(ModuleRegistry.PageKey(module, i), StringComparer.OrdinalIgnoreCase));
    }

    public bool HasPageAccess(ClaimsPrincipal user, string environment, string pageKey)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(environment)) return false;
        if (IsAdmin(user)) return true;
        return FindAppUser(user)?.HasPage(environment, pageKey) == true;
    }

    public IReadOnlyList<string> GetAccessibleModules(ClaimsPrincipal user, string environment) =>
        ModuleNames.All.Where(m => HasAccess(user, environment, m)).ToList();

    public IReadOnlyList<EnvironmentInfo> GetAccessibleEnvironments(ClaimsPrincipal user) =>
        _catalog.All.Where(e => HasAccess(user, e.Key, ModuleNames.AnyModule)).ToList();

    // ------------------------------------------------------------------

    private AppUser? FindAppUser(ClaimsPrincipal user) =>
        _store.Find(AccountNameHelper.GetSamAccountName(user.Identity?.Name));

    private static bool IsUserListed(string identityName, IEnumerable<string> users)
    {
        var sam = AccountNameHelper.GetSamAccountName(identityName);
        foreach (var entry in users)
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

    private string? FindMatchingGroup(ClaimsPrincipal user, string identityName, IEnumerable<string> groups)
    {
        var userDomain = AccountNameHelper.GetDomain(identityName);
        foreach (var entry in groups)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var group = entry.Trim();
            if (!group.Contains('\\') && userDomain.Length > 0)
                group = $"{userDomain}\\{group}";

            try
            {
                // WindowsPrincipal.IsInRole iç içe (nested) grupları da kapsar
                if (user.IsInRole(group)) return group;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AD grubu kontrol edilemedi: {Group}", group);
            }
        }
        return null;
    }
}
