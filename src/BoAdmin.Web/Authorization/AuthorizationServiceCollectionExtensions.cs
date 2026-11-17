using Microsoft.AspNetCore.Authorization;

namespace BoAdmin.Web.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Modül bazlı yetkilendirmeyi kaydeder:
    ///  - Her modül için aynı adlı policy (Reports, Users, Groups)
    ///  - "AnyModule" policy'si (karşılama ekranı)
    ///  - Fallback: [Authorize] yazılmayan her yer için kimliği doğrulanmış kullanıcı şartı
    /// </summary>
    public static IServiceCollection AddModuleAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModuleAuthorizationOptions>(configuration.GetSection(ModuleAuthorizationOptions.SectionName));

        services.AddMemoryCache(); // kullanıcı bilgisi (AD görünen ad) önbelleği
        services.AddSingleton<IModuleAccessService, ModuleAccessService>();
        services.AddSingleton<IAuthorizationHandler, ModuleAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var module in ModuleNames.All)
            {
                options.AddPolicy(module, policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ModuleRequirement(module)));
            }

            options.AddPolicy(ModuleNames.AnyModule, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ModuleRequirement(ModuleNames.AnyModule)));

            // Hiçbir sayfa anonim açılmasın
            options.FallbackPolicy = options.DefaultPolicy;
        });

        return services;
    }
}
