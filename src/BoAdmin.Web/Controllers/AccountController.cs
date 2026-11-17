using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Models;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Controllers;

/// <summary>
/// Giriş ekranı. Kullanıcı Windows hesabıyla otomatik tanınır (şifre sorulmaz);
/// ekranda kimliğini görür, yetkili olduğu ortamı seçer ve uygulamaya girer.
/// </summary>
[Authorize]
public sealed class AccountController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly CurrentEnvironmentAccessor _environment;
    private readonly IModuleAccessService _accessService;
    private readonly ModuleRegistry _registry;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        ICurrentUser currentUser,
        CurrentEnvironmentAccessor environment,
        IModuleAccessService accessService,
        ModuleRegistry registry,
        ILogger<AccountController> logger)
    {
        _currentUser = currentUser;
        _environment = environment;
        _accessService = accessService;
        _registry = registry;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        var current = _environment.Current.Key;

        var environments = _accessService.GetAccessibleEnvironments(User)
            .Select(e => new LoginEnvironmentOption(
                e.Key,
                e.DisplayName,
                e.Color,
                IsSelected: string.Equals(e.Key, current, StringComparison.OrdinalIgnoreCase),
                Modules: _accessService.GetAccessibleModules(User, e.Key)
                    .Select(m => _registry.Find(m))
                    .Where(m => m is { IsAvailable: true })
                    .Select(m => m!.Title)
                    .ToList()))
            .ToList();

        if (environments.Count == 0)
            _logger.LogWarning("Yetkisiz giriş denemesi: {User}", _currentUser.UserName);

        return View(new LoginViewModel
        {
            DisplayName = _currentUser.DisplayName,
            Sicil = _currentUser.Sicil,
            UserName = _currentUser.UserName,
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null,
            Environments = environments
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(string environment, string? returnUrl)
    {
        var env = _accessService.GetAccessibleEnvironments(User)
            .FirstOrDefault(e => string.Equals(e.Key, environment, StringComparison.OrdinalIgnoreCase));

        if (env is null)
        {
            TempData["LoginError"] = "Seçilen ortama erişim yetkiniz yok.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        CurrentEnvironmentAccessor.WriteCookie(Response, env.Key);
        EntryGateFilter.MarkEntered(Response);

        _logger.LogInformation("Giriş: {User} ({Name}), ortam: {Environment}",
            _currentUser.UserName, _currentUser.DisplayName, env.Key);

        return returnUrl is not null && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        EntryGateFilter.Clear(Response);
        _logger.LogInformation("Çıkış: {User}", _currentUser.UserName);

        TempData["LoginInfo"] = "Uygulamadan çıkış yaptınız. Windows oturumunuz açık kalır.";
        return RedirectToAction(nameof(Login));
    }
}
