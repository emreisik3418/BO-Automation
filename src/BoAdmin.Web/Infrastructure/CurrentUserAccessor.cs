using System.DirectoryServices.AccountManagement;
using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// ICurrentUser implementasyonu. Görünen adı AD'den bir kez okur ve 8 saat önbellekte tutar.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    private static readonly TimeSpan DisplayNameCacheDuration = TimeSpan.FromHours(8);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CurrentUserAccessor> _logger;

    public CurrentUserAccessor(
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache cache,
        ILogger<CurrentUserAccessor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
        _logger = logger;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string UserName =>
        _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;

    public string Sicil => AccountNameHelper.GetSamAccountName(UserName);

    public string DisplayName
    {
        get
        {
            var sicil = Sicil;
            if (string.IsNullOrEmpty(sicil)) return string.Empty;

            return _cache.GetOrCreate($"ad-displayname:{UserName.ToLowerInvariant()}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = DisplayNameCacheDuration;
                return LookupDisplayName(sicil) ?? sicil;
            }) ?? sicil;
        }
    }

    private string? LookupDisplayName(string sicil)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            // Sunucunun bağlı olduğu domain'e, uygulama havuzu kimliğiyle bağlanır
            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, sicil);
            return string.IsNullOrWhiteSpace(user?.DisplayName) ? null : user.DisplayName;
        }
        catch (Exception ex)
        {
            // AD'ye ulaşılamazsa uygulama çalışmaya devam etsin; sicil gösterilir
            _logger.LogWarning(ex, "AD'den görünen ad okunamadı: {Sicil}", sicil);
            return null;
        }
    }
}
