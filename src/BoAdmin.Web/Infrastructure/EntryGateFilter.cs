using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Kullanıcı tarayıcı oturumunda giriş ekranından geçmediyse (ortam seçip "Uygulamaya gir" demediyse)
/// sayfa isteklerini giriş ekranına yönlendirir. Kimlik doğrulama yine Windows Authentication'dır;
/// bu ekran kimliği gösterir ve ortam seçtirir.
/// </summary>
public sealed class EntryGateFilter : IActionFilter
{
    /// <summary>Tarayıcı kapanınca silinen oturum cookie'si.</summary>
    public const string CookieName = "BoAdmin.Entered";

    private static readonly HashSet<string> OpenControllers =
        new(StringComparer.OrdinalIgnoreCase) { "Account", "Error" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;

        // Yalnızca sayfa gösterimleri (GET); form gönderimleri ve hata sayfaları serbest
        if (!HttpMethods.IsGet(http.Request.Method)) return;
        if (http.User.Identity?.IsAuthenticated != true) return;
        if (http.Request.Cookies.ContainsKey(CookieName)) return;

        var controller = context.RouteData.Values["controller"]?.ToString();
        if (controller is not null && OpenControllers.Contains(controller)) return;

        var returnUrl = http.Request.Path + http.Request.QueryString;
        context.Result = new RedirectToActionResult("Login", "Account", new { returnUrl });
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    public static void MarkEntered(HttpResponse response) =>
        response.Cookies.Append(CookieName, "1", new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = response.HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax
            // Expires yok: tarayıcı kapanınca silinir
        });

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);
}
