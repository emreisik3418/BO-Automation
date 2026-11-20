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
    public int PageSize { get; set; } = 20;
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

    /// <summary>TXT ile toplu işlem: yedek klasörüne kopyala (adı_CUID), sonra orijinali sil</summary>
    BackupCopy,

    /// <summary>Excel ile toplu ad güncelleme</summary>
    Rename,

    /// <summary>Excel ile toplu keyword güncelleme</summary>
    Keywords
}

/// <summary>Toplu yedekleme önizlemesindeki bir rapor.</summary>
/// <param name="BackupName">Kopyanın adı: {orijinal ad}_{orijinal CUID}</param>
/// <param name="CanProcess">false ise rapor işleme alınmaz (Reason açıklar)</param>
public sealed record BulkPreviewItemDto(
    ReportListItemDto Report,
    int InstanceCount,
    string BackupName,
    bool CanProcess,
    string? Reason = null);

/// <summary>TXT (CUID listesi) ile toplu yedekleme önizlemesi.</summary>
public sealed record BulkBackupPreviewDto(
    IReadOnlyList<BulkPreviewItemDto> Items,
    IReadOnlyList<string> NotFoundCuids,
    IReadOnlyList<string> InvalidEntries,
    int DuplicateCount,
    FolderDto TargetFolder)
{
    public IReadOnlyList<BulkPreviewItemDto> Processable => Items.Where(i => i.CanProcess).ToList();
    public IReadOnlyList<BulkPreviewItemDto> Skipped => Items.Where(i => !i.CanProcess).ToList();
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

// ----------------------------------------------------------------------
// Excel ile toplu ad güncelleme
// ----------------------------------------------------------------------

/// <summary>Excel'deki bir satır: CUID + yeni ad. RowNumber kullanıcıya hata göstermek için.</summary>
public sealed record RenameRow(int RowNumber, string Cuid, string NewName);

/// <summary>Excel'de işlenemeyen satır.</summary>
public sealed record RenameRowIssue(int RowNumber, string? Cuid, string Reason);

/// <summary>Ad güncelleme önizlemesindeki bir rapor.</summary>
public sealed record RenamePreviewItemDto(
    int RowNumber,
    ReportListItemDto Report,
    string NewName,
    bool CanProcess,
    string? Reason = null);

public sealed record RenamePreviewDto(
    IReadOnlyList<RenamePreviewItemDto> Items,
    IReadOnlyList<RenameRowIssue> NotFound,
    IReadOnlyList<RenameRowIssue> InvalidRows)
{
    public IReadOnlyList<RenamePreviewItemDto> Processable => Items.Where(i => i.CanProcess).ToList();
    public IReadOnlyList<RenamePreviewItemDto> Skipped => Items.Where(i => !i.CanProcess).ToList();
}

/// <summary>Onaylanan tek bir ad değişikliği.</summary>
public sealed record RenameRequest(int ReportId, string NewName);

// ----------------------------------------------------------------------
// Excel ile toplu keyword güncelleme
// ----------------------------------------------------------------------

public enum KeywordMode
{
    /// <summary>Mevcut keyword'lere ekle</summary>
    Add,

    /// <summary>Mevcut keyword'leri tamamen değiştir</summary>
    Replace
}

/// <summary>Excel'deki bir satır: CUID + keyword'ler + işlem.</summary>
public sealed record KeywordRow(int RowNumber, string Cuid, string Keywords, KeywordMode Mode);

/// <summary>Keyword güncelleme önizlemesindeki bir rapor.</summary>
public sealed record KeywordPreviewItemDto(
    int RowNumber,
    ReportListItemDto Report,
    KeywordMode Mode,
    string Requested,
    string Current,
    string Result,
    bool CanProcess,
    string? Reason = null);

public sealed record KeywordPreviewDto(
    IReadOnlyList<KeywordPreviewItemDto> Items,
    IReadOnlyList<RenameRowIssue> NotFound,
    IReadOnlyList<RenameRowIssue> InvalidRows)
{
    public IReadOnlyList<KeywordPreviewItemDto> Processable => Items.Where(i => i.CanProcess).ToList();
    public IReadOnlyList<KeywordPreviewItemDto> Skipped => Items.Where(i => !i.CanProcess).ToList();
}

/// <summary>Onaylanan tek bir keyword değişikliği (sonuç, işlem anındaki güncel değerden yeniden hesaplanır).</summary>
public sealed record KeywordRequest(int ReportId, string Keywords, KeywordMode Mode);
