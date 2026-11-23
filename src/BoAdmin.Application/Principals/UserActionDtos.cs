namespace BoAdmin.Application.Principals;

/// <summary>Excel ile toplu kullanıcı işlemi türü.</summary>
public enum UserActionKind
{
    /// <summary>Aktif / pasif yapma (satır bazında hedef durum)</summary>
    SetStatus,

    /// <summary>Kullanıcı silme</summary>
    Delete
}

/// <summary>Dosyadaki bir satır. <paramref name="Disable"/>: SetStatus için hedef (true = pasif).</summary>
public sealed record UserActionRow(int RowNumber, string Account, bool? Disable);

/// <summary>Dosyada / önizlemede işlenemeyen satır.</summary>
public sealed record PrincipalRowIssue(int RowNumber, string? Value, string Reason);

public sealed record UserActionPreviewItem(
    int RowNumber,
    UserListItemDto User,
    bool? Disable,
    bool CanProcess,
    string? Reason = null);

public sealed record UserActionPreviewDto(
    UserActionKind Kind,
    IReadOnlyList<UserActionPreviewItem> Items,
    IReadOnlyList<PrincipalRowIssue> Problems)
{
    public IReadOnlyList<UserActionPreviewItem> Processable => Items.Where(i => i.CanProcess).ToList();
    public IReadOnlyList<UserActionPreviewItem> Skipped => Items.Where(i => !i.CanProcess).ToList();
}

/// <summary>Onaylanan tek işlem.</summary>
public sealed record UserActionRequest(int UserId, bool? Disable);

public sealed record UserActionResult(
    int UserId,
    string UserName,
    string? FullName,
    string Operation,
    bool Success,
    string Message);

/// <summary>Audit ve ekranda kullanılan işlem adları.</summary>
public static class UserOperations
{
    public const string Enable = "UserEnable";
    public const string Disable = "UserDisable";
    public const string Delete = "UserDelete";

    public static string Text(string operation) => operation switch
    {
        Enable => "Kullanıcı aktif yapma",
        Disable => "Kullanıcı pasif yapma",
        Delete => "Kullanıcı silme",
        _ => operation
    };
}
