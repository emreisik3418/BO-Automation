using BoAdmin.Application.Principals;

namespace BoAdmin.Web.Models;

/// <summary>
/// Toplu üyelik değişikliği ekranları (Kullanıcı Yönetimi ve Grup Yönetimi ortak kullanır).
/// Controller adı modele yazılır; ortak view'lar formları doğru controller'a gönderir.
/// </summary>
public sealed class MembershipUploadViewModel
{
    public required MemberType Type { get; init; }
    public required string Controller { get; init; }
    public required string PreviewAction { get; init; }
    public string? ErrorMessage { get; set; }
}

public sealed class MembershipPreviewViewModel
{
    public required MembershipChangePreviewDto Preview { get; init; }
    public required string Controller { get; init; }
    public required string UploadAction { get; init; }
    public required string ExecuteAction { get; init; }
    public required string EnvironmentKey { get; init; }
    public string? FileName { get; init; }
    public Guid PreviewJobId { get; set; }
}

public sealed class MembershipExecuteModel
{
    public string Environment { get; set; } = string.Empty;
    public Guid PreviewJobId { get; set; }
    public int ConfirmationCount { get; set; }
}

public sealed class MembershipResultViewModel
{
    public required MemberType Type { get; init; }
    public required string Controller { get; init; }
    public required string UploadAction { get; init; }
    public required IReadOnlyList<MembershipChangeResult> Results { get; init; }
}
