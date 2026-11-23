using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Principals;
using BoAdmin.Application.Common;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Models;
using BoAdmin.Web.Infrastructure.Jobs;
using BoAdmin.Web.Modules.Groups.ViewModels;
using BoAdmin.Web.Modules.Reports.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Modules.Groups;

/// <summary>
/// Grup Yönetimi.
///  GET  /Groups                  -> grup arama
///  GET  /Groups/Detail/{id}      -> grup bilgileri, üst/alt gruplar, kullanıcılar
///  POST /Groups/MembershipReport -> üyelik raporu (arka plan işi) -> Excel
///  GET  /Groups/Job/{id}, JobStatus/{id}, JobResult/{id}, JobFile/{id}
/// </summary>
[Authorize(Policy = ModuleNames.Groups)]
public sealed class GroupsController : Controller
{
    private readonly IPrincipalService _principals;
    private readonly CurrentEnvironmentAccessor _environment;
    private readonly IModuleAccessService _access;
    private readonly ICurrentUser _currentUser;
    private readonly JobService _jobs;

    public GroupsController(
        IPrincipalService principals,
        CurrentEnvironmentAccessor environment,
        IModuleAccessService access,
        ICurrentUser currentUser,
        JobService jobs)
    {
        _principals = principals;
        _environment = environment;
        _access = access;
        _currentUser = currentUser;
        _jobs = jobs;
    }

    [HttpGet]
    public async Task<IActionResult> Index(GroupSearchCriteria criteria, CancellationToken ct)
    {
        var vm = new GroupSearchViewModel
        {
            Criteria = criteria,
            Searched = Request.Query.ContainsKey(GroupSearchViewModel.SearchFlag)
        };

        if (!vm.Searched) return View(vm);

        try
        {
            vm.Result = await _principals.SearchGroupsAsync(criteria, ct);
        }
        catch (BoException ex)
        {
            vm.ErrorMessage = ex.UserMessage;
        }

        return View(vm);
    }

