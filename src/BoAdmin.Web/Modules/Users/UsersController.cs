using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Principals;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Models;
using BoAdmin.Web.Infrastructure.Jobs;
using BoAdmin.Web.Modules.Reports.ViewModels;
using BoAdmin.Web.Modules.Users.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Modules.Users;

/// <summary>
/// Kullanıcı Yönetimi.
///  GET /Users             -> kullanıcı arama (ilk açılışta BO'ya istek atılmaz)
///  GET /Users/Export      -> arama sonucunun tamamı Excel
///  GET /Users/Detail/{id} -> kullanıcı bilgileri + üye olduğu gruplar
///  GET /Users/Status, /Users/Remove        -> Excel ile toplu aktif/pasif ve silme (yükleme)
///  POST /Users/StatusPreview, RemovePreview -> önizleme (arka plan işi)
///  POST /Users/Execute                      -> onaylanan işlem (arka plan işi)
/// </summary>
[Authorize(Policy = ModuleNames.Users)]
public sealed class UsersController : Controller
{
    private readonly IPrincipalService _principals;
    private readonly CurrentEnvironmentAccessor _environment;
    private readonly IModuleAccessService _access;
    private readonly ICurrentUser _currentUser;
    private readonly JobService _jobs;
    private readonly ILogger<UsersController> _logger;

    private const long MaxFileBytes = 5 * 1024 * 1024;

