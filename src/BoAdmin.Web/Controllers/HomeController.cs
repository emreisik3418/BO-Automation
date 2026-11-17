using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Models;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Controllers;

/// <summary>Karşılama ekranı: kullanıcı adı, sicil, seçili ortam ve o ortamda erişebildiği modüller.</summary>
[Authorize]
public sealed class HomeController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentEnvironment _environment;
    private readonly IModuleAccessService _accessService;
    private readonly ModuleRegistry _registry;

    public HomeController(
        ICurrentUser currentUser,
        ICurrentEnvironment environment,
        IModuleAccessService accessService,
        ModuleRegistry registry)
    {
        _currentUser = currentUser;
        _environment = environment;
        _accessService = accessService;
        _registry = registry;
    }

    public IActionResult Index()
    {
        // Seçili ortam, kullanıcının yetkili olduğu ortamlardan seçilir.
        // Burada da modül yoksa hiçbir ortamda yetkisi yok demektir.
        var accessible = _accessService.GetAccessibleModules(User, _environment.Key);
        if (accessible.Count == 0)
            return RedirectToAction("AccessDenied", "Error");

        var modules = _registry.All
            .Where(m => accessible.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var environments = _accessService.GetAccessibleEnvironments(User).Select(e => e.DisplayName).ToList();

        return View(new HomeViewModel(
            _currentUser.DisplayName, _currentUser.Sicil, _currentUser.UserName,
            _environment.DisplayName, environments, modules));
    }
}
