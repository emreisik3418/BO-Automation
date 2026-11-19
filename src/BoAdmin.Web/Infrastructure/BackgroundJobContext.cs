namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Arka plan işlerinde (toplu önizleme/silme) HttpContext yoktur.
/// İşi başlatan kullanıcı ve ortam bu nesneye yazılır; ICurrentUser ve ICurrentEnvironment
/// önce buraya bakar. Normal isteklerde boş kalır.
/// </summary>
public sealed class BackgroundJobContext
{
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public EnvironmentInfo? Environment { get; set; }

    public bool IsActive => UserName is not null;
}
