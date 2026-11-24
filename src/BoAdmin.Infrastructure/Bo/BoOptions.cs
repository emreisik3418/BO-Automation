namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// appsettings.json > "SapBo" bölümü. Birden fazla BO ortamı tanımlanır:
///
/// "SapBo": {
///   "DefaultEnvironment": "TEST",
///   "Environments": {
///     "TEST": { "BaseUrl": "...", "UserName": "...", "Authorization": { "Reports": { ... } } },
///     "PROD": { ... }
///   }
/// }
///
/// Şifreler appsettings'te boş bırakılır:
///   Geliştirme: User Secrets  -> SapBo:Environments:TEST:Password
///   Prod      : ortam değişkeni -> SapBo__Environments__PROD__Password
/// </summary>
public sealed class BoOptions
{
    public const string SectionName = "SapBo";

    /// <summary>Kullanıcı henüz seçim yapmadıysa açılan ortam.</summary>
    public string DefaultEnvironment { get; set; } = string.Empty;

    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Token bu süre sonunda proaktif olarak yenilenir. BO oturum süresinden kısa olmalı.</summary>
    public int TokenLifetimeMinutes { get; set; } = 50;

    public Dictionary<string, BoEnvironmentOptions> Environments { get; set; } = new();

    /// <summary>Şirket içi şifre kasası (HsmService). Ortamında "Sbox" bölümü olan ortamlar BO kullanıcı/şifresini buradan alır.</summary>
    public SboxOptions Sbox { get; set; } = new();

    /// <summary>Ortamı büyük/küçük harf duyarsız bulur.</summary>
    public BoEnvironmentOptions? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return Environments.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>Ortamın config'teki doğru yazımlı anahtarı ("test" -> "TEST").</summary>
    public string? NormalizeKey(string? key) =>
        Environments.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ortamlar, Order alanına göre sıralı.</summary>
    public IReadOnlyList<KeyValuePair<string, BoEnvironmentOptions>> Ordered =>
        Environments.OrderBy(kv => kv.Value.Order).ThenBy(kv => kv.Key).ToList();
}

/// <summary>Tek bir BO ortamının ayarları.</summary>
public sealed class BoEnvironmentOptions
{
    /// <summary>Ekranda görünen ad. Boşsa anahtar kullanılır.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Rozet rengi (Bootstrap): danger, warning, success, info, secondary. PROD için "danger" önerilir.</summary>
    public string Color { get; set; } = "secondary";

    /// <summary>Menüdeki sıra.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Kritik ortam (ör. PROD). true ise silme / yedekleme / ad güncelleme onayında ek bir
    /// "Emin misiniz?" penceresi açılır ve ekranda kırmızı uyarı gösterilir.
    /// Yazılmazsa Color "danger" olan ortamlar kritik sayılır.
    /// </summary>
    public bool? IsCritical { get; set; }

    /// <summary>Örn. http://bo-server:6405/biprws</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Doluysa BO kullanıcı adı ve şifresi Sbox'tan alınır; UserName / Password kullanılmaz.
    /// Boşsa UserName + Password (User Secrets / ortam değişkeni) kullanılır.
    /// </summary>
    public SboxTargetOptions? Sbox { get; set; }

    public bool UsesSbox => Sbox?.IsConfigured == true;

    /// <summary>secEnterprise / secWinAD / secLDAP</summary>
    public string AuthType { get; set; } = "secEnterprise";

    /// <summary>"Kurtarılabilir silme" arşiv klasörünün SI_ID'si. 0 = özellik kapalı.</summary>
    public int ArchiveFolderId { get; set; }

    /// <summary>Public Folders kökünün SI_ID'si (standart kurulumda 23).</summary>
    public int RootFolderId { get; set; } = 23;

    public BoDeleteStrategy DeleteStrategy { get; set; } = BoDeleteStrategy.Documents;

    public BoMoveStrategy MoveStrategy { get; set; } = BoMoveStrategy.InfoObjects;

    // "Authorization" alt bölümü Web katmanında (ModuleAuthorizationOptions) okunur.
}

/// <summary>
/// İlk denenecek silme uç noktası. Seçilen uç nokta 404 dönerse diğerleri sırayla denenir
/// ve çalışan uç nokta ortam bazında hatırlanır.
/// </summary>
public enum BoDeleteStrategy
{
    /// <summary>DELETE /v1/documents/{id}</summary>
    Documents,

    /// <summary>DELETE /v1/infoobjects/{id}</summary>
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

/// <summary>Sbox servisi bağlantısı (tüm ortamlar için ortak).</summary>
public sealed class SboxOptions
{
    /// <summary>Örn. http://sbox-sunucu/HsmService.svc</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Sbox servisini çağıran uygulama kullanıcısı.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Düz metin tutulmaz: User Secrets (geliştirme) / ortam değişkeni SapBo__Sbox__Password (sunucu).</summary>
    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Sbox'tan alınan bilgi bellekte bu kadar dakika tutulur. BO logon "şifre hatalı" derse süre beklenmeden yeniden alınır.</summary>
    public int CacheMinutes { get; set; } = 60;
}

/// <summary>Bir ortamın Sbox'taki kaydının yeri (değer biçimi "kullanici;sifre").</summary>
public sealed class SboxTargetOptions
{
    public string AppName { get; set; } = string.Empty;
    public string SubAppName { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string SensitiveDataName { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SensitiveDataName);

    public override string ToString() => $"{AppName}/{SubAppName}/{SensitiveDataName}";
}
