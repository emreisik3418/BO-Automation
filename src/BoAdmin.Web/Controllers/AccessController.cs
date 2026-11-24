using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Infrastructure.AppUsers;
using BoAdmin.Web.Models;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BoAdmin.Web.Controllers;

/// <summary>
/// Uygulama Kullanıcıları (yalnızca yöneticiler).
///  GET  /Access              -> liste
///  GET  /Access/Edit/{sicil} -> düzenleme (sicil yoksa yeni kullanıcı)
///  POST /Access/Edit         -> kaydet
///  POST /Access/Delete       -> sil
/// Her değişiklik İşlem Geçmişi'ne (audit) yazılır.
/// </summary>
[Authorize(Policy = ModuleNames.Admin)]
public sealed class AccessController : Controller
{
    private readonly AppUserStore _store;
    private readonly ModuleRegistry _registry;
    private readonly EnvironmentCatalog _catalog;
    private readonly IOptionsMonitor<AccessControlOptions> _options;
    private readonly IAuditLogService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AccessController> _logger;

    public AccessController(AppUserStore store, ModuleRegistry registry, EnvironmentCatalog catalog,
        IOptionsMonitor<AccessControlOptions> options, IAuditLogService audit, ICurrentUser currentUser,
        ILogger<AccessController> logger)
    {
        _store = store;
        _registry = registry;
        _catalog = catalog;
        _options = options;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View(new AccessIndexViewModel
    {
        Users = _store.GetAll(),
        Environments = _catalog.All,
        PageCount = _registry.Pages.Count(),
        FilePath = _store.FilePath,
        AdminUsers = _options.CurrentValue.Admins.Users,
        AdminGroups = _options.CurrentValue.Admins.Groups
    });

    [HttpGet("Access/Edit/{sicil?}")]
    public IActionResult Edit(string? sicil, string? copyFrom)
    {
        AccessEditViewModel vm;
        var existing = _store.Find(sicil);

        if (existing is not null)
        {
            vm = FromUser(existing);
            vm.IsNew = false;
        }
        else
        {
            vm = new AccessEditViewModel { IsNew = true, Sicil = sicil ?? string.Empty };
            // Başka bir kullanıcının yetkileriyle başla
            if (_store.Find(copyFrom) is { } template)
                vm.Grants = FromUser(template).Grants;
        }

        return View(Fill(vm));
    }

    [HttpPost("Access/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AccessEditViewModel model)
    {
        var sicil = AppUserStore.Normalize(model.Sicil);
        if (sicil.Length == 0 || sicil.Length > 64 || sicil.Any(c => char.IsWhiteSpace(c) || c is ';' or ',' or '\'' or '"'))
        {
            model.ErrorMessage = "Geçerli bir sicil girin (ör. A12345).";
            return View("Edit", Fill(model));
        }

        if (model.IsNew && _store.Find(sicil) is not null)
        {
            model.ErrorMessage = $"{sicil} zaten listede. Listeden düzenleyin.";
            return View("Edit", Fill(model));
        }

        // Yalnızca tanımlı ortam ve sayfa anahtarları kabul edilir
        var validEnvs = _catalog.All.Select(e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validPages = _registry.Pages.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var permissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in model.Grants.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var parts = grant.Split('|');
            if (parts.Length != 2 || !validEnvs.Contains(parts[0]) || !validPages.Contains(parts[1])) continue;
            var env = validEnvs.First(e => string.Equals(e, parts[0], StringComparison.OrdinalIgnoreCase));
            if (!permissions.TryGetValue(env, out var list)) permissions[env] = list = new List<string>();
            list.Add(validPages.First(p => string.Equals(p, parts[1], StringComparison.OrdinalIgnoreCase)));
        }

        var before = _store.Find(sicil);
        var displayName = string.IsNullOrWhiteSpace(model.DisplayName)
            ? AdDirectory.TryGetDisplayName(sicil, _logger) ?? before?.DisplayName
            : model.DisplayName.Trim();

        var user = new AppUser
        {
            Sicil = sicil,
            DisplayName = displayName,
            IsActive = model.IsActive,
            Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(),
            Permissions = permissions
        };

        bool added;
        try
        {
            added = _store.Save(user, _currentUser.UserName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Kullanıcı dosyasına yazılamadı: {Path}", _store.FilePath);
            model.ErrorMessage = $"Kullanıcı dosyasına yazılamadı ({_store.FilePath}). Klasörde yazma yetkisini kontrol edin.";
            return View("Edit", Fill(model));
        }

        await WriteAuditAsync(added ? "AccessUserAdd" : "AccessUserUpdate", user,
            $"{(user.IsActive ? "Aktif" : "Pasif")}; {Describe(permissions)}" +
            (before is null ? "" : $" (önce: {(before.IsActive ? "Aktif" : "Pasif")}; {Describe(before.Permissions)})"));

        TempData["Success"] = added ? $"{sicil} eklendi." : $"{sicil} güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string sicil)
    {
        var user = _store.Find(sicil);
        if (user is null)
        {
            TempData["Warning"] = "Kullanıcı bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        _store.Delete(user.Sicil);
        await WriteAuditAsync("AccessUserRemove", user, $"Silindi. Önceki yetkiler: {Describe(user.Permissions)}");

        TempData["Success"] = $"{user.Sicil} silindi.";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------

    private AccessEditViewModel Fill(AccessEditViewModel vm)
    {
        vm.Environments = _catalog.All;
        vm.Modules = _registry.All.Where(m => m.IsAvailable).ToList();

        if (!vm.IsNew && _store.Find(vm.Sicil) is { } u)
        {
            vm.CreatedInfo = u.CreatedAt is null ? null : $"{u.CreatedAt:dd.MM.yyyy HH:mm} · {u.CreatedBy}";
            vm.UpdatedInfo = u.UpdatedAt is null ? null : $"{u.UpdatedAt:dd.MM.yyyy HH:mm} · {u.UpdatedBy}";
        }
        return vm;
    }

    private static AccessEditViewModel FromUser(AppUser u) => new()
    {
        Sicil = u.Sicil,
        DisplayName = u.DisplayName,
        IsActive = u.IsActive,
        Note = u.Note,
        Grants = u.Permissions.SelectMany(kv => kv.Value.Select(p => AccessEditViewModel.GrantKey(kv.Key, p))).ToList()
    };

    private static string Describe(Dictionary<string, List<string>> permissions) =>
        permissions.Count == 0
            ? "yetki yok"
            : string.Join(" | ", permissions.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}"));

    private Task WriteAuditAsync(string operation, AppUser user, string message) =>
        _audit.WriteAsync(new AuditEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Environment = "-",
            UserName = _currentUser.UserName,
            DisplayName = _currentUser.DisplayName,
            Module = "Access",
            Operation = operation,
            ObjectName = user.DisplayName is null ? user.Sicil : $"{user.Sicil} ({user.DisplayName})",
            ObjectKind = "AppUser",
            Success = true,
            Message = message
        });
}
