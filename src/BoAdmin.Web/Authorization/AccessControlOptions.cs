namespace BoAdmin.Web.Authorization;

/// <summary>
/// Uygulama yetkileri ("AccessControl" bölümü).
///
/// "AccessControl": {
///   "Admins": { "Users": [ "A12345" ], "Groups": [ "DOMAIN\\BO_Admins" ] },
///   "UsersFile": "D:\\Apps\\BoAdmin\\data\\kullanicilar.json"
/// }
///
/// Yöneticiler tüm ortamlarda tüm sayfalara erişir ve "Uygulama Kullanıcıları" sayfasını görür.
/// Diğer kullanıcılar o sayfadan eklenir; ortam ve sayfa yetkileri UsersFile dosyasında tutulur.
/// </summary>
public sealed class AccessControlOptions
{
    public const string SectionName = "AccessControl";

    public AdminOptions Admins { get; set; } = new();

    /// <summary>Kullanıcı yetkilerinin tutulduğu JSON dosyası. Göreli yol uygulama klasörüne göredir.</summary>
    public string UsersFile { get; set; } = "data/kullanicilar.json";
}

public sealed class AdminOptions
{
    /// <summary>Sicil (sAMAccountName) listesi. "A12345" veya "DOMAIN\A12345".</summary>
    public List<string> Users { get; set; } = new();

    /// <summary>AD grupları. Domain yazılmazsa kullanıcının domain'i varsayılır.</summary>
    public List<string> Groups { get; set; } = new();
}
