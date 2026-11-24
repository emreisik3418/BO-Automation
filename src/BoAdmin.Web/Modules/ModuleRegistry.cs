using BoAdmin.Web.Authorization;

namespace BoAdmin.Web.Modules;

/// <summary>Modülün sol menüdeki alt sayfası.</summary>
/// <param name="ActiveFor">Bu menü öğesini seçili gösteren action'lar (ör. önizleme/sonuç sayfaları).</param>
public sealed record ModuleNavItem(string Title, string Action, string Icon, params string[] ActiveFor)
{
    public bool IsActiveFor(string? action) =>
        string.Equals(action, Action, StringComparison.OrdinalIgnoreCase)
        || ActiveFor.Any(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Sol menüdeki bir modülün tanımı.</summary>
/// <param name="Name">Modül/policy adı (ModuleNames)</param>
/// <param name="Title">Menüde görünen Türkçe ad</param>
/// <param name="Icon">Icons sınıfındaki ikon adı</param>
/// <param name="Controller">Modülün controller'ı</param>
/// <param name="IsAvailable">false ise "Yakında" olarak pasif gösterilir</param>
public sealed record ModuleDescriptor(
    string Name,
    string Title,
    string Description,
    string Icon,
    string Controller,
    string Action,
    bool IsAvailable,
    IReadOnlyList<ModuleNavItem> Items);

/// <summary>
/// Uygulamadaki tüm modüller. Yeni modül eklemek için:
///  1) ModuleNames'e sabit ekle
///  2) Buraya satır ekle (IsAvailable: true) ve alt sayfalarını yaz
///  3) Modules/{Modül}/ altına Controller + Views
///  4) Yeni sayfalar "Uygulama Kullanıcıları" ekranında otomatik listelenir
/// </summary>
public sealed class ModuleRegistry
{
    public IReadOnlyList<ModuleDescriptor> All { get; } = new[]
    {
        new ModuleDescriptor(ModuleNames.Reports, "Rapor Yönetişimi",
            "Rapor arama, silme, toplu yedekleme, ad, keyword ve açıklama güncelleme, işlem geçmişi",
            "reports", "Reports", "Index", IsAvailable: true,
            new[]
            {
                new ModuleNavItem("Rapor Arama", "Index", "search", "DeletePreview", "Delete"),
                new ModuleNavItem("Rapor Silme", "Bulk", "layers", "BulkPreview", "BulkExecute"),
                new ModuleNavItem("Rapor Adı Güncelleme", "Rename", "edit", "RenamePreview", "RenameExecute"),
                new ModuleNavItem("Keyword Güncelleme", "Keywords", "tag", "KeywordsPreview", "KeywordsExecute"),
                new ModuleNavItem("Açıklama Güncelleme", "Descriptions", "text", "DescriptionsPreview", "DescriptionsExecute"),
                new ModuleNavItem("İşlem Geçmişi", "History", "clock", "ExportHistory"),
            }),

        new ModuleDescriptor(ModuleNames.Users, "Kullanıcı Yönetimi",
            "BO kullanıcılarını arama, toplu aktif/pasif yapma, silme ve grup üyeliği",
            "user", "Users", "Index", IsAvailable: true,
            new[]
            {
                new ModuleNavItem("Kullanıcı Arama", "Index", "search", "Detail", "Export"),
                new ModuleNavItem("Aktif / Pasif Yapma", "Status", "lock", "StatusPreview"),
                new ModuleNavItem("Kullanıcı Silme", "Remove", "trash", "RemovePreview"),
                new ModuleNavItem("Grup Üyeliği", "GroupMembership", "users", "GroupMembershipPreview", "GroupMembershipExecute"),
            }),

        new ModuleDescriptor(ModuleNames.Groups, "Grup Yönetimi",
            "BO grupları, alt grup ekleme ve üyelik raporları",
            "users", "Groups", "Index", IsAvailable: true,
            new[]
            {
                new ModuleNavItem("Grup Arama", "Index", "search", "Detail"),
                new ModuleNavItem("Alt Grup Ekleme", "Subgroups", "layers", "SubgroupsPreview", "SubgroupsExecute"),
                new ModuleNavItem("Üyelik Raporu", "Membership", "sheet", "MembershipReport", "MembershipReady"),
            }),
    };

    /// <summary>Yetki anahtarı: "Reports.Index", "Users.Remove" ...</summary>
    public static string PageKey(ModuleDescriptor module, ModuleNavItem item) => $"{module.Controller}.{item.Action}";

    /// <summary>Yetkilendirilebilir tüm sayfalar (yalnızca hazır modüller).</summary>
    public IEnumerable<(ModuleDescriptor Module, ModuleNavItem Item, string Key)> Pages =>
        All.Where(m => m.IsAvailable).SelectMany(m => m.Items.Select(i => (m, i, PageKey(m, i))));

    /// <summary>Controller + action hangi sayfaya ait? (önizleme / onay action'ları da ActiveFor ile eşlenir)</summary>
    public string? FindPageKey(string? controller, string? action)
    {
        var module = FindByController(controller);
        var item = module?.Items.FirstOrDefault(i => i.IsActiveFor(action));
        return module is null || item is null ? null : PageKey(module, item);
    }

    public ModuleDescriptor? Find(string name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public ModuleDescriptor? FindByController(string? controller) =>
        All.FirstOrDefault(m => string.Equals(m.Controller, controller, StringComparison.OrdinalIgnoreCase));
}
