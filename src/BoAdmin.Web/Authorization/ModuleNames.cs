namespace BoAdmin.Web.Authorization;

/// <summary>
/// Modül adları. Aynı zamanda policy adıdır: [Authorize(Policy = ModuleNames.Reports)]
/// Modül erişimi = seçili ortamda o modülün en az bir sayfasına yetki.
/// </summary>
public static class ModuleNames
{
    public const string Reports = "Reports";
    public const string Users = "Users";
    public const string Groups = "Groups";

    /// <summary>En az bir modüle erişimi olan kullanıcılar (karşılama ekranı için).</summary>
    public const string AnyModule = "AnyModule";

    /// <summary>Uygulama yöneticileri (appsettings AccessControl:Admins).</summary>
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = new[] { Reports, Users, Groups };
}
