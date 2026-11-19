using System.Globalization;

namespace BoAdmin.Web.Infrastructure;

public static class UserInitials
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Avatar için baş harfler: "Emre Işık" -> "Eİ", ad yoksa sicilin ilk harfi.</summary>
    public static string From(string? displayName, string? sicil)
    {
        var parts = (displayName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var letters = parts.Length switch
        {
            0 => string.IsNullOrEmpty(sicil) ? "?" : sicil[..1],
            1 => parts[0][..1],
            _ => string.Concat(parts[0][..1], parts[^1][..1])
        };
        return letters.ToUpper(Tr);
    }
}
