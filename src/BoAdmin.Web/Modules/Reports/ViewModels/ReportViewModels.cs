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
    public ReportSearchCriteria Criteria { get; init; } = new();
    public PagedResult<ReportListItemDto>? Result { get; set; }
    public IReadOnlyList<FolderDto> Folders { get; set; } = Array.Empty<FolderDto>();
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<ReportKindInfo> Kinds => ReportKinds.All;

    public static readonly int[] PageSizes = { 25, 50, 100, 200 };

    /// <summary>Mevcut filtreleri koruyarak sayfa/sıralama linki üretir.</summary>
    public Dictionary<string, string> RouteValues(int? page = null, ReportSortField? sortBy = null, bool? sortDescending = null)
    {
        var c = Criteria;
        var d = new Dictionary<string, string>();

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
    public List<int> SelectedIds { get; set; } = new();
    public bool MoveToArchive { get; set; }
    public bool DeleteInstances { get; set; }
    public int ConfirmationCount { get; set; }
}

public sealed class DeleteResultViewModel
{
    public DeleteResultViewModel(IReadOnlyList<DeleteItemResult> results, bool moveToArchive)
    {
        Results = results;
        MoveToArchive = moveToArchive;
    }

    public IReadOnlyList<DeleteItemResult> Results { get; }
    public bool MoveToArchive { get; }
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailCount => Results.Count(r => !r.Success);
}

// ----------------------------------------------------------------------
// İşlem geçmişi
// ----------------------------------------------------------------------

public sealed class HistoryViewModel
{
    public AuditQuery Query { get; init; } = new();
    public PagedResult<AuditEntry>? Result { get; set; }

    public static readonly int[] PageSizes = { 25, 50, 100, 200 };

    public Dictionary<string, string> RouteValues(int? page = null)
    {
        var q = Query;
        var d = new Dictionary<string, string>();

        void Add(string key, object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(s)) d[$"Query.{key}"] = s;
        }

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
    };

    public static string Get(string? operation) => operation switch
    {
        nameof(ReportOperation.Delete) => "Silme",
        nameof(ReportOperation.Archive) => "Arşive taşıma",
        nameof(ReportOperation.DeleteInstance) => "Instance silme",
        _ => operation ?? string.Empty
    };

    public static string Get(ReportOperation operation) => Get(operation.ToString());
}
