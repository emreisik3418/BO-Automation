using BoAdmin.Application.Common;
using BoAdmin.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Ortam + modül bazlı yetkilendirmeyi kaydeder:
    ///  - Her modül için aynı adlı policy (Reports, Users, Groups), seçili ortama göre değerlendirilir
    ///  - "AnyModule" policy'si (karşılama ekranı)
    ///  - Fallback: [Authorize] yazılmayan her yer için kimliği doğrulanmış kullanıcı şartı
    ///  - Seçili ortam (ICurrentEnvironment, cookie)
    /// </summary>
    public static IServiceCollection AddModuleAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModuleAuthorizationOptions>(configuration.GetSection(ModuleAuthorizationOptions.SectionName));

        services.AddMemoryCache(); // kullanıcı bilgisi (AD görünen ad) önbelleği
        services.AddSingleton<EnvironmentCatalog>();
        services.AddSingleton<IModuleAccessService, ModuleAccessService>();

        // Seçili ortam istek bazında çözülür
        services.AddScoped<CurrentEnvironmentAccessor>();
        services.AddScoped<ICurrentEnvironment>(sp => sp.GetRequiredService<CurrentEnvironmentAccessor>());

        // Giriş ekranından geçmemiş kullanıcıyı giriş ekranına yönlendir
        services.Configure<MvcOptions>(o => o.Filters.Add<EntryGateFilter>());

        // Handler seçili ortamı kullandığı için scoped
        services.AddScoped<IAuthorizationHandler, ModuleAuthorizationHandler>();

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
