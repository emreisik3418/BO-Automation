using System.Globalization;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;

namespace BoAdmin.Web.Modules.Reports.ViewModels;

// ----------------------------------------------------------------------
// Rapor arama
// ----------------------------------------------------------------------

public sealed class ReportSearchViewModel
{
    /// <summary>Arama formundaki gizli alan; yalnızca bu varsa BO'da arama yapılır.</summary>
    public const string SearchFlag = "search";

    public ReportSearchCriteria Criteria { get; init; } = new();
    public PagedResult<ReportListItemDto>? Result { get; set; }

    /// <summary>Seçili klasör (otomatik tamamlama kutusunda yolu gösterilir).</summary>
    public FolderDto? SelectedFolder { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Kullanıcı arama yaptı mı (ilk açılışta false: BO'ya istek atılmaz).</summary>
    public bool Searched { get; set; }

    public IReadOnlyList<ReportKindInfo> Kinds => ReportKinds.All;

    public static readonly int[] PageSizes = { 20, 50, 100, 200 };

    /// <summary>Hızlı filtre linkleri (ilk açılış ekranı).</summary>
    public static Dictionary<string, string> QuickFilter(int? lastDays = null, string? kind = null)
    {
        var d = new Dictionary<string, string>
        {
            [SearchFlag] = "1",
            ["Criteria.SortBy"] = nameof(ReportSortField.UpdatedAt),
            ["Criteria.SortDescending"] = "True",
            ["Criteria.PageSize"] = "20"
        };
        if (lastDays is not null)
            d["Criteria.UpdatedFrom"] = DateTime.Today.AddDays(-lastDays.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (kind is not null)
            d["Criteria.Kind"] = kind;
        return d;
    }

    /// <summary>Mevcut filtreleri koruyarak sayfa/sıralama linki üretir.</summary>
    public Dictionary<string, string> RouteValues(int? page = null, ReportSortField? sortBy = null, bool? sortDescending = null)
    {
        var c = Criteria;
        var d = new Dictionary<string, string> { [SearchFlag] = "1" };

        void Add(string key, object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(s)) d[$"Criteria.{key}"] = s;
        }

        Add(nameof(c.NameContains), c.NameContains);
        Add(nameof(c.FolderId), c.FolderId);
        Add(nameof(c.IncludeSubfolders), c.IncludeSubfolders);
        Add(nameof(c.OwnerContains), c.OwnerContains);
        Add(nameof(c.Kind), c.Kind);
        Add(nameof(c.UpdatedFrom), c.UpdatedFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(nameof(c.UpdatedTo), c.UpdatedTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(nameof(c.SortBy), sortBy ?? c.SortBy);
        Add(nameof(c.SortDescending), sortDescending ?? c.SortDescending);
        Add(nameof(c.PageSize), c.PageSize);
        Add(nameof(c.Page), page ?? c.Page);

        return d;
    }

    /// <summary>Başlığa tıklanınca: aynı sütunsa yön değişir, farklıysa artan sıralama. Sayfa 1'e döner.</summary>
    public Dictionary<string, string> SortRouteValues(ReportSortField field) =>
        RouteValues(page: 1, sortBy: field,
            sortDescending: field == Criteria.SortBy ? !Criteria.SortDescending : false);

    public string SortIndicator(ReportSortField field) =>
        field != Criteria.SortBy ? string.Empty : Criteria.SortDescending ? "▼" : "▲";
}

// ----------------------------------------------------------------------
// Silme
// ----------------------------------------------------------------------

public sealed class DeletePreviewViewModel
{
    public DeletePreviewViewModel(DeletePreviewDto preview) => Preview = preview;

    public DeletePreviewDto Preview { get; }
    public bool MoveToArchive { get; set; }
    public bool DeleteInstances { get; set; }
    public string? ErrorMessage { get; set; }

    public int Count => Preview.Items.Count;
    public bool IsMulti => Count > 1;
}

/// <summary>Onay formundan gelen veri.</summary>
public sealed class DeleteRequestModel
{
    /// <summary>Önizlemenin hazırlandığı ortam; işlem anındaki ortamla eşleşmeli.</summary>
    public string Environment { get; set; } = string.Empty;

    public List<int> SelectedIds { get; set; } = new();
    public bool MoveToArchive { get; set; }
    public bool DeleteInstances { get; set; }
    public int ConfirmationCount { get; set; }
}

public sealed class DeleteResultViewModel
{
    public DeleteResultViewModel(IReadOnlyList<DeleteItemResult> results, ReportOperation operation, string? targetPath = null)
    {
        Results = results;
        Operation = operation;
        TargetPath = targetPath;
    }

    public IReadOnlyList<DeleteItemResult> Results { get; }
    public ReportOperation Operation { get; }
    public string? TargetPath { get; }
    public string OperationText => OperationNames.Get(Operation);
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailCount => Results.Count(r => !r.Success);
}

// ----------------------------------------------------------------------
// TXT ile toplu yedekleme
// ----------------------------------------------------------------------

/// <summary>Toplu işlem yükleme formu.</summary>
public sealed class BulkUploadViewModel
{
    public int? TargetFolderId { get; set; }
    public string? ErrorMessage { get; set; }
    public int MaxCount => ReportService.MaxBulkSize;
}

public sealed class BulkPreviewViewModel
{
    public BulkPreviewViewModel(BulkBackupPreviewDto preview) => Preview = preview;

    public BulkBackupPreviewDto Preview { get; }
    public string? FileName { get; set; }

    /// <summary>Önizlemenin hazırlandığı ortam (onay formunda geri gönderilir).</summary>
    public string? EnvironmentKey { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Kopyalanıp silinecek raporlar.</summary>
    public IReadOnlyList<BulkPreviewItemDto> ToProcess => Preview.Processable;

    /// <summary>İşleme alınmayacak raporlar (ör. Webi dışı türler).</summary>
    public IReadOnlyList<BulkPreviewItemDto> Skipped => Preview.Skipped;
}

// ----------------------------------------------------------------------
// Excel ile toplu ad güncelleme
// ----------------------------------------------------------------------

public sealed class RenameUploadViewModel
{
    public string? ErrorMessage { get; set; }
    public int MaxCount => ReportService.MaxBulkSize;
}

public sealed class RenamePreviewViewModel
{
    public RenamePreviewViewModel(RenamePreviewDto preview) => Preview = preview;

    public RenamePreviewDto Preview { get; }
    public string? FileName { get; set; }
    public string? EnvironmentKey { get; set; }

    /// <summary>Önizlemeyi hazırlayan iş; onayda değişiklikler buradan alınır.</summary>
    public Guid PreviewJobId { get; set; }
}

public sealed class RenameExecuteModel
{
    public string Environment { get; set; } = string.Empty;
    public Guid PreviewJobId { get; set; }
    public int ConfirmationCount { get; set; }
}

/// <summary>Arka plan işinin ilerleme ekranı.</summary>
public sealed record JobViewModel(
    Guid Id,
    string Title,
    BoAdmin.Web.Infrastructure.EnvironmentInfo Environment,
    string StatusUrl,
    string ResultUrl,
    string BackUrl);

/// <summary>Toplu işlem onay formundan gelen veri.</summary>
public sealed class BulkExecuteModel
{
    public string Environment { get; set; } = string.Empty;
    public int TargetFolderId { get; set; }
    public List<int> SelectedIds { get; set; } = new();
    public int ConfirmationCount { get; set; }
}

// ----------------------------------------------------------------------
// İşlem geçmişi
// ----------------------------------------------------------------------

public sealed class HistoryViewModel
{
    public AuditQuery Query { get; init; } = new();
    public PagedResult<AuditEntry>? Result { get; set; }

    public static readonly int[] PageSizes = { 20, 50, 100, 200 };

    public Dictionary<string, string> RouteValues(int? page = null)
    {
        var q = Query;
        var d = new Dictionary<string, string>();

        void Add(string key, object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(s)) d[$"Query.{key}"] = s;
        }

        Add(nameof(q.Environment), q.Environment);
        Add(nameof(q.UserContains), q.UserContains);
        Add(nameof(q.ObjectContains), q.ObjectContains);
        Add(nameof(q.Operation), q.Operation);
        Add(nameof(q.Success), q.Success);
        Add(nameof(q.From), q.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(nameof(q.To), q.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(nameof(q.PageSize), q.PageSize);
        Add(nameof(q.Page), page ?? q.Page);

        return d;
    }
}

/// <summary>İşlem adlarının Türkçe karşılıkları.</summary>
public static class OperationNames
{
    public static readonly IReadOnlyList<(string Value, string Text)> All = new[]
    {
        (nameof(ReportOperation.Delete), "Silme"),
        (nameof(ReportOperation.Archive), "Arşive taşıma"),
        (nameof(ReportOperation.BackupCopy), "Toplu yedekle + sil (TXT)"),
        (nameof(ReportOperation.Rename), "Ad güncelleme"),
    };

    public static string Get(string? operation) => operation switch
    {
        nameof(ReportOperation.Delete) => "Silme",
        nameof(ReportOperation.Archive) => "Arşive taşıma",
        nameof(ReportOperation.DeleteInstance) => "Instance silme",
        nameof(ReportOperation.BackupCopy) => "Toplu yedekle + sil (TXT)",
        nameof(ReportOperation.Rename) => "Ad güncelleme",
        "BackupMove" => "Toplu yedekleme (TXT, taşıma)", // eski kayıtlar
        _ => operation ?? string.Empty
    };

    public static string Get(ReportOperation operation) => Get(operation.ToString());
}
