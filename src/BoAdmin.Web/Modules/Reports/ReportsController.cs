using System.Globalization;
using System.Text;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Modules.Reports.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Modules.Reports;

/// <summary>
/// "Rapor İşlemleri" sekmesi.
///  GET  /Reports                -> arama + liste
///  GET  /Reports/DeletePreview  -> silme önizleme (yan etkisiz)
///  POST /Reports/Delete         -> silme / arşive taşıma
///  GET  /Reports/History        -> İşlem Geçmişi
///  GET  /Reports/ExportHistory  -> İşlem Geçmişi CSV
/// </summary>
[Authorize(Policy = ModuleNames.Reports)]
public sealed class ReportsController : Controller
{
    private const int MaxExportRows = 10_000;

    private readonly IReportService _reports;
    private readonly IAuditLogService _audit;

    public ReportsController(IReportService reports, IAuditLogService audit)
    {
        _reports = reports;
        _audit = audit;
    }

    // ------------------------------------------------------------------
    // Arama
    // ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index(ReportSearchCriteria criteria, CancellationToken ct)
    {
        var vm = new ReportSearchViewModel { Criteria = criteria };

        // BO'ya ulaşılamasa bile form görünsün; hata sayfanın üstünde gösterilir
        try
        {
            vm.Folders = await _reports.GetFoldersAsync(ct);
            vm.Result = await _reports.SearchAsync(criteria, ct);
        }
        catch (BoException ex)
        {
            vm.ErrorMessage = ex.UserMessage;
        }
        catch (AppValidationException ex)
        {
            vm.ErrorMessage = ex.UserMessage;
        }

        return View(vm);
    }

    // ------------------------------------------------------------------
    // Silme
    // ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> DeletePreview([FromQuery] List<int> selectedIds, CancellationToken ct)
    {
        if (selectedIds.Count == 0)
        {
            TempData["Warning"] = "Lütfen en az bir rapor seçin.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var preview = await _reports.PrepareDeleteAsync(selectedIds, ct);
            return View(new DeletePreviewViewModel(preview));
        }
        catch (AppValidationException ex)
        {
            TempData["Warning"] = ex.UserMessage;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(DeleteRequestModel model)
    {
        try
        {
            var results = await _reports.DeleteAsync(model.SelectedIds, new DeleteOptions
            {
                MoveToArchive = model.MoveToArchive,
                DeleteInstances = model.DeleteInstances,
                ConfirmationCount = model.ConfirmationCount
            });

            return View("DeleteResult", new DeleteResultViewModel(results, model.MoveToArchive));
        }
        catch (AppValidationException ex)
        {
            // Onay sayısı hatalı vb.: önizlemeye hata mesajıyla geri dön
            var preview = await _reports.PrepareDeleteAsync(model.SelectedIds, HttpContext.RequestAborted);
            return View("DeletePreview", new DeletePreviewViewModel(preview)
            {
                MoveToArchive = model.MoveToArchive,
                DeleteInstances = model.DeleteInstances,
                ErrorMessage = ex.UserMessage
            });
        }
    }

    // ------------------------------------------------------------------
    // İşlem geçmişi
    // ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> History(AuditQuery query, CancellationToken ct)
    {
        query.Module = ReportService.ModuleName;

        // İlk açılışta son 30 gün
        if (Request.Query.Count == 0)
            query.From = DateTime.Today.AddDays(-30);

        var vm = new HistoryViewModel { Query = query };
        vm.Result = await _audit.QueryAsync(query, ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportHistory(AuditQuery query, CancellationToken ct)
    {
        query.Module = ReportService.ModuleName;
        query.PageSize = 200;

        var rows = new List<AuditEntry>();
        for (query.Page = 1; rows.Count < MaxExportRows; query.Page++)
        {
            var page = await _audit.QueryAsync(query, ct);
            rows.AddRange(page.Items);
            if (!page.HasNextPage) break;
        }

        // Türkçe Excel için: UTF-8 BOM + noktalı virgül ayırıcı
        var sb = new StringBuilder();
        sb.AppendLine("Tarih;Kullanıcı;Ad Soyad;İşlem;Rapor ID;Rapor Adı;Tür;Klasör;Hedef Klasör;Sonuç;Mesaj;Silinen Instance;Toplu İşlem No");

        foreach (var e in rows.Take(MaxExportRows))
        {
            sb.AppendJoin(';', new[]
            {
                Csv(e.TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
                Csv(e.UserName),
                Csv(e.DisplayName),
                Csv(OperationNames.Get(e.Operation)),
                Csv(e.ObjectId?.ToString(CultureInfo.InvariantCulture)),
                Csv(e.ObjectName),
                Csv(ReportKinds.GetDisplayName(e.ObjectKind)),
                Csv(e.FolderPath),
                Csv(e.TargetPath),
                Csv(e.Success ? "Başarılı" : "Başarısız"),
                Csv(e.Message),
                Csv(e.InstanceCount?.ToString(CultureInfo.InvariantCulture)),
                Csv(e.BatchId?.ToString())
            });
            sb.AppendLine();
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"islem-gecmisi-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>CSV hücresi: tırnak kaçışı + Excel formül enjeksiyonu koruması.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
