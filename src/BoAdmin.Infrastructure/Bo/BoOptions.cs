namespace BoAdmin.Infrastructure.Bo;

/// <summary>appsettings.json > "SapBo" bölümü.</summary>
public sealed class BoOptions
{
    public const string SectionName = "SapBo";

    /// <summary>Örn. http://bo-server:6405/biprws</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    /// <summary>appsettings'te boş bırakılır. Geliştirme: User Secrets, Prod: ortam değişkeni SapBo__Password</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>secEnterprise / secWinAD / secLDAP</summary>
    public string AuthType { get; set; } = "secEnterprise";

    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Token bu süre sonunda proaktif olarak yenilenir. BO oturum süresinden kısa olmalı.</summary>
    public int TokenLifetimeMinutes { get; set; } = 50;

    /// <summary>"Kurtarılabilir silme" arşiv klasörünün SI_ID'si. 0 = özellik kapalı.</summary>
    public int ArchiveFolderId { get; set; }

    public BoDeleteStrategy DeleteStrategy { get; set; } = BoDeleteStrategy.InfoObjects;

    public BoMoveStrategy MoveStrategy { get; set; } = BoMoveStrategy.InfoObjects;

    public int QueryPageSize { get; set; } = 50;
}

public enum BoDeleteStrategy
{
    /// <summary>DELETE /v1/infoobjects/{id} (doğrulanmalı)</summary>
    InfoObjects,

    /// <summary>DELETE /raylight/v1/documents/{id} (yalnızca Webi)</summary>
    Raylight
}

public enum BoMoveStrategy
{
    /// <summary>PUT /v1/infoobjects/{id} ile parent klasör değişikliği (doğrulanmalı)</summary>
    InfoObjects,

    /// <summary>Raylight ile (yalnızca Webi)</summary>
    Raylight
}
