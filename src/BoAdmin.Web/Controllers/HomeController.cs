using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Application.Reports;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Models;
using BoAdmin.Web.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Controllers;

/// <summary>
/// Ana sayfa: karşılama, seçili ortamda erişilebilen modüller, son 30 günün işlem özeti ve son işlemler.
/// </summary>
[Authorize]
public sealed class HomeController : Controller
{
    private const int PeriodDays = 30;

    private readonly ICurrentUser _currentUser;
    private readonly CurrentEnvironmentAccessor _environment;
    private readonly IModuleAccessService _accessService;
    private readonly ModuleRegistry _registry;
    private readonly IAuditLogService _audit;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        ICurrentUser currentUser,
        CurrentEnvironmentAccessor environment,
        IModuleAccessService accessService,
        ModuleRegistry registry,
        IAuditLogService audit,
        ILogger<HomeController> logger)
    {
        _currentUser = currentUser;
        _environment = environment;
        _accessService = accessService;
        _registry = registry;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var env = _environment.Current;

        // Seçili ortam, kullanıcının yetkili olduğu ortamlardan seçilir.
        // Burada da modül yoksa hiçbir ortamda yetkisi yok demektir.
        var accessible = _accessService.GetAccessibleModules(User, env.Key);
        if (accessible.Count == 0)
            return RedirectToAction("AccessDenied", "Error");

        var modules = _registry.All
            .Where(m => m.IsAvailable && accessible.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var hasReports = accessible.Contains(ModuleNames.Reports, StringComparer.OrdinalIgnoreCase);

        DashboardStats? stats = null;
        IReadOnlyList<AuditEntry> recent = Array.Empty<AuditEntry>();

        if (hasReports)
        {
            try
            {
                (stats, recent) = await LoadDashboardAsync(env.Key, ct);
            }
            catch (Exception ex)
            {
                // Pano verisi okunamazsa ana sayfa yine açılsın
                _logger.LogWarning(ex, "Ana sayfa özeti okunamadı.");
            }
        }

        return View(new HomeViewModel
        {
            DisplayName = _currentUser.DisplayName,
            Sicil = _currentUser.Sicil,
            UserName = _currentUser.UserName,
            CurrentEnvironment = env,
            AccessibleEnvironments = _accessService.GetAccessibleEnvironments(User),
            Modules = modules,
            ComingSoon = _registry.All.Where(m => !m.IsAvailable).ToList(),
            HasReports = hasReports,
            Stats = stats,
            RecentActivity = recent
        });
    }

    private async Task<(DashboardStats, IReadOnlyList<AuditEntry>)> LoadDashboardAsync(string environment, CancellationToken ct)
    {
        AuditQuery Query(bool? success = null, string? operation = null) => new()
        {
            Environment = environment,
            Module = ReportService.ModuleName,
            From = DateTime.Today.AddDays(-PeriodDays),
            Success = success,
            Operation = operation,
            Page = 1,
            PageSize = 10
        };

        var all = await _audit.QueryAsync(Query(), ct);
        var ok = await _audit.QueryAsync(Query(success: true), ct);
        var fail = await _audit.QueryAsync(Query(success: false), ct);
        var backup = await _audit.QueryAsync(Query(success: true, operation: "BackupCopy"), ct);

        var stats = new DashboardStats(
            all.TotalCount ?? 0, ok.TotalCount ?? 0, fail.TotalCount ?? 0, backup.TotalCount ?? 0, PeriodDays);

        return (stats, all.Items.Take(6).ToList());
    }
}
