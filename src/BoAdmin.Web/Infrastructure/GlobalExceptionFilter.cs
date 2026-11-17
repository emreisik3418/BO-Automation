using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Controller'lardan kaçan hataları kullanıcı dostu Türkçe mesaja çevirir.
///  - AJAX/JSON isteklerinde: { error, traceId } JSON
///  - Normal isteklerde: Views/Shared/Error.cshtml
/// Teknik detay yalnızca loga yazılır.
/// </summary>
public sealed class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;
    private readonly IModelMetadataProvider _metadataProvider;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, IModelMetadataProvider metadataProvider)
    {
        _logger = logger;
        _metadataProvider = metadataProvider;
    }

    public void OnException(ExceptionContext context)
    {
        var ex = context.Exception;
        var traceId = context.HttpContext.TraceIdentifier;

        var (status, title, message) = ex switch
        {
            AppValidationException v => (400, "İşlem yapılamadı", v.UserMessage),
            BoUnavailableException b => (503, "SAP BO'ya ulaşılamıyor", b.UserMessage),
            BoAuthenticationException b => (502, "SAP BO oturum hatası", b.UserMessage),
            BoAccessDeniedException b => (502, "SAP BO yetki hatası", b.UserMessage),
            BoNotFoundException b => (404, "Nesne bulunamadı", b.UserMessage),
            BoException b => (502, "SAP BO hatası", b.UserMessage),
            OperationCanceledException => (499, "İstek iptal edildi", "İstek iptal edildi."),
            _ => (500, "Beklenmeyen hata",
                "İşleminiz sırasında beklenmeyen bir hata oluştu. Sorun devam ederse iz numarasıyla sistem yöneticisine başvurun.")
        };

        if (status >= 500 && ex is not BoException)
            _logger.LogError(ex, "İşlenmeyen hata. TraceId: {TraceId}", traceId);
        else
            _logger.LogWarning(ex, "{Title}: {Message} TraceId: {TraceId}", title, ex.Message, traceId);

        if (IsAjaxRequest(context.HttpContext.Request))
        {
            context.Result = new JsonResult(new { error = message, title, traceId }) { StatusCode = status };
        }
        else
        {
            var model = new ErrorViewModel
            {
                RequestId = traceId,
                StatusCode = status,
                Title = title,
                Message = message
            };

            context.Result = new ViewResult
            {
                ViewName = "~/Views/Shared/Error.cshtml",
                ViewData = new ViewDataDictionary<ErrorViewModel>(_metadataProvider, context.ModelState) { Model = model },
                StatusCode = status
            };
        }

        context.ExceptionHandled = true;
    }

    private static bool IsAjaxRequest(HttpRequest request) =>
        string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
        || request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
}
