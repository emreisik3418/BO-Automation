using BoAdmin.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Controllers;

/// <summary>
/// Hata sayfaları:
///  /Error              -> beklenmeyen hata (UseExceptionHandler, prod)
///  /Error/{kod}        -> 403, 404 vb. (UseStatusCodePagesWithReExecute)
///  /Error/AccessDenied -> Erişim Reddedildi
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ErrorController : Controller
{
    private const string ErrorView = "~/Views/Shared/Error.cshtml";

    [Route("Error")]
    public IActionResult Index()
    {
        // Hata, UseExceptionHandler tarafından zaten loglanır
        return View(ErrorView, new ErrorViewModel
        {
            RequestId = HttpContext.TraceIdentifier,
            StatusCode = 500
        });
    }

    [Route("Error/{statusCode:int}")]
    public IActionResult Status(int statusCode)
    {
        if (statusCode == StatusCodes.Status403Forbidden)
            return AccessDenied();

        var model = statusCode switch
        {
            404 => new ErrorViewModel
            {
                StatusCode = 404,
                Title = "Sayfa bulunamadı",
                Message = "Aradığınız sayfa bulunamadı veya taşınmış olabilir."
            },
            _ => new ErrorViewModel
            {
                StatusCode = statusCode,
                Title = "İstek tamamlanamadı",
                Message = $"İsteğiniz tamamlanamadı (HTTP {statusCode})."
            }
        };

        model.RequestId = HttpContext.TraceIdentifier;
        Response.StatusCode = statusCode;
        return View(ErrorView, model);
    }

    [Route("Error/AccessDenied")]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("AccessDenied");
    }
}
