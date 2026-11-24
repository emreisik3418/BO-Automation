using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Infrastructure.Bo;
using BoAdmin.Infrastructure.Bo.Credentials;
using BoAdmin.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BoAdmin.Web.Controllers;

/// <summary>
/// Bağlantı testi: /Diagnostics/Sbox
/// Her ortamın kimlik bilgisini nereden aldığını gösterir; "Test et" Sbox'tan bilgiyi yeniden alır
/// ve o bilgiyle BO'ya yeni oturum açar. Şifre hiçbir zaman ekrana yazılmaz.
/// </summary>
[Authorize(Policy = ModuleNames.AnyModule)]
public sealed class DiagnosticsController : Controller
{
    private readonly IOptionsMonitor<BoOptions> _options;
    private readonly IBoCredentialProvider _credentials;
    private readonly IBoSessionService _sessions;
    private readonly IModuleAccessService _access;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(IOptionsMonitor<BoOptions> options, IBoCredentialProvider credentials,
        IBoSessionService sessions, IModuleAccessService access, ILogger<DiagnosticsController> logger)
    {
        _options = options;
        _credentials = credentials;
        _sessions = sessions;
        _access = access;
        _logger = logger;
    }

    public sealed record EnvRow(string Key, string DisplayName, bool UsesSbox, string? Target);

    public sealed record TestResult(string Environment, bool Success, string Step, string Message, string? UserName, string? Source);

    [HttpGet]
    public IActionResult Sbox()
    {
        ViewData["Result"] = TempData["SboxResult"] is string json
            ? System.Text.Json.JsonSerializer.Deserialize<TestResult>(json)
            : null;
        return View(Rows());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SboxTest(string environment, CancellationToken ct)
    {
        if (!Rows().Any(r => string.Equals(r.Key, environment, StringComparison.OrdinalIgnoreCase)))
            return Forbid();

        TestResult result;
        string? userName = null, source = null;
        try
        {
            // 1) Kimlik bilgisi (Sbox'tan bellek kullanılmadan)
            var credential = await _credentials.GetFreshAsync(environment, ct);
            userName = credential.UserName;
            source = credential.FromSbox ? "Sbox" : "appsettings / User Secrets";

            // 2) Bu bilgiyle yeni BO oturumu
            await _sessions.ResetAsync(environment, ct);
            await _sessions.GetTokenAsync(environment, ct);

            result = new TestResult(environment, true, "Tamam",
                $"Kimlik bilgisi alındı ({source}) ve BO oturumu açıldı. Şifre uzunluğu: {credential.Password.Length} karakter.",
                userName, source);
        }
        catch (BoException ex)
        {
            _logger.LogWarning(ex, "Sbox/BO bağlantı testi başarısız: {Environment}", environment);
            result = new TestResult(environment, false, userName is null ? "Kimlik bilgisi alınamadı" : "BO oturumu açılamadı",
                ex.UserMessage, userName, source);
        }

        TempData["SboxResult"] = System.Text.Json.JsonSerializer.Serialize(result);
        return RedirectToAction(nameof(Sbox));
    }

    private List<EnvRow> Rows()
    {
        var accessible = _access.GetAccessibleEnvironments(User).Select(e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _options.CurrentValue.Ordered
            .Where(kv => accessible.Contains(kv.Key))
            .Select(kv => new EnvRow(kv.Key, string.IsNullOrWhiteSpace(kv.Value.DisplayName) ? kv.Key : kv.Value.DisplayName,
                kv.Value.UsesSbox, kv.Value.UsesSbox ? kv.Value.Sbox!.ToString() : null))
            .ToList();
    }
}
