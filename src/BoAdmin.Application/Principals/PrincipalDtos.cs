namespace BoAdmin.Application.Principals;

// ----------------------------------------------------------------------
// Kullanıcılar
// ----------------------------------------------------------------------

public enum UserStatusFilter
{
    All,
    Active,
    Disabled
}

public enum UserSortField
{
    Name,
    FullName,
    LastLogon,
    Created
}

/// <summary>Kullanıcı arama kriterleri (query string'den bağlanır).</summary>
public sealed class UserSearchCriteria
{
    /// <summary>Hesap adı, tam ad veya e-posta içerir.</summary>
    public string? Text { get; set; }
    public UserStatusFilter Status { get; set; } = UserStatusFilter.All;

    /// <summary>Son girişi bu tarihten önce olanlar (hiç giriş yapmamışlar bu filtrede gelmez).</summary>
    public DateTime? LastLogonBefore { get; set; }

    public UserSortField SortBy { get; set; } = UserSortField.Name;
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public void Normalize()
    {
        Page = Math.Max(1, Page);
        PageSize = Math.Clamp(PageSize, 10, 200);
        Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();
    }
}

public sealed record UserListItemDto(
    int Id,
    string? Cuid,
    string Name,
    string? FullName,
    string? Email,
    bool Disabled,
    DateTime? LastLogon,
    DateTime? CreatedAt,
    string? Description);

public sealed record UserDetailDto(UserListItemDto User, IReadOnlyList<GroupListItemDto> Groups);

// ----------------------------------------------------------------------
// Gruplar
// ----------------------------------------------------------------------

public sealed class GroupSearchCriteria
{
    public string? Text { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public void Normalize()
    {
        Page = Math.Max(1, Page);
        PageSize = Math.Clamp(PageSize, 10, 200);
        Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();
    }
}

public sealed record GroupListItemDto(
    int Id,
    string? Cuid,
    string Name,
    string? Description,
    DateTime? CreatedAt);

/// <summary>Grup detayı. Kullanıcı listesi büyük gruplarda kısaltılır (UsersTruncated).</summary>
public sealed record GroupDetailDto(
    GroupListItemDto Group,
    IReadOnlyList<GroupListItemDto> ParentGroups,
    IReadOnlyList<GroupListItemDto> Subgroups,
    IReadOnlyList<UserListItemDto> Users,
    bool UsersTruncated);

/// <summary>Üyelik raporunun bir satırı.</summary>
/// <param name="GroupPath">Alt gruplar dahilse "Ana grup &gt; Alt grup" yolu; değilse grubun adı.</param>
public sealed record MembershipRow(
    string RootGroup,
    string GroupPath,
    string MemberType,
    string MemberName,
    string? FullName,
    string? Email,
    bool? Disabled,
    DateTime? LastLogon);

public sealed record MembershipReport(
    IReadOnlyList<MembershipRow> Rows,
    int GroupCount,
    bool IncludeNested,
    IReadOnlyList<string> Warnings);
