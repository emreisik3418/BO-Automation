using System.Globalization;
using System.Text;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Infrastructure.Jobs;
using BoAdmin.Web.Modules.Reports.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Modules.Reports;

/// <summary>
/// "Rapor Yönetişimi" modülü (seçili ortamda).
///  GET  /Reports                -> arama formu; ?search=1 ile arama + liste
///  GET  /Reports/Folders?q=     -> klasör otomatik tamamlama (JSON)
///  GET  /Reports/DeletePreview  -> silme önizleme (yan etkisiz)
///  POST /Reports/Delete         -> silme / arşive taşıma
///  GET  /Reports/Bulk           -> TXT ile toplu yedekleme: dosya yükleme
///  POST /Reports/BulkPreview    -> TXT önizleme (arka plan işi başlatır)
///  POST /Reports/BulkExecute    -> yedek klasörüne kopyala ({ad}_{CUID}) + orijinali sil (arka plan işi)
///  GET  /Reports/Rename         -> Excel ile toplu ad güncelleme: dosya yükleme
///  GET  /Reports/RenameTemplate -> boş Excel şablonu
///  POST /Reports/RenamePreview  -> Excel önizleme (arka plan işi)
///  POST /Reports/RenameExecute  -> ad güncelleme (arka plan işi)
///  GET  /Reports/Keywords       -> Excel ile toplu keyword güncelleme: dosya yükleme
///  GET  /Reports/KeywordsTemplate -> boş Excel şablonu
///  POST /Reports/KeywordsPreview  -> Excel önizleme (arka plan işi)
///  POST /Reports/KeywordsExecute  -> keyword güncelleme (arka plan işi)
///  GET  /Reports/Descriptions         -> Excel ile toplu açıklama güncelleme: dosya yükleme
///  GET  /Reports/DescriptionsTemplate -> boş Excel şablonu
///  POST /Reports/DescriptionsPreview  -> Excel önizleme (arka plan işi)
///  POST /Reports/DescriptionsExecute  -> açıklama güncelleme (arka plan işi)
///  GET  /Reports/Job/{id}       -> ilerleme ekranı
///  GET  /Reports/JobStatus/{id} -> ilerleme durumu (JSON, saniyede bir sorulur)
///  GET  /Reports/JobResult/{id} -> iş bitince sonuç (önizleme veya sonuç sayfası)
///  GET  /Reports/History        -> İşlem Geçmişi
///  GET  /Reports/ExportHistory  -> İşlem Geçmişi CSV
/// </summary>
[Authorize(Policy = ModuleNames.Reports)]
public sealed class ReportsController : Controller
{
    private const int MaxExportRows = 10_000;
    private const long MaxUploadBytes = 1024 * 1024; // 1 MB
    private const long MaxExcelBytes = 5 * 1024 * 1024; // 5 MB

    private readonly IReportService _reports;
    private readonly IAuditLogService _audit;
    private readonly ICurrentEnvironment _environment;
    private readonly CurrentEnvironmentAccessor _environmentAccessor;
    private readonly IModuleAccessService _accessService;
    private readonly ICurrentUser _currentUser;
    private readonly JobService _jobs;

    public ReportsController(
        IReportService reports,
        IAuditLogService audit,
        ICurrentEnvironment environment,
        CurrentEnvironmentAccessor environmentAccessor,
        IModuleAccessService accessService,
        ICurrentUser currentUser,
        JobService jobs)
    {
        _reports = reports;
        _audit = audit;
        _environment = environment;
        _environmentAccessor = environmentAccessor;
        _accessService = accessService;
        _currentUser = currentUser;
        _jobs = jobs;
    }

