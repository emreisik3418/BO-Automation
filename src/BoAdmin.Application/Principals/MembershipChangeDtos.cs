namespace BoAdmin.Application.Principals;

/// <summary>Gruba eklenen / çıkarılan üyenin türü.</summary>
public enum MemberType
{
    /// <summary>Kullanıcı (PUT/DELETE /v1/usergroups/{id}/users)</summary>
    User,

    /// <summary>Alt grup (PUT/DELETE /v1/usergroups/{id}/usergroups)</summary>
    Group
}

/// <summary>Dosyadaki bir satır. <paramref name="Add"/>: true = ekle, false = çıkar, null = tanınmadı.</summary>
public sealed record MembershipChangeRow(int RowNumber, string GroupName, string MemberName, bool? Add);

public sealed record MembershipChangeItem(
    int RowNumber,
    GroupListItemDto Group,
    int MemberId,
    string MemberName,
    string? MemberDetail,
    bool Add,
    bool CanProcess,
    string? Reason = null);

public sealed record MembershipChangePreviewDto(
    MemberType MemberType,
    IReadOnlyList<MembershipChangeItem> Items,
    IReadOnlyList<PrincipalRowIssue> Problems)
{
    public IReadOnlyList<MembershipChangeItem> Processable => Items.Where(i => i.CanProcess).ToList();
    public IReadOnlyList<MembershipChangeItem> Skipped => Items.Where(i => !i.CanProcess).ToList();
}

public sealed record MembershipChangeRequest(int GroupId, string GroupName, int MemberId, string MemberName, bool Add);

public sealed record MembershipChangeResult(
    string GroupName,
    string MemberName,
    string Operation,
    bool Success,
    string Message);

public static class MembershipOperations
{
    public const string AddUser = "GroupAddUser";
    public const string RemoveUser = "GroupRemoveUser";
    public const string AddGroup = "GroupAddGroup";
    public const string RemoveGroup = "GroupRemoveGroup";

    public static string Of(MemberType type, bool add) => type == MemberType.User
        ? add ? AddUser : RemoveUser
        : add ? AddGroup : RemoveGroup;

    public static bool IsKnown(string? operation) =>
        operation is AddUser or RemoveUser or AddGroup or RemoveGroup;

    public static string Text(string operation) => operation switch
    {
        AddUser => "Gruba kullanıcı ekleme",
        RemoveUser => "Gruptan kullanıcı çıkarma",
        AddGroup => "Gruba alt grup ekleme",
        RemoveGroup => "Gruptan alt grup çıkarma",
        _ => operation
    };
}
