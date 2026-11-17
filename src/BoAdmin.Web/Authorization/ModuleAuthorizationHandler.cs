using BoAdmin.Application.Common;
using Microsoft.AspNetCore.Authorization;

namespace BoAdmin.Web.Authorization;

/// <summary>
/// [Authorize(Policy = "Reports")] gibi policy'leri, kullanıcının SEÇTİĞİ ORTAM için değerlendirir.
/// Kural: kullanıcı o ortamın sicil listesinde VEYA izinli AD gruplarından birinde ise erişebilir.
/// </summary>
public sealed class ModuleAuthorizationHandler : AuthorizationHandler<ModuleRequirement>
{
    private readonly IModuleAccessService _accessService;
    private readonly ICurrentEnvironment _environment;
    private readonly ILogger<ModuleAuthorizationHandler> _logger;

    public ModuleAuthorizationHandler(
        IModuleAccessService accessService,
        ICurrentEnvironment environment,
        ILogger<ModuleAuthorizationHandler> logger)
    {
        _accessService = accessService;
        _environment = environment;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuleRequirement requirement)
    {
        if (_accessService.HasAccess(context.User, _environment.Key, requirement.ModuleName))
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Erişim reddedildi: {User} -> {Environment}/{Module}",
                context.User.Identity?.Name ?? "(anonim)", _environment.Key, requirement.ModuleName);
        }

        return Task.CompletedTask;
    }
}
