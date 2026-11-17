using System.Net;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// BoRestClient'ın her isteğine X-SAP-LogonToken ekler.
/// 401 gelirse token'ı düşürür, yeniden logon olur ve isteği BİR KEZ tekrarlar.
/// </summary>
public sealed class BoLogonTokenHandler : DelegatingHandler
{
    private readonly IBoSessionService _session;
    private readonly ILogger<BoLogonTokenHandler> _logger;

    public BoLogonTokenHandler(IBoSessionService session, ILogger<BoLogonTokenHandler> logger)
    {
        _session = session;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _session.GetTokenAsync(ct);
        SetToken(request, token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        _logger.LogInformation("BO 401 döndü ({Method} {Uri}); oturum yenilenip istek tekrarlanıyor.",
            request.Method, request.RequestUri);

        response.Dispose();
        _session.Invalidate(token);

        token = await _session.GetTokenAsync(ct);
        SetToken(request, token);

        // Gövde StringContent/JsonContent olduğu için yeniden gönderilebilir
        return await base.SendAsync(request, ct);
    }

    private static void SetToken(HttpRequestMessage request, string token)
    {
        request.Headers.Remove(BoSessionService.TokenHeader);
        request.Headers.TryAddWithoutValidation(BoSessionService.TokenHeader, token);
    }
}
