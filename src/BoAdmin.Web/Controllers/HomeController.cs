using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Models;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Controllers;

/// <summary>Karşılama ekranı: kullanıcı adı, sicil ve erişebildiği modüller.</summary>
[Authorize]
public sealed class HomeController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IModuleAccessService _accessService;
    private readonly ModuleRegistry _registry;

    public HomeController(ICurrentUser currentUser, IModuleAccessService accessService, ModuleRegistry registry)
    {
        _currentUser = currentUser;
        _accessService = accessService;
        _registry = registry;
    }

    public IActionResult Index()
    {
        var accessible = _accessService.GetAccessibleModules(User);

        // Hiçbir modüle yetkisi yoksa doğrudan Erişim Reddedildi
        if (accessible.Count == 0)
            return RedirectToAction("AccessDenied", "Error");

        var modules = _registry.All
            .Where(m => accessible.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return View(new HomeViewModel(_currentUser.DisplayName, _currentUser.Sicil, _currentUser.UserName, modules));
    }
}
