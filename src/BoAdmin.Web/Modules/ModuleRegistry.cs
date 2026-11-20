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
///  3) appsettings "SapBo:Environments:{ORTAM}:Authorization" altına kural ekle
///  4) Modules/{Modül}/ altına Controller + Views
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
                new ModuleNavItem("İşlem Geçmişi", "History", "clock"),
            }),

        new ModuleDescriptor(ModuleNames.Users, "Kullanıcı Yönetimi",
            "BO kullanıcılarını arama ve yönetme",
            "user", "Users", "Index", IsAvailable: false, Array.Empty<ModuleNavItem>()),

        new ModuleDescriptor(ModuleNames.Groups, "Grup Yönetimi",
            "BO gruplarını ve üyeliklerini yönetme",
            "users", "Groups", "Index", IsAvailable: false, Array.Empty<ModuleNavItem>()),
    };

    public ModuleDescriptor? Find(string name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public ModuleDescriptor? FindByController(string? controller) =>
        All.FirstOrDefault(m => string.Equals(m.Controller, controller, StringComparison.OrdinalIgnoreCase));
}
