using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Principals;
using BoAdmin.Web.Authorization;
using BoAdmin.Web.Infrastructure;
using BoAdmin.Web.Modules.Users.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoAdmin.Web.Modules.Users;

/// <summary>
/// Kullanıcı Yönetimi.
///  GET /Users             -> kullanıcı arama (ilk açılışta BO'ya istek atılmaz)
///  GET /Users/Export      -> arama sonucunun tamamı Excel
///  GET /Users/Detail/{id} -> kullanıcı bilgileri + üye olduğu gruplar
/// </summary>
[Authorize(Policy = ModuleNames.Users)]
public sealed class UsersController : Controller
{
    private readonly IPrincipalService _principals;
    private readonly CurrentEnvironmentAccessor _environment;
    private readonly IModuleAccessService _access;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IPrincipalService principals,
        CurrentEnvironmentAccessor environment,
        IModuleAccessService access,
        ILogger<UsersController> logger)
    {
        _principals = principals;
        _environment = environment;
        _access = access;
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
}
