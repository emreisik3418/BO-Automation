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

/// <summary>
/// SAP BO rapor işlemleri. Application katmanı REST ayrıntılarını bilmez;
/// implementasyon Infrastructure/Bo/BoReportGateway.cs içindedir.
/// </summary>
public interface IBoReportGateway
{
    Task<PagedResult<ReportListItemDto>> SearchAsync(ReportSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Verilen ID'lerden yalnızca desteklenen türdeki raporları döndürür (instance ve diğer nesneler hariç).</summary>
    Task<IReadOnlyList<ReportListItemDto>> GetReportsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Rapor ID -> instance sayısı</summary>
    Task<IReadOnlyDictionary<int, int>> GetInstanceCountsAsync(IReadOnlyCollection<int> reportIds, CancellationToken ct = default);

    Task<IReadOnlyList<int>> GetInstanceIdsAsync(int reportId, CancellationToken ct = default);

    /// <summary>Nesneyi siler ve silindiğini doğrular.</summary>
    Task<DeleteOutcome> DeleteObjectAsync(int id, string kind, int parentId, CancellationToken ct = default);

    /// <summary>Raporu klasöre taşır ve taşındığını doğrular.</summary>
    Task MoveToFolderAsync(int id, string kind, int targetFolderId, CancellationToken ct = default);

    Task<IReadOnlyList<FolderDto>> GetFoldersAsync(CancellationToken ct = default);

    /// <summary>Config'teki arşiv klasörü. Tanımlı değilse (0) null.</summary>
    Task<FolderDto?> GetArchiveFolderAsync(CancellationToken ct = default);
}
