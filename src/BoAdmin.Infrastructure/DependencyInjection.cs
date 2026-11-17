using System.Net.Http.Headers;
using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Audit;
using BoAdmin.Infrastructure.Audit;
using BoAdmin.Infrastructure.Bo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
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

        // ---------------- Audit log ----------------
        var auditConnection = configuration.GetConnectionString("AuditDb");
        if (string.IsNullOrWhiteSpace(auditConnection))
            throw new InvalidOperationException("ConnectionStrings:AuditDb tanımlı değil.");

        services.AddDbContextFactory<AuditDbContext>(options =>
            options.UseSqlServer(auditConnection, sql => sql.EnableRetryOnFailure(3)));

        services.AddSingleton<IAuditLogService, EfAuditLogService>();

        return services;
    }

    /// <summary>
    /// Uygulama başlarken audit tablosunu hazırlar:
    ///  - Veritabanı yoksa oluşturur (geliştirme / LocalDB)
    ///  - Veritabanı var ama tablo yoksa tabloyu oluşturur
    /// Yetki yoksa hata loglanır, uygulama yine açılır (kayıtlar dosya loguna düşmeye devam eder).
    /// Prod'da tabloyu DBA oluşturacaksa: db/AuditLog.sql
    /// </summary>
    public static async Task InitializeInfrastructureAsync(this IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("BoAdmin.Startup");

        try
        {
            var factory = services.GetRequiredService<IDbContextFactory<AuditDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var creator = db.GetService<IRelationalDatabaseCreator>();

            if (!await creator.ExistsAsync())
            {
                await creator.CreateAsync();
                await creator.CreateTablesAsync();
                logger.LogInformation("Audit veritabanı ve AuditLog tablosu oluşturuldu.");
                return;
            }

            var tableExists = await db.Database
                .SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]")
                .SingleAsync() == 1;

            if (!tableExists)
            {
                await creator.CreateTablesAsync();
                logger.LogInformation("AuditLog tablosu oluşturuldu.");
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Audit veritabanı hazırlanamadı. İşlem geçmişi veritabanına yazılamayacak (dosya logu devam eder). " +
                "Bağlantı dizesini ve yetkileri kontrol edin veya db/AuditLog.sql'i çalıştırın.");
        }
    }

    private static void ConfigureBoHttpClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<BoOptions>>().Value;

        // Göreli yolların doğru birleşmesi için BaseAddress "/" ile bitmeli
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.RequestTimeoutSeconds));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
