namespace BoAdmin.Web.Models;

/// <summary>Hata sayfası modeli. (VS şablonundaki ErrorViewModel'in yerine geçer.)</summary>
public class ErrorViewModel
{
    /// <summary>Destek için iz numarası (loglarda aranabilir).</summary>
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public int StatusCode { get; set; } = 500;

    public string Title { get; set; } = "Bir hata oluştu";

    public string Message { get; set; } = "İşleminiz sırasında beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.";
}
