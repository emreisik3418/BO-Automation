using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Kullanıcının seçtiği BO ortamı. Seçim bir cookie'de tutulur.
/// Kural:
///  1) Cookie'deki ortam geçerli ve kullanıcı orada yetkiliyse o ortam
///  2) Değilse varsayılan ortam (yetkiliyse)
///  3) Değilse kullanıcının yetkili olduğu ilk ortam
///  4) Hiçbir ortamda yetkisi yoksa varsayılan ortam (yetki kontrolü erişimi zaten reddeder)
/// </summary>
public sealed class CurrentEnvironmentAccessor : ICurrentEnvironment
{
    public const string CookieName = "BoAdmin.Environment";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly EnvironmentCatalog _catalog;
    private readonly IModuleAccessService _accessService;

    private EnvironmentInfo? _resolved;

    private readonly BackgroundJobContext _job;

    public CurrentEnvironmentAccessor(
        IHttpContextAccessor httpContextAccessor,
        EnvironmentCatalog catalog,
        IModuleAccessService accessService,
        BackgroundJobContext job)
    {
        _job = job;
        _httpContextAccessor = httpContextAccessor;
        _catalog = catalog;
        _accessService = accessService;
    }

    /// <summary>Seçilen ortamı cookie'ye yazar (1 yıl).</summary>
    public static void WriteCookie(HttpResponse response, string environmentKey) =>
        response.Cookies.Append(CookieName, environmentKey, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = response.HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

    public string Key => Current.Key;
    public string DisplayName => Current.DisplayName;
    public string Color => Current.Color;

    /// <summary>Production gibi kritik ortamları ekranda vurgulamak için.</summary>
    public bool IsCritical => Color == "danger";

    public EnvironmentInfo Current => _resolved ??= Resolve();

    private EnvironmentInfo Resolve()
    {
        // Arka plan işi: iş başlatılırken seçili olan ortam
        if (_job.Environment is not null)
            return _job.Environment;

        var fallback = _catalog.Find(_catalog.DefaultKey)
            ?? new EnvironmentInfo(string.Empty, "(ortam tanımlı değil)", "secondary");

        var http = _httpContextAccessor.HttpContext;
        if (http?.User.Identity?.IsAuthenticated != true)
            return fallback;

        var accessible = _accessService.GetAccessibleEnvironments(http.User);
        if (accessible.Count == 0)
            return fallback;

        EnvironmentInfo? Pick(string? key) =>
            accessible.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        return Pick(http.Request.Cookies[CookieName])
               ?? Pick(_catalog.DefaultKey)
               ?? accessible[0];
    }
}
