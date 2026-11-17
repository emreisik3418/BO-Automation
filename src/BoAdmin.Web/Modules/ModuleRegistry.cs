using BoAdmin.Web.Authorization;

namespace BoAdmin.Web.Modules;

/// <summary>Üst menüdeki bir sekmenin tanımı.</summary>
/// <param name="Name">Modül/policy adı (ModuleNames)</param>
/// <param name="Title">Sekmede görünen Türkçe ad</param>
/// <param name="Icon">Bootstrap Icons sınıfı</param>
/// <param name="Controller">Sekmeye tıklanınca açılacak controller</param>
/// <param name="IsAvailable">false ise "Yakında" olarak pasif gösterilir</param>
public sealed record ModuleDescriptor(
    string Name,
    string Title,
    string Description,
    string Icon,
    string Controller,
    string Action,
    bool IsAvailable);

/// <summary>
/// Uygulamadaki tüm modüller. Yeni modül eklemek için:
///  1) ModuleNames'e sabit ekle
///  2) Buraya satır ekle (IsAvailable: true)
///  3) appsettings "Authorization:Modules" altına kural ekle
///  4) Modules/{Modül}/ altına Controller + Views
/// </summary>
public sealed class ModuleRegistry
{
    public IReadOnlyList<ModuleDescriptor> All { get; } = new[]
    {
        new ModuleDescriptor(ModuleNames.Reports, "Rapor İşlemleri",
            "Rapor arama, silme, arşive taşıma ve işlem geçmişi",
            "bi-file-earmark-bar-graph", "Reports", "Index", IsAvailable: true),

        new ModuleDescriptor(ModuleNames.Users, "Kullanıcı İşlemleri",
            "BO kullanıcı yönetimi",
            "bi-person-gear", "Users", "Index", IsAvailable: false),

        new ModuleDescriptor(ModuleNames.Groups, "Grup İşlemleri",
            "BO grup yönetimi",
            "bi-people", "Groups", "Index", IsAvailable: false),
    };

    public ModuleDescriptor? Find(string name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
