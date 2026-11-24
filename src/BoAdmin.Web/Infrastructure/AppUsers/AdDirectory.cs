using System.DirectoryServices.AccountManagement;

namespace BoAdmin.Web.Infrastructure.AppUsers;

/// <summary>Sicilden AD görünen adını bulur (kullanıcı eklerken adı otomatik doldurmak için).</summary>
public static class AdDirectory
{
    public static string? TryGetDisplayName(string sicil, ILogger logger)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(sicil)) return null;
        try
        {
            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, sicil);
            return string.IsNullOrWhiteSpace(user?.DisplayName) ? null : user.DisplayName;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AD'den görünen ad okunamadı: {Sicil}", sicil);
            return null;
        }
    }
}
