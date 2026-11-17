using System.Net;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// BoRestClient'ın her isteğine, isteğin ait olduğu ortamın X-SAP-LogonToken'ını ekler.
/// Ortam, BoRestClient tarafından request.Options içine yazılır.
/// 401 gelirse token'ı düşürür, yeniden logon olur ve isteği BİR KEZ tekrarlar.
/// </summary>
public sealed class BoLogonTokenHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<string> EnvironmentKey = new("BoEnvironment");

    private readonly IBoSessionService _session;
    private readonly ILogger<BoLogonTokenHandler> _logger;

    public BoLogonTokenHandler(IBoSessionService session, ILogger<BoLogonTokenHandler> logger)
    {
        _session = session;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!request.Options.TryGetValue(EnvironmentKey, out var environment) || string.IsNullOrEmpty(environment))
            throw new InvalidOperationException("BO isteğinde ortam bilgisi yok.");

        var token = await _session.GetTokenAsync(environment, ct);
        SetToken(request, token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        _logger.LogInformation("BO 401 döndü ({Environment} {Method} {Uri}); oturum yenilenip istek tekrarlanıyor.",
            environment, request.Method, request.RequestUri);

        response.Dispose();
        _session.Invalidate(environment, token);

        token = await _session.GetTokenAsync(environment, ct);
        SetToken(request, token);

        // Gövde JsonContent olduğu için yeniden gönderilebilir
        return await base.SendAsync(request, ct);
    }

    private static void SetToken(HttpRequestMessage request, string token)
    {
        request.Headers.Remove(BoSessionService.TokenHeader);
        request.Headers.TryAddWithoutValidation(BoSessionService.TokenHeader, token);
    }
}
