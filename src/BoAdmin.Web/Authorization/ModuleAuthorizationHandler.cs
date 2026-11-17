using Microsoft.AspNetCore.Authorization;

namespace BoAdmin.Web.Authorization;

/// <summary>
/// [Authorize(Policy = "Reports")] gibi policy'leri değerlendirir.
/// Kural: kullanıcı sicil listesinde VEYA izinli AD gruplarından birinde ise erişebilir.
/// </summary>
public sealed class ModuleAuthorizationHandler : AuthorizationHandler<ModuleRequirement>
{
    private readonly IModuleAccessService _accessService;
    private readonly ILogger<ModuleAuthorizationHandler> _logger;

    public ModuleAuthorizationHandler(IModuleAccessService accessService, ILogger<ModuleAuthorizationHandler> logger)
    {
        _accessService = accessService;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuleRequirement requirement)
    {
        if (_accessService.HasAccess(context.User, requirement.ModuleName))
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Erişim reddedildi: {User} -> {Module}",
                context.User.Identity?.Name ?? "(anonim)", requirement.ModuleName);
        }

        return Task.CompletedTask;
    }
}
