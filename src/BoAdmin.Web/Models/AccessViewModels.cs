using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Infrastructure.AppUsers;
using BoAdmin.Web.Modules;

namespace BoAdmin.Web.Models;

public sealed class AccessIndexViewModel
{
    public required IReadOnlyList<AppUser> Users { get; init; }
    public required IReadOnlyList<EnvironmentInfo> Environments { get; init; }
    public required int PageCount { get; init; }
    public required string FilePath { get; init; }
    public required IReadOnlyList<string> AdminUsers { get; init; }
    public required IReadOnlyList<string> AdminGroups { get; init; }
}

/// <summary>Kullanıcı ekleme / düzenleme formu.</summary>
public sealed class AccessEditViewModel
{
    public bool IsNew { get; set; }
    public string Sicil { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }

    /// <summary>Seçili kutular: "ORTAM|Reports.Index" biçiminde.</summary>
    public List<string> Grants { get; set; } = new();

    // ---- Ekran için ----
    public IReadOnlyList<EnvironmentInfo> Environments { get; set; } = Array.Empty<EnvironmentInfo>();
    public IReadOnlyList<ModuleDescriptor> Modules { get; set; } = Array.Empty<ModuleDescriptor>();
    public string? ErrorMessage { get; set; }
    public string? CreatedInfo { get; set; }
    public string? UpdatedInfo { get; set; }

    public static string GrantKey(string environment, string pageKey) => $"{environment}|{pageKey}";

    public bool IsGranted(string environment, string pageKey) =>
        Grants.Contains(GrantKey(environment, pageKey), StringComparer.OrdinalIgnoreCase);
}