    public UsersController(
        IPrincipalService principals,
        CurrentEnvironmentAccessor environment,
        IModuleAccessService access,
        ICurrentUser currentUser,
        JobService jobs,
        ILogger<UsersController> logger)
    {
        _principals = principals;
        _environment = environment;
        _access = access;
        _currentUser = currentUser;
        _jobs = jobs;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(UserSearchCriteria criteria, CancellationToken ct)
    {
        var vm = new UserSearchViewModel
        {
            Criteria = criteria,
            Searched = Request.Query.ContainsKey(UserSearchViewModel.SearchFlag)
        };

        if (!vm.Searched) return View(vm);

        try
        {
            vm.Result = await _principals.SearchUsersAsync(criteria, ct);
        }
        catch (Exception ex) when (ex is BoException or AppValidationException)
        {
            vm.ErrorMessage = ex is BoException bo ? bo.UserMessage : ((AppValidationException)ex).UserMessage;
        }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Export(UserSearchCriteria criteria, CancellationToken ct)
    {
        try
        {
            var users = await _principals.ExportUsersAsync(criteria, ct);
            var env = _environment.Current;

            _logger.LogInformation("Kullanıcı listesi Excel'e aktarıldı: {Count} kayıt, ortam {Environment}", users.Count, env.Key);

            return File(PrincipalExcelWriter.Users(users, env.DisplayName), PrincipalExcelWriter.ContentType,
                $"bo-kullanicilar-{env.Key.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        }
        catch (BoException ex)
        {
            TempData["Warning"] = ex.UserMessage;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("Users/Detail/{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        UserDetailDto? detail;
        try
        {
            detail = await _principals.GetUserDetailAsync(id, ct);
        }
        catch (BoException ex)
        {
            TempData["Warning"] = ex.UserMessage;
            return RedirectToAction(nameof(Index));
        }

        if (detail is null)
        {
            TempData["Warning"] = "Kullanıcı bu ortamda bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["NavAction"] = nameof(Index);
        return View(new UserDetailViewModel(detail)
        {
            CanViewGroups = _access.HasAccess(User, _environment.Current.Key, ModuleNames.Groups)
        });
    }

    // ------------------------------------------------------------------
    // Excel ile toplu aktif/pasif ve silme
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Status() => View("ActionUpload", new UserActionUploadViewModel { Kind = UserActionKind.SetStatus });

    [HttpGet]
    public IActionResult Remove() => View("ActionUpload", new UserActionUploadViewModel { Kind = UserActionKind.Delete });

    [HttpGet]
    public IActionResult Template(UserActionKind kind) =>
        File(UserActionFileReader.CreateTemplate(kind), PrincipalExcelWriter.ContentType,
            kind == UserActionKind.Delete ? "kullanici-silme-sablonu.xlsx" : "kullanici-aktif-pasif-sablonu.xlsx");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public Task<IActionResult> StatusPreview(IFormFile? file, CancellationToken ct) => PreviewAsync(UserActionKind.SetStatus, file, ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public Task<IActionResult> RemovePreview(IFormFile? file, CancellationToken ct) => PreviewAsync(UserActionKind.Delete, file, ct);

    private async Task<IActionResult> PreviewAsync(UserActionKind kind, IFormFile? file, CancellationToken ct)
    {
        var form = new UserActionUploadViewModel { Kind = kind };
        var backAction = NavActionOf(kind);

        if (file is null || file.Length == 0)
        {
            form.ErrorMessage = "Lütfen kullanıcı hesaplarını içeren bir dosya seçin.";
            return View("ActionUpload", form);
        }

        var excel = UserActionFileReader.IsExcel(file.FileName);
        var text = kind == UserActionKind.Delete && UserActionFileReader.IsText(file.FileName);
        if (!excel && !text)
        {
            form.ErrorMessage = kind == UserActionKind.Delete
                ? "Yalnızca .xlsx, .txt veya .csv dosyası yüklenebilir."
                : "Yalnızca .xlsx dosyası yüklenebilir.";
            return View("ActionUpload", form);
        }

        if (file.Length > MaxFileBytes)
        {
            form.ErrorMessage = "Dosya en fazla 5 MB olabilir.";
            return View("ActionUpload", form);
        }

        UserActionFileReader.ReadResult read;
        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;
            read = excel ? UserActionFileReader.ReadExcel(ms) : UserActionFileReader.ReadText(ms);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            form.ErrorMessage = "Dosya okunamadı. Dosyanın bozuk veya parola korumalı olmadığından emin olun.";
            return View("ActionUpload", form);
        }

        var fileName = Path.GetFileName(file.FileName);
        var env = _environment.Current;
        var title = kind == UserActionKind.Delete ? "Silme önizlemesi hazırlanıyor" : "Aktif / pasif önizlemesi hazırlanıyor";

        var job = _jobs.Start(title, backAction, _currentUser.UserName, _currentUser.DisplayName, env, UserActionService.PreviewSteps,
            async (sp, progress, jobCt) =>
            {
                var service = sp.GetRequiredService<IUserActionService>();
                var preview = await service.PrepareAsync(kind, read.Rows, read.Issues, progress, jobCt);
                return new UserActionPreviewViewModel { Preview = preview, FileName = fileName, EnvironmentKey = env.Key };
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Execute(UserActionExecuteModel model)
    {
        var previewJob = _jobs.Get(model.PreviewJobId, _currentUser.UserName);
        if (previewJob?.Result is not UserActionPreviewViewModel preview)
        {
            TempData["Warning"] = "Önizleme bulunamadı veya süresi doldu. Lütfen dosyayı yeniden yükleyin.";
            return RedirectToAction(nameof(Index));
        }

        var kind = preview.Preview.Kind;
        var backAction = NavActionOf(kind);

        if (!string.Equals(model.Environment, _environment.Current.Key, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(preview.EnvironmentKey, _environment.Current.Key, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Warning"] = $"İşlem yapılmadı: önizleme '{preview.EnvironmentKey}' ortamında hazırlanmıştı, şu an aktif ortam '{_environment.Current.Key}'. Lütfen işlemi yeniden başlatın.";
            return RedirectToAction(backAction);
        }

        // İşlenecek kullanıcılar formdan değil, sunucudaki önizlemeden alınır
        var requests = preview.Preview.Processable.Select(i => new UserActionRequest(i.User.Id, i.Disable)).ToList();
        if (requests.Count == 0 || model.ConfirmationCount != requests.Count)
        {
            TempData["Warning"] = $"Onay sayısı ({model.ConfirmationCount}) işlenecek kullanıcı sayısıyla ({requests.Count}) eşleşmiyor.";
            return RedirectToAction(nameof(JobResult), new { id = model.PreviewJobId });
        }

        var confirmation = model.ConfirmationCount;
        var title = kind == UserActionKind.Delete
            ? $"{requests.Count} kullanıcı siliniyor"
            : $"{requests.Count} kullanıcının durumu güncelleniyor";

        var job = _jobs.Start(title, backAction, _currentUser.UserName, _currentUser.DisplayName, _environment.Current,
            UserActionService.ExecuteSteps,
            async (sp, progress, _) =>
            {
                var service = sp.GetRequiredService<IUserActionService>();
                var results = await service.ExecuteAsync(kind, requests, confirmation, progress);
                return new UserActionResultViewModel { Kind = kind, Results = results };
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    private static string NavActionOf(UserActionKind kind) => kind == UserActionKind.Delete ? nameof(Remove) : nameof(Status);

    // ------------------------------------------------------------------
    // Excel ile gruba kullanıcı ekleme / çıkarma
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult GroupMembership() =>
        View("MembershipUpload", MembershipFlow.UploadModel(MemberType.User, "Users", nameof(GroupMembershipPreview)));

    [HttpGet]
    public IActionResult MembersTemplate() =>
        File(MembershipFileReader.CreateTemplate(MemberType.User), PrincipalExcelWriter.ContentType,
            MemberType.User == MemberType.User ? "gruba-kullanici-ekleme-sablonu.xlsx" : "gruba-alt-grup-ekleme-sablonu.xlsx");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MembershipFlow.MaxFileBytes + 64 * 1024)]
    public Task<IActionResult> GroupMembershipPreview(IFormFile? file, [FromServices] MembershipFlow flow, CancellationToken ct) =>
        flow.PreviewAsync(this, MemberType.User, file, nameof(GroupMembership), nameof(GroupMembershipPreview), nameof(GroupMembershipExecute), nameof(Job), ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GroupMembershipExecute(MembershipExecuteModel model, [FromServices] MembershipFlow flow) =>
        flow.Execute(this, model, nameof(GroupMembership), nameof(Job), nameof(JobResult));

    // ------------------------------------------------------------------
    // Arka plan işleri
    // ------------------------------------------------------------------

    [HttpGet("Users/Job/{id:guid}")]
    public IActionResult Job(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        if (job is null)
        {
            TempData["Warning"] = "İşlem bulunamadı veya süresi doldu.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["NavAction"] = job.NavAction;
        return View("JobProgress", new JobViewModel(job.Id, job.Title, job.Environment,
            Url.Action(nameof(JobStatus), new { id })!, Url.Action(nameof(JobResult), new { id })!,
            Url.Action(job.NavAction)!));
    }

    [HttpGet("Users/JobStatus/{id:guid}")]
    public IActionResult JobStatus(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        return job is null ? NotFound(new { error = "İşlem bulunamadı." }) : Json(JobStatusJson.From(job));
    }

    [HttpGet("Users/JobResult/{id:guid}")]
    public IActionResult JobResult(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        if (job is null)
        {
            TempData["Warning"] = "İşlem bulunamadı veya süresi doldu.";
            return RedirectToAction(nameof(Index));
        }

        if (job.State == JobState.Running) return RedirectToAction(nameof(Job), new { id });
        if (job.State == JobState.Failed)
        {
            TempData["Warning"] = job.Error;
            return RedirectToAction(job.NavAction);
        }

        ViewData["NavAction"] = job.NavAction;
        switch (job.Result)
        {
            case MembershipPreviewViewModel membershipPreview:
                membershipPreview.PreviewJobId = job.Id;
                return View("MembershipPreview", membershipPreview);
            case MembershipResultViewModel membershipResult:
                return View("MembershipResult", membershipResult);
            case UserActionPreviewViewModel preview:
                preview.PreviewJobId = job.Id;
                return View("ActionPreview", preview);
            case UserActionResultViewModel result:
                return View("ActionResult", result);
            default:
                return RedirectToAction(job.NavAction);
        }
    }
}
