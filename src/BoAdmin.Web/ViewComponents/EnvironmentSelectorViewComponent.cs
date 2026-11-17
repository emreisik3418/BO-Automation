using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.ViewComponents;

public sealed record EnvironmentSelectorModel(EnvironmentInfo Current, IReadOnlyList<EnvironmentInfo> Accessible, string ReturnUrl);

/// <summary>Üst menüde aktif ortam rozeti + yetkili olunan ortamlar arasında geçiş.</summary>
public sealed class EnvironmentSelectorViewComponent : ViewComponent
{
    private readonly CurrentEnvironmentAccessor _environment;
    private readonly IModuleAccessService _accessService;

    public EnvironmentSelectorViewComponent(CurrentEnvironmentAccessor environment, IModuleAccessService accessService)
    {
        _environment = environment;
        _accessService = accessService;
    }

    public IViewComponentResult Invoke()
    {
        var accessible = _accessService.GetAccessibleEnvironments(UserClaimsPrincipal);
        var returnUrl = HttpContext.Request.Path + HttpContext.Request.QueryString;

        return View(new EnvironmentSelectorModel(_environment.Current, accessible, returnUrl));
    }
}
