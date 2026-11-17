using BoAdmin.Application;
using BoAdmin.Application.Common;
using BoAdmin.Infrastructure;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc.Razor;
using Serilog;

// Uygulama ayağa kalkarken oluşabilecek hataları da yakalamak için geçici logger
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("BoAdmin başlatılıyor...");

    var builder = WebApplication.CreateBuilder(args);

    // ---------------------------------------------------------------
    // Loglama (Serilog) - ayarlar appsettings.json > "Serilog"
    // ---------------------------------------------------------------
    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ---------------------------------------------------------------
    // Kimlik doğrulama: Windows Authentication (Negotiate)
    // IIS'te IIS'in Windows Auth'u, VS/Kestrel'de Negotiate handler'ı çalışır.
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
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    // 403 -> Erişim Reddedildi, 404 -> Bulunamadı vb.
    app.UseStatusCodePagesWithReExecute("/Error/{0}");

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
