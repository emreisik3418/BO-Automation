using System.Globalization;
using BoAdmin.Application.Common;
using BoAdmin.Application.Principals;

namespace BoAdmin.Web.Modules.Users.ViewModels;

public sealed class UserSearchViewModel
{
    /// <summary>Arama formundaki gizli alan; yalnızca bu varsa BO'da arama yapılır.</summary>
    public const string SearchFlag = "search";

    public static readonly int[] PageSizes = { 20, 50, 100, 200 };

    public UserSearchCriteria Criteria { get; init; } = new();
    public PagedResult<UserListItemDto>? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Searched { get; set; }

    public static Dictionary<string, string> QuickFilter(UserStatusFilter? status = null, int? inactiveDays = null,
        UserSortField sortBy = UserSortField.Name, bool descending = false)
    {
        var d = new Dictionary<string, string>
        {
            [SearchFlag] = "1",
            ["Criteria.SortBy"] = sortBy.ToString(),
            ["Criteria.SortDescending"] = descending.ToString(),
            ["Criteria.PageSize"] = "20"
        };
        if (status is not null) d["Criteria.Status"] = status.Value.ToString();
        if (inactiveDays is not null)
            d["Criteria.LastLogonBefore"] = DateTime.Today.AddDays(-inactiveDays.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return d;
    }

    /// <summary>Mevcut filtreleri koruyarak sayfa/sıralama linki üretir.</summary>
    public Dictionary<string, string> RouteValues(int? page = null, UserSortField? sortBy = null, bool? sortDescending = null)
    {
        var c = Criteria;
        var d = new Dictionary<string, string> { [SearchFlag] = "1" };

        void Add(string key, object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(s)) d[$"Criteria.{key}"] = s;
        }

        Add(nameof(c.Text), c.Text);
        Add(nameof(c.Status), c.Status);
        Add(nameof(c.LastLogonBefore), c.LastLogonBefore?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(nameof(c.SortBy), sortBy ?? c.SortBy);
        Add(nameof(c.SortDescending), sortDescending ?? c.SortDescending);
        Add(nameof(c.PageSize), c.PageSize);
        Add(nameof(c.Page), page ?? c.Page);
        return d;
    }

    public Dictionary<string, string> SortRouteValues(UserSortField field) =>
        RouteValues(page: 1, sortBy: field, sortDescending: field == Criteria.SortBy ? !Criteria.SortDescending : false);

    public string SortIndicator(UserSortField field) =>
        field != Criteria.SortBy ? string.Empty : Criteria.SortDescending ? "▼" : "▲";

    public static string StatusText(UserStatusFilter s) => s switch
    {
        UserStatusFilter.Active => "Aktif",
        UserStatusFilter.Disabled => "Pasif",
        _ => "Tümü"
    };
}

public sealed class UserDetailViewModel
{
    public UserDetailViewModel(UserDetailDto detail) => Detail = detail;

    public UserDetailDto Detail { get; }

    /// <summary>Kullanıcının Grup Yönetimi modülüne erişimi varsa grup adları detaya link olur.</summary>
    public bool CanViewGroups { get; set; }
}

// ----------------------------------------------------------------------
// Toplu aktif/pasif ve silme
// ----------------------------------------------------------------------

public sealed class UserActionUploadViewModel
{
    public required UserActionKind Kind { get; init; }
    public string? ErrorMessage { get; set; }
    public int MaxCount => UserActionService.MaxBatchSize;
}

public sealed class UserActionPreviewViewModel
{
    public required UserActionPreviewDto Preview { get; init; }
    public string? FileName { get; init; }
    public required string EnvironmentKey { get; init; }
    public Guid PreviewJobId { get; set; }
}

public sealed class UserActionExecuteModel
{
    public string Environment { get; set; } = string.Empty;
    public Guid PreviewJobId { get; set; }
    public int ConfirmationCount { get; set; }
}

public sealed class UserActionResultViewModel
{
    public required UserActionKind Kind { get; init; }
    public required IReadOnlyList<UserActionResult> Results { get; init; }
}