    // ------------------------------------------------------------------
    // Arama
    // ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index(ReportSearchCriteria criteria, CancellationToken ct)
    {
        var vm = new ReportSearchViewModel
        {
            Criteria = criteria,
            // İlk açılışta BO'ya istek atılmaz; kullanıcı "Ara" veya hızlı filtre ile başlatır
            Searched = Request.Query.ContainsKey(ReportSearchViewModel.SearchFlag)
        };

        if (!vm.Searched)
            return View(vm);

        // BO'ya ulaşılamasa bile form görünsün; hata sayfanın üstünde gösterilir
        try
        {
            if (criteria.FolderId is > 0)
                vm.SelectedFolder = await _reports.GetFolderAsync(criteria.FolderId.Value, ct);

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

    /// <summary>Klasör kutusu için otomatik tamamlama: [{ id, name, path }]</summary>
    [HttpGet]
    public async Task<IActionResult> Folders(string? q, CancellationToken ct)
    {
        try
        {
            var folders = await _reports.SearchFoldersAsync(q ?? string.Empty, 25, ct);
            return Json(folders.Select(f => new { id = f.Id, name = f.Name, path = f.Path }));
        }
        catch (BoException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.UserMessage });
        }
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

        if (targetFolderId is not > 0)
        {
            form.ErrorMessage = "Lütfen geçerli bir yedek klasör ID'si girin.";
            return View("Bulk", form);
        }

        // Önizleme arka planda hazırlanır; kullanıcı ilerleme ekranına yönlendirilir
        var fileName = Path.GetFileName(file.FileName);
        var folderId = targetFolderId.Value;
        var env = _environmentAccessor.Current;

        var job = _jobs.Start("Rapor silme önizlemesi hazırlanıyor", nameof(Bulk), _currentUser.UserName, _currentUser.DisplayName,
            env, ReportService.BulkPreviewSteps,
            async (sp, progress, jobCt) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var preview = await service.PrepareBulkBackupAsync(content, folderId, progress, jobCt);
                return new BulkPreviewViewModel(preview) { FileName = fileName, EnvironmentKey = env.Key };
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult BulkExecute(BulkExecuteModel model)
    {
        if (!EnvironmentMatches(model.Environment, out var redirect, nameof(Bulk)))
            return redirect;

        var ids = model.SelectedIds.Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0 || model.ConfirmationCount != ids.Count)
        {
            TempData["Warning"] = $"Onay için yazılan sayı ({model.ConfirmationCount}) işlenecek rapor sayısıyla ({ids.Count}) eşleşmiyor. Lütfen dosyayı yeniden yükleyin.";
            return RedirectToAction(nameof(Bulk));
        }

        var folderId = model.TargetFolderId;
        var confirmation = model.ConfirmationCount;

        var job = _jobs.Start($"{ids.Count} rapor yedeklenip siliniyor", nameof(Bulk), _currentUser.UserName, _currentUser.DisplayName,
            _environmentAccessor.Current, ReportService.BulkExecuteSteps,
            async (sp, progress, _) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var results = await service.ExecuteBulkBackupAsync(ids, folderId, confirmation, progress);
                return new DeleteResultViewModel(results, ReportOperation.BackupCopy);
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    // ------------------------------------------------------------------
    // Excel ile toplu ad güncelleme
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Rename() => View(new RenameUploadViewModel());

    [HttpGet]
    public IActionResult RenameTemplate() =>
        File(RenameFileReader.CreateTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "rapor-ad-guncelleme-sablonu.xlsx");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExcelBytes + 64 * 1024)]
    public async Task<IActionResult> RenamePreview(IFormFile? file, CancellationToken ct)
    {
        var form = new RenameUploadViewModel();

        if (file is null || file.Length == 0)
        {
            form.ErrorMessage = "Lütfen CUID ve yeni adları içeren bir Excel dosyası (.xlsx) seçin.";
            return View("Rename", form);
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) && !string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            form.ErrorMessage = "Yalnızca .xlsx dosyası yüklenebilir. Eski .xls dosyasını Excel'de 'Farklı Kaydet > .xlsx' ile dönüştürün.";
            return View("Rename", form);
        }

        if (file.Length > MaxExcelBytes)
        {
            form.ErrorMessage = "Dosya en fazla 5 MB olabilir.";
            return View("Rename", form);
        }

        RenameFileReader.ReadResult read;
        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;
            read = RenameFileReader.Read(ms);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            form.ErrorMessage = "Excel dosyası okunamadı. Dosyanın bozuk veya parola korumalı olmadığından emin olun.";
            return View("Rename", form);
        }

