using BoAdmin.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace BoAdmin.Application;

public static class DependencyInjection
{
    /// <summary>Uygulama servisleri. Yeni modüller (Users, Groups) buraya eklenecek.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();
        return services;
    }
}
