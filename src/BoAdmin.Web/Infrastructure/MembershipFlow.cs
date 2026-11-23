using BoAdmin.Application.Common;
using BoAdmin.Application.Principals;
using BoAdmin.Web.Infrastructure.Jobs;
using BoAdmin.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Infrastructure;

/// <summary>
/// Toplu üyelik değişikliğinin yükleme → önizleme → onay akışı.
/// Kullanıcı Yönetimi (gruba kullanıcı) ve Grup Yönetimi (gruba alt grup) aynı akışı kullanır.
/// İş ekranları (Job, JobResult) her controller'ın kendi uç noktalarıdır.
/// </summary>
public sealed class MembershipFlow
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly JobService _jobs;
    private readonly ICurrentUser _currentUser;
    private readonly CurrentEnvironmentAccessor _environment;

    public MembershipFlow(JobService jobs, ICurrentUser currentUser, CurrentEnvironmentAccessor environment)
    {
        _jobs = jobs;
        _currentUser = currentUser;
        _environment = environment;
    }

    public static MembershipUploadViewModel UploadModel(MemberType type, string controller, string previewAction) =>
        new() { Type = type, Controller = controller, PreviewAction = previewAction };

    /// <summary>Dosyayı okur ve önizleme işini başlatır. Hata varsa yükleme ekranı döner.</summary>
    public async Task<IActionResult> PreviewAsync(Controller c, MemberType type, IFormFile? file,
        string uploadAction, string previewAction, string executeAction, string jobAction, CancellationToken ct)
    {
        var controller = c.ControllerContext.ActionDescriptor.ControllerName;
        var form = UploadModel(type, controller, previewAction);

        if (file is null || file.Length == 0)
        {
            form.ErrorMessage = "Lütfen bir Excel dosyası (.xlsx) seçin.";
            return c.View("MembershipUpload", form);
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xlsm")
        {
            form.ErrorMessage = "Yalnızca .xlsx dosyası yüklenebilir.";
            return c.View("MembershipUpload", form);
        }

        if (file.Length > MaxFileBytes)
        {
            form.ErrorMessage = "Dosya en fazla 5 MB olabilir.";
            return c.View("MembershipUpload", form);
        }

        MembershipFileReader.ReadResult read;
        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            ms.Position = 0;
            read = MembershipFileReader.Read(ms, type);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            form.ErrorMessage = "Excel dosyası okunamadı. Dosyanın bozuk veya parola korumalı olmadığından emin olun.";
            return c.View("MembershipUpload", form);
        }

        var fileName = Path.GetFileName(file.FileName);
        var env = _environment.Current;

        var job = _jobs.Start("Üyelik önizlemesi hazırlanıyor", uploadAction, _currentUser.UserName, _currentUser.DisplayName,
            env, MembershipChangeService.PreviewSteps,
            async (sp, progress, jobCt) =>
            {
                var service = sp.GetRequiredService<IMembershipChangeService>();
                var preview = await service.PrepareAsync(type, read.Rows, read.Issues, progress, jobCt);
                return new MembershipPreviewViewModel
                {
                    Preview = preview,
                    Controller = controller,
                    UploadAction = uploadAction,
                    ExecuteAction = executeAction,
                    EnvironmentKey = env.Key,
                    FileName = fileName
                };
            });

        return c.RedirectToAction(jobAction, new { id = job.Id });
    }

    /// <summary>Önizlemedeki değişiklikleri (formdan değil, sunucudaki önizlemeden) uygular.</summary>
    public IActionResult Execute(Controller c, MembershipExecuteModel model, string uploadAction, string jobAction, string jobResultAction)
    {
        var previewJob = _jobs.Get(model.PreviewJobId, _currentUser.UserName);
        if (previewJob?.Result is not MembershipPreviewViewModel preview)
        {
            c.TempData["Warning"] = "Önizleme bulunamadı veya süresi doldu. Lütfen dosyayı yeniden yükleyin.";
            return c.RedirectToAction(uploadAction);
        }

        var current = _environment.Current.Key;
        if (!string.Equals(model.Environment, current, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(preview.EnvironmentKey, current, StringComparison.OrdinalIgnoreCase))
        {
            c.TempData["Warning"] = $"İşlem yapılmadı: önizleme '{preview.EnvironmentKey}' ortamında hazırlanmıştı, şu an aktif ortam '{current}'. Lütfen işlemi yeniden başlatın.";
            return c.RedirectToAction(uploadAction);
        }

        var type = preview.Preview.MemberType;
        var requests = preview.Preview.Processable
            .Select(i => new MembershipChangeRequest(i.Group.Id, i.Group.Name, i.MemberId, i.MemberName, i.Add))
            .ToList();

        if (requests.Count == 0 || model.ConfirmationCount != requests.Count)
        {
            c.TempData["Warning"] = $"Onay sayısı ({model.ConfirmationCount}) değişiklik sayısıyla ({requests.Count}) eşleşmiyor.";
            return c.RedirectToAction(jobResultAction, new { id = model.PreviewJobId });
        }

        var confirmation = model.ConfirmationCount;
        var controller = preview.Controller;

        var job = _jobs.Start($"{requests.Count} üyelik değişikliği uygulanıyor", uploadAction, _currentUser.UserName, _currentUser.DisplayName,
            _environment.Current, MembershipChangeService.ExecuteSteps,
            async (sp, progress, _) =>
            {
                var service = sp.GetRequiredService<IMembershipChangeService>();
                var results = await service.ExecuteAsync(type, requests, confirmation, progress);
                return new MembershipResultViewModel { Type = type, Controller = controller, UploadAction = uploadAction, Results = results };
            });

        return c.RedirectToAction(jobAction, new { id = job.Id });
    }
}