    [HttpGet("Groups/Detail/{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        GroupDetailDto? detail;
        try
        {
            detail = await _principals.GetGroupDetailAsync(id, ct);
        }
        catch (BoException ex)
        {
            TempData["Warning"] = ex.UserMessage;
            return RedirectToAction(nameof(Index));
        }

        if (detail is null)
        {
            TempData["Warning"] = "Grup bu ortamda bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["NavAction"] = nameof(Index);
        return View(new GroupDetailViewModel(detail)
        {
            CanViewUsers = _access.HasAccess(User, _environment.Current.Key, ModuleNames.Users)
        });
    }

    // ------------------------------------------------------------------
    // Üyelik raporu
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Membership() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MembershipReport(MembershipReportRequest request, CancellationToken ct)
    {
        var env = _environment.Current;
        string scope;

        if (request.GroupId is > 0)
        {
            GroupListItemDto? group;
            try
            {
                group = (await _principals.GetGroupDetailAsync(request.GroupId.Value, ct))?.Group;
            }
            catch (BoException ex)
            {
                TempData["Warning"] = ex.UserMessage;
                return RedirectToAction(nameof(Membership));
            }

            if (group is null)
            {
                TempData["Warning"] = "Grup bu ortamda bulunamadı.";
                return RedirectToAction(nameof(Membership));
            }
            scope = $"'{group.Name}' grubu";
        }
        else
        {
            scope = string.IsNullOrWhiteSpace(request.Text) ? "Tüm gruplar" : $"Adında '{request.Text.Trim()}' geçen gruplar";
        }

        var groupId = request.GroupId is > 0 ? request.GroupId : null;
        var text = request.Text;
        var nested = request.IncludeNested;

        var job = _jobs.Start("Üyelik raporu hazırlanıyor", nameof(Membership), _currentUser.UserName, _currentUser.DisplayName,
            env, PrincipalService.MembershipReportSteps,
            async (sp, progress, jobCt) =>
            {
                var service = sp.GetRequiredService<IPrincipalService>();
                var report = await service.BuildMembershipReportAsync(groupId, text, nested, progress, jobCt);
                return new MembershipReadyViewModel
                {
                    Report = report,
                    Scope = scope + (nested ? " (alt gruplar dahil)" : ""),
                    FileName = $"bo-grup-uyelikleri-{env.Key.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx",
                    Content = PrincipalExcelWriter.Membership(report, env.DisplayName)
                };
            });

        return RedirectToAction(nameof(Job), new { id = job.Id });
    }

    // ------------------------------------------------------------------
    // Excel ile gruba alt grup ekleme / çıkarma
    // ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Subgroups() =>
        View("MembershipUpload", MembershipFlow.UploadModel(MemberType.Group, "Groups", nameof(SubgroupsPreview)));

    [HttpGet]
    public IActionResult MembersTemplate() =>
        File(MembershipFileReader.CreateTemplate(MemberType.Group), PrincipalExcelWriter.ContentType,
            MemberType.Group == MemberType.User ? "gruba-kullanici-ekleme-sablonu.xlsx" : "gruba-alt-grup-ekleme-sablonu.xlsx");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MembershipFlow.MaxFileBytes + 64 * 1024)]
    public Task<IActionResult> SubgroupsPreview(IFormFile? file, [FromServices] MembershipFlow flow, CancellationToken ct) =>
        flow.PreviewAsync(this, MemberType.Group, file, nameof(Subgroups), nameof(SubgroupsPreview), nameof(SubgroupsExecute), nameof(Job), ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SubgroupsExecute(MembershipExecuteModel model, [FromServices] MembershipFlow flow) =>
        flow.Execute(this, model, nameof(Subgroups), nameof(Job), nameof(JobResult));

    // ------------------------------------------------------------------
    // Arka plan işleri
    // ------------------------------------------------------------------

    [HttpGet("Groups/Job/{id:guid}")]
    public IActionResult Job(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        if (job is null)
        {
            TempData["Warning"] = "İşlem bulunamadı veya süresi doldu.";
            return RedirectToAction(nameof(Membership));
        }

        ViewData["NavAction"] = job.NavAction;
        return View("JobProgress", new JobViewModel(job.Id, job.Title, job.Environment,
            Url.Action(nameof(JobStatus), new { id })!, Url.Action(nameof(JobResult), new { id })!,
            Url.Action(job.NavAction)!));
    }

    [HttpGet("Groups/JobStatus/{id:guid}")]
    public IActionResult JobStatus(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        return job is null ? NotFound(new { error = "İşlem bulunamadı." }) : Json(JobStatusJson.From(job));
    }

    [HttpGet("Groups/JobResult/{id:guid}")]
    public IActionResult JobResult(Guid id)
    {
        var job = _jobs.Get(id, _currentUser.UserName);
        if (job is null)
        {
            TempData["Warning"] = "İşlem bulunamadı veya süresi doldu.";
            return RedirectToAction(nameof(Membership));
        }

        if (job.State == JobState.Running) return RedirectToAction(nameof(Job), new { id });

        if (job.State == JobState.Failed)
        {
            TempData["Warning"] = job.Error;
            return RedirectToAction(job.NavAction);
        }

        ViewData["NavAction"] = job.NavAction;
        if (job.Result is MembershipReadyViewModel ready)
        {
            ready.JobId = job.Id;
            return View("MembershipReady", ready);
        }

        switch (job.Result)
        {
            case MembershipPreviewViewModel membershipPreview:
                membershipPreview.PreviewJobId = job.Id;
                return View("MembershipPreview", membershipPreview);
            case MembershipResultViewModel membershipResult:
                return View("MembershipResult", membershipResult);
        }

        return RedirectToAction(job.NavAction);
    }

    [HttpGet("Groups/JobFile/{id:guid}")]
    public IActionResult JobFile(Guid id)
    {
        if (_jobs.Get(id, _currentUser.UserName)?.Result is MembershipReadyViewModel ready)
            return File(ready.Content, PrincipalExcelWriter.ContentType, ready.FileName);

        TempData["Warning"] = "Rapor bulunamadı veya süresi doldu. Lütfen yeniden oluşturun.";
        return RedirectToAction(nameof(Membership));
    }
}
