namespace BoAdmin.Application.Common;

/// <summary>
/// Kullanıcının üst menüden seçtiği BO ortamı (DEV, TEST, PROD ...).
/// Tüm BO işlemleri ve audit kayıtları bu ortama göre yapılır.
/// Implementasyon: Web/Infrastructure/CurrentEnvironmentAccessor (cookie).
/// </summary>
public interface ICurrentEnvironment
{
    /// <summary>Config anahtarı: "TEST", "PROD"</summary>
    string Key { get; }

    /// <summary>Ekranda görünen ad: "Test", "Canlı"</summary>
    string DisplayName { get; }
}