        var fileName = Path.GetFileName(file.FileName);
        var env = _environmentAccessor.Current;

        var job = _jobs.Start("Ad güncelleme önizlemesi hazırlanıyor", nameof(Rename), _currentUser.UserName, _currentUser.DisplayName,
            env, ReportService.RenamePreviewSteps,
            async (sp, progress, jobCt) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var preview = await service.PrepareRenameAsync(read.Rows, read.Issues, progress, jobCt);
                return new RenamePreviewViewModel(preview) { FileName = fileName, EnvironmentKey = env.Key };
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RenameExecute(RenameExecuteModel model)
    {
        if (!EnvironmentMatches(model.Environment, out var redirect, nameof(Rename)))
            return redirect;

        // Değişiklikler formdan değil, sunucudaki önizlemeden alınır (değiştirilemez)
        var previewJob = _jobs.Get(model.PreviewJobId, _currentUser.UserName);
        if (previewJob?.Result is not RenamePreviewViewModel preview)
        {
            TempData["Warning"] = "Önizleme bulunamadı veya süresi doldu. Lütfen Excel dosyasını yeniden yükleyin.";
            return RedirectToAction(nameof(Rename));
        }

        var requests = preview.Preview.Processable
            .Select(i => new RenameRequest(i.Report.Id, i.NewName))
            .ToList();

        if (requests.Count == 0 || model.ConfirmationCount != requests.Count)
        {
            TempData["Warning"] = $"Onay için yazılan sayı ({model.ConfirmationCount}) güncellenecek rapor sayısıyla ({requests.Count}) eşleşmiyor.";
            return RedirectToAction(nameof(JobResult), new { id = model.PreviewJobId });
        }

        var confirmation = model.ConfirmationCount;
        var job = _jobs.Start($"{requests.Count} raporun adı güncelleniyor", nameof(Rename), _currentUser.UserName, _currentUser.DisplayName,
            _environmentAccessor.Current, ReportService.RenameExecuteSteps,
            async (sp, progress, _) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var results = await service.ExecuteRenameAsync(requests, confirmation, progress);
                return new DeleteResultViewModel(results, ReportOperation.Rename);
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    // ------------------------------------------------------------------
    // Excel ile toplu keyword güncelleme
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Keywords() => View(new KeywordUploadViewModel());

    [HttpGet]
    public IActionResult KeywordsTemplate() =>
        File(KeywordFileReader.CreateTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "rapor-keyword-guncelleme-sablonu.xlsx");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExcelBytes + 64 * 1024)]
    public async Task<IActionResult> KeywordsPreview(IFormFile? file, CancellationToken ct)
    {
        var form = new KeywordUploadViewModel();

        if (file is null || file.Length == 0)
        {
            form.ErrorMessage = "Lütfen CUID ve keyword'leri içeren bir Excel dosyası (.xlsx) seçin.";
            return View("Keywords", form);
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) && !string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            form.ErrorMessage = "Yalnızca .xlsx dosyası yüklenebilir. Eski .xls dosyasını Excel'de 'Farklı Kaydet > .xlsx' ile dönüştürün.";
            return View("Keywords", form);
        }

        if (file.Length > MaxExcelBytes)
        {
            form.ErrorMessage = "Dosya en fazla 5 MB olabilir.";
            return View("Keywords", form);
        }

        KeywordFileReader.ReadResult read;
        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;
            read = KeywordFileReader.Read(ms);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            form.ErrorMessage = "Excel dosyası okunamadı. Dosyanın bozuk veya parola korumalı olmadığından emin olun.";
            return View("Keywords", form);
        }

        var fileName = Path.GetFileName(file.FileName);
        var env = _environmentAccessor.Current;

