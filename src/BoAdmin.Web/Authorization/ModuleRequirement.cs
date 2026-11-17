using Microsoft.AspNetCore.Authorization;

namespace BoAdmin.Web.Authorization;

/// <summary>
/// "Bu modüle erişim gerekir" şartı.
/// ModuleName = ModuleNames.AnyModule ise herhangi bir modüle erişim yeterlidir.
/// </summary>
public sealed class ModuleRequirement : IAuthorizationRequirement
{
    public ModuleRequirement(string moduleName) => ModuleName = moduleName;

    public string ModuleName { get; }
}
