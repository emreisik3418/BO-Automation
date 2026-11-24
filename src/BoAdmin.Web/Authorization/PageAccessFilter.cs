using BoAdmin.Application.Common;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BoAdmin.Web.Authorization;

/// <summary>
/// Sayfa bazlı yetki: modül controller'ındaki bir action menüdeki bir sayfaya aitse
/// (ModuleNavItem.Action veya ActiveFor), kullanıcının seçili ortamda o sayfaya yetkisi aranır.
/// Hiçbir sayfaya ait olmayan ortak action'lar (iş ilerleme ekranı, şablon indirme vb.)
/// controller'daki modül policy'siyle korunur.
/// </summary>
public sealed class PageAccessFilter : IActionFilter
{
    private readonly ModuleRegistry _registry;
    private readonly IModuleAccessService _access;
    private readonly ICurrentEnvironment _environment;
    private readonly ILogger<PageAccessFilter> _logger;

    public PageAccessFilter(ModuleRegistry registry, IModuleAccessService access, ICurrentEnvironment environment,
        ILogger<PageAccessFilter> logger)
    {
        _registry = registry;
        _access = access;
        _environment = environment;
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return;

        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();
        var pageKey = _registry.FindPageKey(controller, action);
        if (pageKey is null) return;

        if (!_access.HasPageAccess(user, _environment.Key, pageKey))
        {
            _logger.LogWarning("Sayfa erişimi reddedildi: {User} -> {Environment}/{Page}", user.Identity?.Name, _environment.Key, pageKey);
            context.Result = new RedirectToActionResult("AccessDenied", "Error", null);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
