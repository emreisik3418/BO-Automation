using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.ViewComponents;

public sealed record SidebarItem(ModuleNavItem Item, bool IsActive);

public sealed record SidebarModule(ModuleDescriptor Module, bool IsActive, IReadOnlyList<SidebarItem> Items);

public sealed record SidebarModel(
    bool HomeActive,
    IReadOnlyList<SidebarModule> Modules,
    IReadOnlyList<ModuleDescriptor> ComingSoon);

/// <summary>
/// Sol menü (seçili ortama göre):
///  - Hazır modüller ve alt sayfaları yalnızca o ortamda yetkili kullanıcıya görünür
///  - Henüz yazılmamış modüller "Yakında" olarak pasif görünür
/// </summary>
public sealed class SidebarViewComponent : ViewComponent
{
    private readonly ModuleRegistry _registry;
    private readonly IModuleAccessService _accessService;
    private readonly ICurrentEnvironment _environment;

    public SidebarViewComponent(ModuleRegistry registry, IModuleAccessService accessService, ICurrentEnvironment environment)
    {
        _registry = registry;
        _accessService = accessService;
        _environment = environment;
    }

    public IViewComponentResult Invoke()
    {
        var controller = ViewContext.RouteData.Values["controller"]?.ToString();
        // Ortak sayfalar (ilerleme / sonuç) hangi menü öğesine ait olduğunu ViewData["NavAction"] ile bildirir
        var action = ViewContext.ViewData["NavAction"] as string
                     ?? ViewContext.RouteData.Values["action"]?.ToString();

        var modules = _registry.All
            .Where(m => m.IsAvailable && _accessService.HasAccess(UserClaimsPrincipal, _environment.Key, m.Name))
            .Select(m =>
            {
                var isActive = string.Equals(m.Controller, controller, StringComparison.OrdinalIgnoreCase);
                var items = m.Items
                    .Select(i => new SidebarItem(i, isActive && i.IsActiveFor(action)))
                    .ToList();
                return new SidebarModule(m, isActive, items);
            })
            .ToList();

        var comingSoon = _registry.All.Where(m => !m.IsAvailable).ToList();
        var homeActive = string.Equals(controller, "Home", StringComparison.OrdinalIgnoreCase);

        return View(new SidebarModel(homeActive, modules, comingSoon));
    }
}
