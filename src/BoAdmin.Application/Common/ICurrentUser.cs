namespace BoAdmin.Application.Common;

/// <summary>
/// İşlemi yapan Windows kullanıcısı. Application katmanı HttpContext'i tanımaz;
/// audit log vb. için kullanıcı bilgisini bu arayüzden alır.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Tam hesap adı: DOMAIN\A12345</summary>
    string UserName { get; }

    /// <summary>Sicil (sAMAccountName): A12345</summary>
    string Sicil { get; }

    /// <summary>AD'deki görünen ad (bulunamazsa sicil).</summary>
    string DisplayName { get; }
}
