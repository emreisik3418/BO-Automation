using System.Globalization;
using System.Text;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Modules.Reports.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Modules.Reports;

/// <summary>
/// "Rapor İşlemleri" sekmesi (seçili ortamda).
///  GET  /Reports                -> arama + liste
///  GET  /Reports/DeletePreview  -> silme önizleme (yan etkisiz)
///  POST /Reports/Delete         -> silme / arşive taşıma
///  GET  /Reports/Bulk           -> TXT ile toplu yedekleme: dosya yükleme
///  POST /Reports/BulkPreview    -> TXT önizleme
///  POST /Reports/BulkExecute    -> yedek klasörüne kopyala ({ad}_{CUID}) + orijinali sil
///  GET  /Reports/History        -> İşlem Geçmişi
///  GET  /Reports/ExportHistory  -> İşlem Geçmişi CSV
/// </summary>
[Authorize(Policy = ModuleNames.Reports)]
public sealed class ReportsController : Controller
{
    private const int MaxExportRows = 10_000;
    private const long MaxUploadBytes = 1024 * 1024; // 1 MB

    private readonly IReportService _reports;
    private readonly IAuditLogService _audit;
    private readonly ICurrentEnvironment _environment;
    private readonly IModuleAccessService _accessService;

    public ReportsController(
        IReportService reports,
        IAuditLogService audit,
        ICurrentEnvironment environment,
        IModuleAccessService accessService)
    {
        _reports = reports;
        _audit = audit;
        _environment = environment;
        _accessService = accessService;
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
    // Silme (arama ekranından)
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
        if (!EnvironmentMatches(model.Environment, out var redirect))
            return redirect;

        try
        {
            var results = await _reports.DeleteAsync(model.SelectedIds, new DeleteOptions
            {
                MoveToArchive = model.MoveToArchive,
                DeleteInstances = model.DeleteInstances,
                ConfirmationCount = model.ConfirmationCount
            });

            var operation = model.MoveToArchive ? ReportOperation.Archive : ReportOperation.Delete;
            return View("DeleteResult", new DeleteResultViewModel(results, operation));
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
    // TXT ile toplu yedekleme: kopyala ({ad}_{CUID}) + orijinali sil
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Bulk() => View(new BulkUploadViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
    public async Task<IActionResult> BulkPreview(IFormFile? file, int? targetFolderId, CancellationToken ct)
    {
        var form = new BulkUploadViewModel { TargetFolderId = targetFolderId };

        if (file is null || file.Length == 0)
        {
            form.ErrorMessage = "Lütfen CUID listesini içeren bir .txt dosyası seçin.";
            return View("Bulk", form);
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            form.ErrorMessage = "Yalnızca .txt dosyası yüklenebilir.";
            return View("Bulk", form);
        }

        if (file.Length > MaxUploadBytes)
        {
            form.ErrorMessage = "Dosya en fazla 1 MB olabilir.";
            return View("Bulk", form);
        }

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = await reader.ReadToEndAsync(ct);

        try
        {
            var preview = await _reports.PrepareBulkBackupAsync(content, targetFolderId ?? 0, ct);
            return View(new BulkPreviewViewModel(preview) { FileName = Path.GetFileName(file.FileName) });
        }
        catch (AppValidationException ex)
        {
            form.ErrorMessage = ex.UserMessage;
            return View("Bulk", form);
        }
        catch (BoException ex)
        {
            form.ErrorMessage = ex.UserMessage;
            return View("Bulk", form);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkExecute(BulkExecuteModel model)
    {
        if (!EnvironmentMatches(model.Environment, out var redirect, nameof(Bulk)))
            return redirect;

        try
        {
            var results = await _reports.ExecuteBulkBackupAsync(model.SelectedIds, model.TargetFolderId, model.ConfirmationCount);
            return View("DeleteResult", new DeleteResultViewModel(results, ReportOperation.BackupCopy));
        }
        catch (AppValidationException ex)
        {
            TempData["Warning"] = ex.UserMessage + " Lütfen dosyayı yeniden yükleyin.";
            return RedirectToAction(nameof(Bulk));
        }
    }

    // ------------------------------------------------------------------
    // İşlem geçmişi
    // ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> History(AuditQuery query, CancellationToken ct)
    {
        query.Module = ReportService.ModuleName;
        query.Environment = AllowedHistoryEnvironment(query.Environment);

        // İlk açılışta son 30 gün
        if (Request.Query.Count == 0)
            query.From = DateTime.Today.AddDays(-30);

        var vm = new HistoryViewModel { Query = query };
        vm.Result = await _audit.QueryAsync(query, ct);

        ViewBag.Environments = _accessService.GetAccessibleEnvironments(User)
            .Where(e => _accessService.HasAccess(User, e.Key, ModuleNames.Reports))
            .ToList();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportHistory(AuditQuery query, CancellationToken ct)
    {
        query.Module = ReportService.ModuleName;
        query.Environment = AllowedHistoryEnvironment(query.Environment);
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
        sb.AppendLine("Tarih;Ortam;Kullanıcı;Ad Soyad;İşlem;Rapor ID;CUID;Rapor Adı;Tür;Klasör;Hedef Klasör;Sonuç;Mesaj;Silinen Instance;Toplu İşlem No");

        foreach (var e in rows.Take(MaxExportRows))
        {
            sb.AppendJoin(';', new[]
            {
                Csv(e.TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
                Csv(e.Environment),
                Csv(e.UserName),
                Csv(e.DisplayName),
                Csv(OperationNames.Get(e.Operation)),
                Csv(e.ObjectId?.ToString(CultureInfo.InvariantCulture)),
                Csv(e.ObjectCuid),
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
        return File(bytes, "text/csv; charset=utf-8", $"islem-gecmisi-{query.Environment}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Önizleme bir ortamda hazırlanıp kullanıcı başka sekmede ortam değiştirdiyse işlemi durdurur.
    /// </summary>
    private bool EnvironmentMatches(string? formEnvironment, out IActionResult redirect, string returnAction = nameof(Index))
    {
        if (string.Equals(formEnvironment, _environment.Key, StringComparison.OrdinalIgnoreCase))
        {
            redirect = null!;
            return true;
        }

        TempData["Warning"] =
            $"İşlem yapılmadı: önizleme '{formEnvironment}' ortamında hazırlanmıştı, şu an aktif ortam '{_environment.Key}'. Lütfen işlemi yeniden başlatın.";
        redirect = RedirectToAction(returnAction);
        return false;
    }

    /// <summary>Kullanıcı yalnızca yetkili olduğu ortamların geçmişini görebilir; varsayılan aktif ortam.</summary>
    private string AllowedHistoryEnvironment(string? requested)
    {
        var allowed = _accessService.GetAccessibleEnvironments(User)
            .Where(e => _accessService.HasAccess(User, e.Key, ModuleNames.Reports))
            .Select(e => e.Key);

        return allowed.FirstOrDefault(k => string.Equals(k, requested, StringComparison.OrdinalIgnoreCase))
               ?? _environment.Key;
    }

    /// <summary>CSV hücresi: tırnak kaçışı + Excel formül enjeksiyonu koruması.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
