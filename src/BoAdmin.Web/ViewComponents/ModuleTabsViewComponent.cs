using BoAdmin.Web.Authorization;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.ViewComponents;

public sealed record ModuleTab(ModuleDescriptor Module, bool IsActive, bool IsEnabled);

/// <summary>
/// Üst menüdeki modül sekmeleri.
///  - Hazır modüller yalnızca yetkili kullanıcıya görünür.
///  - Henüz yazılmamış modüller herkese "Yakında" olarak pasif görünür.
/// </summary>
public sealed class ModuleTabsViewComponent : ViewComponent
{
    private readonly ModuleRegistry _registry;
    private readonly IModuleAccessService _accessService;

    public ModuleTabsViewComponent(ModuleRegistry registry, IModuleAccessService accessService)
    {
        _registry = registry;
        _accessService = accessService;
    }

    public IViewComponentResult Invoke()
    {
        var currentController = ViewContext.RouteData.Values["controller"]?.ToString();

        var tabs = _registry.All
            .Where(m => !m.IsAvailable || _accessService.HasAccess(UserClaimsPrincipal, m.Name))
            .Select(m => new ModuleTab(
                m,
                IsActive: string.Equals(m.Controller, currentController, StringComparison.OrdinalIgnoreCase),
                IsEnabled: m.IsAvailable))
            .ToList();

        return View(tabs);
    }
}
