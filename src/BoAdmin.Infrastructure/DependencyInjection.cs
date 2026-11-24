using BoAdmin.Infrastructure.Bo.Credentials;
using System.Net.Http.Headers;
using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Audit;
using BoAdmin.Infrastructure.Audit;
using Microsoft.Extensions.Hosting;
using BoAdmin.Infrastructure.Bo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoAdmin.Infrastructure;

public static class DependencyInjection
{
    /// <summary>BO REST client, oturum yönetimi, rapor gateway'i ve audit log.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ---------------- SAP BO ayarları ----------------
        services.AddOptions<BoOptions>()
            .Bind(configuration.GetSection(BoOptions.SectionName))
            .Validate(o => o.Environments.Count > 0,
                "SapBo:Environments altında en az bir ortam tanımlanmalı.")
            .Validate(o => o.Find(o.DefaultEnvironment) is not null,
                "SapBo:DefaultEnvironment, SapBo:Environments altındaki ortamlardan biri olmalı.")
            .Validate(o => o.Environments.Values.All(e => Uri.TryCreate(e.BaseUrl, UriKind.Absolute, out _)),
                "Her ortamın BaseUrl değeri geçerli bir adres olmalı (örn. http://bo-server:6405/biprws).")
            .Validate(o => o.Environments.Values.All(e => e.AuthType is "secEnterprise" or "secWinAD" or "secLDAP"),
                "AuthType secEnterprise, secWinAD veya secLDAP olmalı.")
            .Validate(o => !o.Environments.Values.Any(e => e.UsesSbox) || Uri.TryCreate(o.Sbox.Endpoint, UriKind.Absolute, out _),
                "Sbox kullanan ortam var ama SapBo:Sbox:Endpoint geçerli bir adres değil.")
            // Şifre başlangıçta zorunlu tutulmaz: geliştirmede yalnızca TEST şifresi girilmiş olabilir.
            // Şifresi olmayan ortama geçilince ekranda açık hata mesajı gösterilir.
            .ValidateOnStart();

        services.AddMemoryCache();

        // ---------------- Oturum + HTTP ----------------
        services.AddSingleton<ISboxClient, SboxClient>();
        services.AddSingleton<IBoCredentialProvider, BoCredentialProvider>();
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

        // ---------------- Kullanıcı ve grup işlemleri ----------------
        services.AddScoped<IBoPrincipalGateway, BoPrincipalGateway>();

        // ---------------- Audit log (dosya) ----------------
        services.AddOptions<AuditOptions>()
            .Bind(configuration.GetSection(AuditOptions.SectionName));

        services.AddSingleton<FileAuditLogService>();
        services.AddSingleton<IAuditLogService>(sp => sp.GetRequiredService<FileAuditLogService>());

        return services;
    }

    /// <summary>
    /// Uygulama başlarken audit klasörünün yazılabilir olduğunu kontrol eder.
    /// Yazılamıyorsa kritik log düşer; uygulama yine açılır (kayıtlar uygulama loguna düşmeye devam eder).
    /// </summary>
    public static Task InitializeInfrastructureAsync(this IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("BoAdmin.Startup");
        var audit = services.GetRequiredService<FileAuditLogService>();

        try
        {
            audit.EnsureWritable();
            logger.LogInformation("Audit klasörü hazır: {Directory}", audit.AuditDirectory);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Audit klasörüne yazılamıyor: {Directory}. Uygulama havuzu kimliğine bu klasörde 'Modify' yetkisi verin.",
                audit.AuditDirectory);
        }

        // Sbox durumu: hangi ortam şifresini nereden alıyor
        var bo = services.GetRequiredService<IOptions<BoOptions>>().Value;
        logger.LogInformation("Sbox desteği derlemede: {Available}. Sbox adresi: {Endpoint}",
            SboxClient.IsAvailable ? "VAR" : "YOK (Reference.cs derlemeye girmedi)",
            string.IsNullOrWhiteSpace(bo.Sbox.Endpoint) ? "(boş)" : bo.Sbox.Endpoint);
        foreach (var (key, env) in bo.Ordered)
        {
            logger.LogInformation("Ortam {Environment}: kimlik kaynağı {Source}", key,
                env.UsesSbox ? $"Sbox ({env.Sbox})" : "appsettings / User Secrets (Sbox bölümü yok veya SensitiveDataName boş)");
        }

        return Task.CompletedTask;
    }

    private static void ConfigureBoHttpClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<BoOptions>>().Value;

        // BaseAddress yok: her istek seçili ortamın tam adresiyle gönderilir (BoUrl.Combine)
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.RequestTimeoutSeconds));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
