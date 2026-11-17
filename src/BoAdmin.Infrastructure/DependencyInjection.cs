using System.Net.Http.Headers;
using BoAdmin.Application.Abstractions;
using BoAdmin.Infrastructure.Bo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BoAdmin.Infrastructure;

public static class DependencyInjection
{
    /// <summary>BO REST client, oturum yönetimi ve (Parça 5'te) audit log kayıtları.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ---------------- SAP BO ayarları ----------------
        services.AddOptions<BoOptions>()
            .Bind(configuration.GetSection(BoOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "SapBo:BaseUrl geçerli bir adres olmalı (örn. http://bo-server:6405/biprws).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.UserName),
                "SapBo:UserName tanımlı değil.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Password),
                "SapBo:Password tanımlı değil. Geliştirmede User Secrets, prod'da SapBo__Password ortam değişkeni kullanın.")
            .Validate(o => o.AuthType is "secEnterprise" or "secWinAD" or "secLDAP",
                "SapBo:AuthType secEnterprise, secWinAD veya secLDAP olmalı.")
            .ValidateOnStart();

        services.AddMemoryCache();

        // ---------------- Oturum + HTTP ----------------
        services.AddSingleton<IBoSessionService, BoSessionService>();
        services.AddTransient<BoLogonTokenHandler>();

        // Logon/logoff için token handler'sız ayrı client (sonsuz döngüyü önler)
        services.AddHttpClient(BoSessionService.HttpClientName, ConfigureBoHttpClient);

        // Uygulamanın kullandığı typed client
        services.AddHttpClient<BoRestClient>(ConfigureBoHttpClient)
            .AddHttpMessageHandler<BoLogonTokenHandler>();

        services.AddHostedService<BoLogoffHostedService>();

        // ---------------- Rapor işlemleri ----------------
        // Klasör verisi IMemoryCache'te tutulur; servisler scoped (typed HttpClient'ı uzun süre tutmamak için)
        services.AddScoped<FolderPathCache>();
        services.AddScoped<IBoReportGateway, BoReportGateway>();

        // Parça 5: Audit log (EF Core)

        return services;
    }

    /// <summary>Uygulama başlarken altyapı hazırlığı (Parça 5'te audit tablosu oluşturma eklenecek).</summary>
    public static Task InitializeInfrastructureAsync(this IServiceProvider services) => Task.CompletedTask;

    private static void ConfigureBoHttpClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<BoOptions>>().Value;

        // Göreli yolların doğru birleşmesi için BaseAddress "/" ile bitmeli
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.RequestTimeoutSeconds));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