        var job = _jobs.Start("Keyword önizlemesi hazırlanıyor", nameof(Keywords), _currentUser.UserName, _currentUser.DisplayName,
            env, ReportService.KeywordPreviewSteps,
            async (sp, progress, jobCt) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var preview = await service.PrepareKeywordsAsync(read.Rows, read.Issues, progress, jobCt);
                return new KeywordPreviewViewModel(preview) { FileName = fileName, EnvironmentKey = env.Key };
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult KeywordsExecute(KeywordExecuteModel model)
    {
        if (!EnvironmentMatches(model.Environment, out var redirect, nameof(Keywords)))
            return redirect;

        // Değişiklikler formdan değil, sunucudaki önizlemeden alınır
        var previewJob = _jobs.Get(model.PreviewJobId, _currentUser.UserName);
        if (previewJob?.Result is not KeywordPreviewViewModel preview)
        {
            TempData["Warning"] = "Önizleme bulunamadı veya süresi doldu. Lütfen Excel dosyasını yeniden yükleyin.";
            return RedirectToAction(nameof(Keywords));
        }

        var requests = preview.Preview.Processable
            .Select(i => new KeywordRequest(i.Report.Id, i.Requested, i.Mode))
            .ToList();

        if (requests.Count == 0 || model.ConfirmationCount != requests.Count)
        {
            TempData["Warning"] = $"Onay sayısı ({model.ConfirmationCount}) güncellenecek rapor sayısıyla ({requests.Count}) eşleşmiyor.";
            return RedirectToAction(nameof(JobResult), new { id = model.PreviewJobId });
        }

        var confirmation = model.ConfirmationCount;
        var job = _jobs.Start($"{requests.Count} raporun keyword'leri güncelleniyor", nameof(Keywords), _currentUser.UserName, _currentUser.DisplayName,
            _environmentAccessor.Current, ReportService.KeywordExecuteSteps,
            async (sp, progress, _) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var results = await service.ExecuteKeywordsAsync(requests, confirmation, progress);
                return new DeleteResultViewModel(results, ReportOperation.Keywords);
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    // ------------------------------------------------------------------
    // Excel ile toplu açıklama (description) güncelleme
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Descriptions() => View(new DescriptionUploadViewModel());

    [HttpGet]
    public IActionResult DescriptionsTemplate() =>
        File(DescriptionFileReader.CreateTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "rapor-aciklama-guncelleme-sablonu.xlsx");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExcelBytes + 64 * 1024)]
    public async Task<IActionResult> DescriptionsPreview(IFormFile? file, CancellationToken ct)
    {
        var form = new DescriptionUploadViewModel();

        if (file is null || file.Length == 0)
        {
            form.ErrorMessage = "Lütfen CUID ve yeni açıklamaları içeren bir Excel dosyası (.xlsx) seçin.";
            return View("Descriptions", form);
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) && !string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            form.ErrorMessage = "Yalnızca .xlsx dosyası yüklenebilir. Eski .xls dosyasını Excel'de 'Farklı Kaydet > .xlsx' ile dönüştürün.";
            return View("Descriptions", form);
        }

        if (file.Length > MaxExcelBytes)
        {
            form.ErrorMessage = "Dosya en fazla 5 MB olabilir.";
            return View("Descriptions", form);
        }

        DescriptionFileReader.ReadResult read;
        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;
            read = DescriptionFileReader.Read(ms);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            form.ErrorMessage = "Excel dosyası okunamadı. Dosyanın bozuk veya parola korumalı olmadığından emin olun.";
            return View("Descriptions", form);
        }

        var fileName = Path.GetFileName(file.FileName);
        var env = _environmentAccessor.Current;

