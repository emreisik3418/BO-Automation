using BoAdmin.Application.Audit;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Modules;

namespace BoAdmin.Web.Models;

/// <summary>Ana sayfa: seçili ortamda son 30 günün özeti.</summary>
public sealed record DashboardStats(int Total, int Success, int Failed, int BackedUp, int PeriodDays);

public sealed class HomeViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public string Sicil { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;

    public EnvironmentInfo CurrentEnvironment { get; init; } = new(string.Empty, string.Empty, "secondary");
    public IReadOnlyList<EnvironmentInfo> AccessibleEnvironments { get; init; } = Array.Empty<EnvironmentInfo>();

    /// <summary>Seçili ortamda erişilebilen hazır modüller.</summary>
    public IReadOnlyList<ModuleDescriptor> Modules { get; init; } = Array.Empty<ModuleDescriptor>();

    public IReadOnlyList<ModuleDescriptor> ComingSoon { get; init; } = Array.Empty<ModuleDescriptor>();

    /// <summary>Rapor Yönetişimi yetkisi varsa dolu.</summary>
    public DashboardStats? Stats { get; init; }

    public IReadOnlyList<AuditEntry> RecentActivity { get; init; } = Array.Empty<AuditEntry>();

    public bool HasReports { get; init; }

    public string FirstName =>
        DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? DisplayName;

    public string Greeting => DateTime.Now.Hour switch
    {
        < 6 => "İyi geceler",
        < 12 => "Günaydın",
        < 18 => "İyi günler",
        _ => "İyi akşamlar"
    };

    /// <summary>"5 dk önce", "3 sa önce", "dün 14:20", "12.11.2026"</summary>
    public static string TimeAgo(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var diff = DateTime.Now - local;

        if (diff.TotalMinutes < 1) return "az önce";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} dk önce";
        if (diff.TotalHours < 24 && local.Date == DateTime.Today) return $"{(int)diff.TotalHours} sa önce";
        if (local.Date == DateTime.Today.AddDays(-1)) return $"dün {local:HH:mm}";
        return local.ToString("dd.MM.yyyy HH:mm");
    }
}
