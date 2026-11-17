using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Controllers;

/// <summary>Üst menüdeki ortam seçici.</summary>
[Authorize]
public sealed class EnvironmentController : Controller
{
    private static readonly string[] ModuleControllers = { "Reports", "Users", "Groups" };

    private readonly IModuleAccessService _accessService;
    private readonly ILogger<EnvironmentController> _logger;

    public EnvironmentController(IModuleAccessService accessService, ILogger<EnvironmentController> logger)
    {
        _accessService = accessService;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Select(string key, string? returnUrl)
    {
        var env = _accessService.GetAccessibleEnvironments(User)
            .FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        if (env is null)
        {
            TempData["Warning"] = "Seçilen ortama erişim yetkiniz yok.";
            return RedirectToAction("Index", "Home");
        }

        CurrentEnvironmentAccessor.WriteCookie(Response, env.Key);

        _logger.LogInformation("{User} ortam değiştirdi: {Environment}", User.Identity?.Name, env.Key);
        TempData["Success"] = $"{env.DisplayName} ortamına geçildi.";

        // Önizleme/sonuç sayfaları önceki ortamın verisini taşır; modülün ana sayfasına dön
        return Redirect(GetModuleHome(returnUrl));
    }

    private string GetModuleHome(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl))
            return Url.Action("Index", "Home")!;

        var firstSegment = returnUrl.TrimStart('~', '/').Split('/', '?')[0];
        var controller = ModuleControllers.FirstOrDefault(c => string.Equals(c, firstSegment, StringComparison.OrdinalIgnoreCase));

        return controller is null
            ? Url.Action("Index", "Home")!
            : Url.Action("Index", controller)!;
    }
}
