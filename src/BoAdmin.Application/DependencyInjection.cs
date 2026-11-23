using BoAdmin.Application.Principals;
using BoAdmin.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace BoAdmin.Application;

public static class DependencyInjection
{
    /// <summary>Uygulama servisleri. Yeni modüller buraya eklenir.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IPrincipalService, PrincipalService>();
        return services;
    }
}