        var job = _jobs.Start("Açıklama önizlemesi hazırlanıyor", nameof(Descriptions), _currentUser.UserName, _currentUser.DisplayName,
            env, ReportService.DescriptionPreviewSteps,
            async (sp, progress, jobCt) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var preview = await service.PrepareDescriptionsAsync(read.Rows, read.Issues, progress, jobCt);
                return new DescriptionPreviewViewModel(preview) { FileName = fileName, EnvironmentKey = env.Key };
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DescriptionsExecute(DescriptionExecuteModel model)
    {
        if (!EnvironmentMatches(model.Environment, out var redirect, nameof(Descriptions)))
            return redirect;

        // Değişiklikler formdan değil, sunucudaki önizlemeden alınır
        var previewJob = _jobs.Get(model.PreviewJobId, _currentUser.UserName);
        if (previewJob?.Result is not DescriptionPreviewViewModel preview)
        {
            TempData["Warning"] = "Önizleme bulunamadı veya süresi doldu. Lütfen Excel dosyasını yeniden yükleyin.";
            return RedirectToAction(nameof(Descriptions));
        }

        var requests = preview.Preview.Processable
            .Select(i => new DescriptionRequest(i.Report.Id, i.NewValue))
            .ToList();

        if (requests.Count == 0 || model.ConfirmationCount != requests.Count)
        {
            TempData["Warning"] = $"Onay sayısı ({model.ConfirmationCount}) güncellenecek rapor sayısıyla ({requests.Count}) eşleşmiyor.";
            return RedirectToAction(nameof(JobResult), new { id = model.PreviewJobId });
        }

        var confirmation = model.ConfirmationCount;
        var job = _jobs.Start($"{requests.Count} raporun açıklaması güncelleniyor", nameof(Descriptions), _currentUser.UserName, _currentUser.DisplayName,
            _environmentAccessor.Current, ReportService.DescriptionExecuteSteps,
            async (sp, progress, _) =>
            {
                var service = sp.GetRequiredService<IReportService>();
                var results = await service.ExecuteDescriptionsAsync(requests, confirmation, progress);
                return new DeleteResultViewModel(results, ReportOperation.Description);
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    // ------------------------------------------------------------------
    // Arka plan işleri: ilerleme ekranı
    // ------------------------------------------------------------------

    [HttpGet("Reports/Job/{id:guid}")]
    public IActionResult Job(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        if (job is null)
        {
            TempData["Warning"] = "İşlem bulunamadı veya süresi doldu. Sonuçları İşlem Geçmişi'nde görebilirsiniz.";
            return RedirectToAction(nameof(Bulk));
        }

        ViewData["NavAction"] = job.NavAction;
        return View(new JobViewModel(job.Id, job.Title, job.Environment,
            Url.Action(nameof(JobStatus), new { id })!, Url.Action(nameof(JobResult), new { id })!,
            Url.Action(job.NavAction)!));
    }

    [HttpGet("Reports/JobStatus/{id:guid}")]
    public IActionResult JobStatus(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        if (job is null) return NotFound(new { error = "İşlem bulunamadı." });

        var s = job.Snapshot();
        return Json(new
        {
            state = s.State.ToString().ToLowerInvariant(),
            title = s.Title,
            step = s.Step,
            stepCount = s.StepCount,
            phase = s.Phase,
            done = s.Done,
            total = s.Total,
            currentItem = s.CurrentItem,
            success = s.SuccessCount,
            fail = s.FailCount,
            elapsed = Math.Round(s.ElapsedSeconds),
            remaining = s.RemainingSeconds is null ? (double?)null : Math.Ceiling(s.RemainingSeconds.Value),
            percent = s.Percent,
            error = s.Error
        });
    }

    [HttpGet("Reports/JobResult/{id:guid}")]
    public IActionResult JobResult(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        if (job is null)
        {
            TempData["Warning"] = "İşlem bulunamadı veya süresi doldu. Sonuçları İşlem Geçmişi'nde görebilirsiniz.";
            return RedirectToAction(nameof(Bulk));
        }

        if (job.State == JobState.Running)
            return RedirectToAction(nameof(Job), new { id });

        if (job.State == JobState.Failed)
        {
            TempData["Warning"] = job.Error;
            return RedirectToAction(job.NavAction);
        }

        ViewData["NavAction"] = job.NavAction;

        switch (job.Result)
        {
            case BulkPreviewViewModel bulkPreview:
                return View("BulkPreview", bulkPreview);
            case RenamePreviewViewModel renamePreview:
                renamePreview.PreviewJobId = job.Id;
                return View("RenamePreview", renamePreview);
            case KeywordPreviewViewModel keywordPreview:
                keywordPreview.PreviewJobId = job.Id;
                return View("KeywordsPreview", keywordPreview);
            case DescriptionPreviewViewModel descriptionPreview:
                descriptionPreview.PreviewJobId = job.Id;
                return View("DescriptionsPreview", descriptionPreview);
            case DeleteResultViewModel result:
                return View("DeleteResult", result);
            default:
                return RedirectToAction(job.NavAction);
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
