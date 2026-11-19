using BoAdmin.Application.Common;
using BoAdmin.Application.Reports.Dtos;

namespace BoAdmin.Application.Abstractions;

public enum DeleteOutcome
{
    /// <summary>Nesne CMS'ten kalktı.</summary>
    Deleted,

    /// <summary>Nesne hâlâ var ama farklı bir yere alınmış (BO 4.3 Geri Dönüşüm Kutusu açıksa).</summary>
    MovedToRecycleBin
}

/// <summary>Kopyalama sonucu.</summary>
/// <param name="AlreadyExisted">Yedek kopya daha önceki bir çalıştırmada oluşturulmuştu (yeniden kopyalanmadı).</param>
public sealed record CopyResult(int NewId, string? NewCuid, string NewName, bool AlreadyExisted);

/// <summary>
/// SAP BO rapor işlemleri (kullanıcının seçtiği ortamda). Application katmanı REST ayrıntılarını bilmez;
/// implementasyon Infrastructure/Bo/BoReportGateway.cs içindedir.
/// </summary>
public interface IBoReportGateway
{
    Task<PagedResult<ReportListItemDto>> SearchAsync(ReportSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Verilen ID'lerden yalnızca desteklenen türdeki raporları döndürür (instance ve diğer nesneler hariç).</summary>
    Task<IReadOnlyList<ReportListItemDto>> GetReportsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Verilen CUID'lerden yalnızca desteklenen türdeki raporları döndürür.</summary>
    Task<IReadOnlyList<ReportListItemDto>> GetReportsByCuidsAsync(IReadOnlyCollection<string> cuids, CancellationToken ct = default);

    /// <summary>Rapor ID -> instance sayısı</summary>
    Task<IReadOnlyDictionary<int, int>> GetInstanceCountsAsync(IReadOnlyCollection<int> reportIds, CancellationToken ct = default);

    Task<IReadOnlyList<int>> GetInstanceIdsAsync(int reportId, CancellationToken ct = default);

    /// <summary>Nesneyi siler ve silindiğini doğrular.</summary>
    Task<DeleteOutcome> DeleteObjectAsync(int id, string kind, int parentId, CancellationToken ct = default);

    /// <summary>Raporu klasöre taşır ve taşındığını doğrular.</summary>
    Task MoveToFolderAsync(int id, string kind, int targetFolderId, CancellationToken ct = default);

    /// <summary>
    /// Raporu hedef klasöre yeni adla kopyalar ve kopyanın oluştuğunu doğrular.
    /// Hedefte adı "_{sourceCuid}" ile biten bir kopya zaten varsa yeniden kopyalamaz, onu döndürür.
    /// </summary>
    Task<CopyResult> CopyToFolderAsync(int sourceId, string sourceCuid, string kind, int targetFolderId, string newName, CancellationToken ct = default);

    /// <summary>Raporun adını değiştirir ve yeni adın yazıldığını doğrular.</summary>
    Task RenameAsync(int id, string newName, CancellationToken ct = default);

    Task<IReadOnlyList<FolderDto>> GetFoldersAsync(CancellationToken ct = default);

    /// <summary>Klasör bilgisi (yol dahil). Bulunamazsa null.</summary>
    Task<FolderDto?> GetFolderAsync(int folderId, CancellationToken ct = default);

    /// <summary>Config'teki arşiv klasörü. Tanımlı değilse (0) null.</summary>
    Task<FolderDto?> GetArchiveFolderAsync(CancellationToken ct = default);
}
