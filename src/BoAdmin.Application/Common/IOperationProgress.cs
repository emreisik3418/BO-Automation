namespace BoAdmin.Application.Common;

/// <summary>
/// Uzun süren işlemlerin ilerlemesini bildirmek için (ekrandaki ilerleme çubuğu).
/// Servisler bu arayüzü bilir; nasıl gösterileceğini Web katmanı belirler.
/// </summary>
public interface IOperationProgress
{
    /// <summary>Yeni bir adıma geçildi. <paramref name="total"/> biliniyorsa adımdaki iş sayısı (0 = belirsiz).</summary>
    void Phase(string title, int total = 0);

    /// <summary>Adımdaki bir iş bitti. <paramref name="success"/> verilirse başarılı/başarısız sayacı artar.</summary>
    void Step(string? item = null, bool? success = null);

    /// <summary>Şu an işlenen öğe (bitmeden önce göstermek için).</summary>
    void Current(string? item);
}

/// <summary>İlerleme izlenmeyen çağrılar için boş implementasyon.</summary>
public sealed class NullOperationProgress : IOperationProgress
{
    public static readonly NullOperationProgress Instance = new();

    public void Phase(string title, int total = 0) { }
    public void Step(string? item = null, bool? success = null) { }
    public void Current(string? item) { }
}
