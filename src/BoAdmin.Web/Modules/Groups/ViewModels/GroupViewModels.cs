using System.Globalization;
using BoAdmin.Application.Common;
using BoAdmin.Application.Principals;

namespace BoAdmin.Web.Modules.Groups.ViewModels;

public sealed class GroupSearchViewModel
{
    public const string SearchFlag = "search";

    public static readonly int[] PageSizes = { 20, 50, 100, 200 };

    public GroupSearchCriteria Criteria { get; init; } = new();
    public PagedResult<GroupListItemDto>? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Searched { get; set; }

    public Dictionary<string, string> RouteValues(int? page = null)
    {
        var c = Criteria;
        var d = new Dictionary<string, string> { [SearchFlag] = "1" };
        if (!string.IsNullOrEmpty(c.Text)) d["Criteria.Text"] = c.Text;
        d["Criteria.PageSize"] = c.PageSize.ToString(CultureInfo.InvariantCulture);
        d["Criteria.Page"] = (page ?? c.Page).ToString(CultureInfo.InvariantCulture);
        return d;
    }
}

public sealed class GroupDetailViewModel
{
    public GroupDetailViewModel(GroupDetailDto detail) => Detail = detail;

    public GroupDetailDto Detail { get; }

    /// <summary>Kullanıcı Yönetimi modülüne erişim varsa kullanıcı adları detaya link olur.</summary>
    public bool CanViewUsers { get; set; }
}

/// <summary>Üyelik raporu formu (POST).</summary>
public sealed class MembershipReportRequest
{
    public int? GroupId { get; set; }
    public string? Text { get; set; }
    public bool IncludeNested { get; set; }
}

/// <summary>Üyelik raporu hazır: özet + indirme.</summary>
public sealed class MembershipReadyViewModel
{
    public required MembershipReport Report { get; init; }
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
    public string? Scope { get; init; }
    public Guid JobId { get; set; }
}
