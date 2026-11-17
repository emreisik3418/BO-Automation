using Microsoft.AspNetCore.Mvc.Razor;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// View'ları modül klasörlerinde de arar:
///   /Modules/{Controller}/Views/{Controller}/{View}.cshtml
///   /Modules/{Controller}/Views/Shared/{View}.cshtml
/// Modül klasör adı controller adıyla aynı olmalı (Modules/Reports -> ReportsController).
/// Bulunamazsa standart /Views/... konumlarına düşer.
/// </summary>
public sealed class ModuleViewLocationExpander : IViewLocationExpander
{
    private static readonly string[] ModuleLocations =
    {
        "/Modules/{1}/Views/{1}/{0}.cshtml",
        "/Modules/{1}/Views/Shared/{0}.cshtml"
    };

    public void PopulateValues(ViewLocationExpanderContext context) { }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations) =>
        ModuleLocations.Concat(viewLocations);
}
