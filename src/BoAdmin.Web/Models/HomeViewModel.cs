using BoAdmin.Web.Modules;

namespace BoAdmin.Web.Models;

public sealed record HomeViewModel(
    string DisplayName,
    string Sicil,
    string UserName,
    IReadOnlyList<ModuleDescriptor> Modules);
