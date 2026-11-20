using BoAdmin.Application;
using BoAdmin.Application.Common;
using BoAdmin.Infrastructure;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Razor;
using Serilog;

// Uygulama ayağa kalkarken oluşabilecek hataları da yakalamak için geçici logger
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("BoAdmin başlatılıyor...");

    // Exe başka bir klasörden veya Windows servisi olarak çalıştırıldığında da
    // appsettings ve wwwroot exe'nin yanındaki klasörden okunur.
    // (VS'de geliştirmede bin altında wwwroot olmadığı için proje klasörü kullanılır.)
    var exeFolder = AppContext.BaseDirectory;
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = Directory.Exists(Path.Combine(exeFolder, "wwwroot")) ? exeFolder : null
    });

    // Windows servisi olarak kurulursa servis yaşam döngüsüne bağlanır; exe çift tıkla/konsoldan
    // çalıştırıldığında etkisizdir.
    builder.Host.UseWindowsService(options => options.ServiceName = "BoAdmin");

    // ---------------------------------------------------------------
    // Loglama (Serilog) - ayarlar appsettings.json > "Serilog"
    // ---------------------------------------------------------------
    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ---------------------------------------------------------------
    // Kimlik doğrulama: Windows Authentication (Negotiate)
    // Exe (Kestrel) olarak çalışırken Negotiate handler'ı Kerberos/NTLM ile kullanıcıyı tanır.
    // ---------------------------------------------------------------
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    // ---------------------------------------------------------------
    // Yetkilendirme: modül bazlı policy'ler (Reports, Users, Groups)
    // appsettings.json > "Authorization:Modules"
    // ---------------------------------------------------------------
    builder.Services.AddModuleAuthorization(builder.Configuration);

    // ---------------------------------------------------------------
    // MVC + modül klasörlerinden view bulma + merkezi hata filtresi
    // ---------------------------------------------------------------
    builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add<GlobalExceptionFilter>();
    });

    builder.Services.Configure<RazorViewEngineOptions>(options =>
    {
        // /Modules/{Modül}/Views/{Controller}/{View}.cshtml
        options.ViewLocationExpanders.Add(new ModuleViewLocationExpander());
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUserAccessor>();
    builder.Services.AddSingleton<ModuleRegistry>();

    // ---------------------------------------------------------------
    // Katmanlar
    // ---------------------------------------------------------------
    builder.Services.AddApplication();                         // servisler
    builder.Services.AddInfrastructure(builder.Configuration); // BO REST client, audit

    var app = builder.Build();

    // Audit tablosu yoksa oluştur
    await app.Services.InitializeInfrastructureAsync();

    // ---------------------------------------------------------------
    // HTTP pipeline
    // ---------------------------------------------------------------
    // HTTPS uç noktası tanımlıysa (appsettings.Production.json > Kestrel:Endpoints:Https)
    // HTTP istekleri HTTPS'e yönlendirilir; yalnızca HTTP ile yayında yönlendirme yapılmaz.
    var httpsEnabled = !string.IsNullOrWhiteSpace(app.Configuration["Kestrel:Endpoints:Https:Url"])
                       || app.Environment.IsDevelopment();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        if (httpsEnabled) app.UseHsts();
    }

    // 403 -> Erişim Reddedildi, 404 -> Bulunamadı vb.
    app.UseStatusCodePagesWithReExecute("/Error/{0}");

    // 401'i hata sayfasına yönlendirme: Windows Auth (Negotiate/Kerberos/NTLM) el sıkışması
    // birden fazla 401 ile ilerler. Araya hata sayfası girerse
    // "Attempting to use an incomplete authentication context" hatası oluşur.
    app.Use(async (context, next) =>
    {
        await next();

        if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            var statusCodePages = context.Features.Get<IStatusCodePagesFeature>();
            if (statusCodePages is not null)
                statusCodePages.Enabled = false;
        }
    });

    if (httpsEnabled)
        app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    // Her isteği tek satır logla (kullanıcı adıyla)
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diag, http) =>
            diag.Set("UserName", http.User.Identity?.Name ?? "(anonim)");
    });

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    Log.Information("BoAdmin hazır. Ortam: {Environment}, klasör: {ContentRoot}",
        app.Environment.EnvironmentName, app.Environment.ContentRootPath);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Uygulama beklenmedik şekilde sonlandı.");
}
finally
{
    Log.CloseAndFlush();
}
