namespace BoAdmin.Application.Reports.Dtos;

public enum ReportSortField
{
    Name,
    Kind,
    Owner,
    UpdatedAt,
    Id
}

/// <summary>Rapor arama filtreleri.</summary>
public sealed class ReportSearchCriteria
{
    /// <summary>Rapor adı içerir</summary>
    public string? NameContains { get; set; }

    /// <summary>Klasör SI_ID</summary>
    public int? FolderId { get; set; }

    /// <summary>Alt klasörleri de dahil et</summary>
    public bool IncludeSubfolders { get; set; } = true;

    /// <summary>Sahip içerir</summary>
    public string? OwnerContains { get; set; }

    /// <summary>SI_KIND (null = tüm desteklenen türler)</summary>
    public string? Kind { get; set; }

    /// <summary>Son değişiklik tarihi başlangıcı (dahil)</summary>
    public DateTime? UpdatedFrom { get; set; }

    /// <summary>Son değişiklik tarihi bitişi (gün sonuna kadar dahil)</summary>
    public DateTime? UpdatedTo { get; set; }

    public ReportSortField SortBy { get; set; } = ReportSortField.Name;
    public bool SortDescending { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>Tablodaki rapor satırı.</summary>
public sealed record ReportListItemDto(
    int Id,
    string Name,
    string Kind,
    string KindDisplayName,
    string? Owner,
    int ParentFolderId,
    string FolderPath,
    DateTime? UpdatedAt,
    string? Cuid = null);

public sealed record FolderDto(int Id, string Name, string Path);

/// <summary>Silme önizlemesindeki satır.</summary>
public sealed record DeletePreviewItemDto(ReportListItemDto Report, int InstanceCount);

/// <summary>Silme önizleme ekranının verisi.</summary>
public sealed record DeletePreviewDto(
    IReadOnlyList<DeletePreviewItemDto> Items,
    IReadOnlyList<int> NotFoundIds,
    FolderDto? ArchiveFolder)
{
    public bool IsArchiveAvailable => ArchiveFolder is not null;
    public int TotalInstanceCount => Items.Sum(i => i.InstanceCount);
}

/// <summary>Silme seçenekleri.</summary>
public sealed class DeleteOptions
{
    /// <summary>true: silmek yerine arşiv klasörüne taşı (kurtarılabilir silme)</summary>
    public bool MoveToArchive { get; set; }

    /// <summary>Raporun instance'larını (zamanlanmış çıktılarını) da sil</summary>
    public bool DeleteInstances { get; set; }

    /// <summary>Kullanıcının onay için yazdığı rapor sayısı; seçili rapor sayısıyla eşleşmeli.</summary>
    public int ConfirmationCount { get; set; }
}

public enum ReportOperation
{
    Delete,
    Archive,
    DeleteInstance,

    /// <summary>TXT ile toplu işlem: yedek klasörüne taşıma</summary>
    BackupMove
}

/// <summary>TXT (CUID listesi) ile toplu yedekleme önizlemesi.</summary>
public sealed record BulkBackupPreviewDto(
    IReadOnlyList<DeletePreviewItemDto> Items,
    IReadOnlyList<string> NotFoundCuids,
    IReadOnlyList<string> InvalidEntries,
    int DuplicateCount,
    FolderDto TargetFolder)
{
    public int TotalInstanceCount => Items.Sum(i => i.InstanceCount);
    public int AlreadyInTargetCount => Items.Count(i => i.Report.ParentFolderId == TargetFolder.Id);
}

/// <summary>Toplu işlemde tek raporun sonucu.</summary>
public sealed record DeleteItemResult(
    int ReportId,
    string ReportName,
    string? FolderPath,
    ReportOperation Operation,
    bool Success,
    string Message,
    int InstancesDeleted = 0,
    int InstancesFailed = 0);
