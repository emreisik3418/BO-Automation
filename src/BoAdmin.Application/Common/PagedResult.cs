namespace BoAdmin.Application.Common;

/// <summary>
/// Sayfalı sonuç. BO cmsquery toplam kayıt sayısı döndürmediği için TotalCount null olabilir;
/// bu durumda sayfalama "Önceki / Sonraki" şeklinde HasNextPage ile yapılır.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    bool HasNextPage,
    int? TotalCount = null)
{
    public bool HasPreviousPage => Page > 1;

    public int? TotalPages => TotalCount is null || PageSize <= 0
        ? null
        : (int)Math.Ceiling(TotalCount.Value / (double)PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new(Array.Empty<T>(), page, pageSize, false, 0);
}
