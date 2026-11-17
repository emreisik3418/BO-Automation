namespace BoAdmin.Web.Authorization;

/// <summary>
/// Modül (sekme) adları. Aynı zamanda policy adı ve appsettings anahtarıdır:
/// [Authorize(Policy = ModuleNames.Reports)]  <->  "Authorization:Modules:Reports"
/// </summary>
public static class ModuleNames
{
    public const string Reports = "Reports";
    public const string Users = "Users";
    public const string Groups = "Groups";

    /// <summary>En az bir modüle erişimi olan kullanıcılar (karşılama ekranı için).</summary>
    public const string AnyModule = "AnyModule";

    public static readonly IReadOnlyList<string> All = new[] { Reports, Users, Groups };
}
