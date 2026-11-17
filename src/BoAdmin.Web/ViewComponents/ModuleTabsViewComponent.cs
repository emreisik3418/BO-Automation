using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.ViewComponents;

public sealed record ModuleTab(ModuleDescriptor Module, bool IsActive, bool IsEnabled);

/// <summary>
/// Üst menüdeki modül sekmeleri (seçili ortama göre).
///  - Hazır modüller yalnızca o ortamda yetkili kullanıcıya görünür.
///  - Henüz yazılmamış modüller herkese "Yakında" olarak pasif görünür.
/// </summary>
public sealed class ModuleTabsViewComponent : ViewComponent
{
    private readonly ModuleRegistry _registry;
    private readonly IModuleAccessService _accessService;
    private readonly ICurrentEnvironment _environment;

    public ModuleTabsViewComponent(ModuleRegistry registry, IModuleAccessService accessService, ICurrentEnvironment environment)
    {
        _registry = registry;
        _accessService = accessService;
        _environment = environment;
    }

    public IViewComponentResult Invoke()
    {
        var currentController = ViewContext.RouteData.Values["controller"]?.ToString();

        var tabs = _registry.All
            .Where(m => !m.IsAvailable || _accessService.HasAccess(UserClaimsPrincipal, _environment.Key, m.Name))
            .Select(m => new ModuleTab(
                m,
                IsActive: string.Equals(m.Controller, currentController, StringComparison.OrdinalIgnoreCase),
                IsEnabled: m.IsAvailable))
            .ToList();

        return View(tabs);
    }
}
