using System.Net.Http.Json;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Infrastructure.Bo.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// BO servis hesabı oturumunu yönetir:
///  - POST /logon/long ile token alır ve önbellekte tutar
///  - Süre dolunca (TokenLifetimeMinutes) otomatik yeniler, eski oturumu /logoff ile kapatır
///  - 401 alınınca Invalidate ile token düşürülür, bir sonraki istekte yeniden logon olunur
///  - Uygulama kapanırken LogoffAsync çağrılır (BoLogoffHostedService)
/// </summary>
public interface IBoSessionService
{
    Task<string> GetTokenAsync(CancellationToken ct = default);

    /// <summary>Verilen token hâlâ geçerli token ise düşürür (eşzamanlı isteklerde çift logon'u önler).</summary>
    void Invalidate(string token);

    Task LogoffAsync(CancellationToken ct = default);
}

public sealed class BoSessionService : IBoSessionService, IDisposable
{
    public const string HttpClientName = "BoAuth";
    public const string TokenHeader = "X-SAP-LogonToken";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BoOptions> _options;
    private readonly ILogger<BoSessionService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private sealed record SessionState(string Token, DateTimeOffset ExpiresAt);
    private SessionState? _state; // Volatile/Interlocked ile erişilir

    public BoSessionService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BoOptions> options,
        ILogger<BoSessionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        var state = Volatile.Read(ref _state);
        if (state is not null && DateTimeOffset.UtcNow < state.ExpiresAt)
            return state.Token;

        await _lock.WaitAsync(ct);
        try
        {
            // Kilit beklerken başka bir istek yenilemiş olabilir
            state = Volatile.Read(ref _state);
            if (state is not null && DateTimeOffset.UtcNow < state.ExpiresAt)
                return state.Token;

            var oldToken = state?.Token;

            var token = await LogonAsync(ct);
            var lifetime = TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.TokenLifetimeMinutes));
            Volatile.Write(ref _state, new SessionState(token, DateTimeOffset.UtcNow.Add(lifetime)));

            _logger.LogInformation("BO oturumu açıldı. Geçerlilik: {Minutes} dk.", lifetime.TotalMinutes);

            // Süresi dolan (ama hâlâ açık olabilecek) eski oturumu arka planda kapat.
            // BO'da açık kalan oturumlar eşzamanlı lisans tüketir.
            if (oldToken is not null)
                _ = LogoffTokenSafeAsync(oldToken, CancellationToken.None);

            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate(string token)
    {
        var state = Volatile.Read(ref _state);
        if (state is null || state.Token != token) return;

        // Yalnızca hâlâ aynı token duruyorsa düşür (başka istek yenilediyse dokunma)
        if (ReferenceEquals(Interlocked.CompareExchange(ref _state, null, state), state))
            _logger.LogWarning("BO token'ı geçersiz sayıldı (401). Bir sonraki istekte yeniden oturum açılacak.");
    }

    public async Task LogoffAsync(CancellationToken ct = default)
    {
        var state = Interlocked.Exchange(ref _state, null);
        if (state is null) return;

        await LogoffTokenSafeAsync(state.Token, ct);
    }

    private async Task<string> LogonAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var request = new BoLogonRequest(options.UserName, options.Password, options.AuthType);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("logon/long", request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BoUnavailableException($"BO logon isteği gönderilemedi: {client.BaseAddress}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BoUnavailableException($"BO logon isteği zaman aşımına uğradı: {client.BaseAddress}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = await BoErrorParser.ReadAsync(response, ct);
                _logger.LogError("BO logon başarısız. HTTP {Status}, Kod: {Code}, Mesaj: {Message}",
                    (int)response.StatusCode, code, message);
                throw new BoAuthenticationException($"BO logon başarısız: {message}", code, (int)response.StatusCode);
            }

            // Öncelik header'da; yoksa gövdedeki logonToken (doğrulanmalı)
            string? token = null;
            if (response.Headers.TryGetValues(TokenHeader, out var values))
                token = values.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(token))
            {
                var body = await response.Content.ReadFromJsonAsync<BoLogonResponse>(cancellationToken: ct);
                token = body?.LogonToken;
            }

            if (string.IsNullOrWhiteSpace(token))
                throw new BoAuthenticationException("BO logon yanıtında token bulunamadı.");

            return NormalizeToken(token);
        }
    }

    private async Task LogoffTokenSafeAsync(string token, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "logoff");
            request.Headers.TryAddWithoutValidation(TokenHeader, token);

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                _logger.LogInformation("BO oturumu kapatıldı.");
            else
                _logger.LogWarning("BO logoff başarısız. HTTP {Status}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Logoff hatası kritik değil; oturum BO tarafında zaman aşımıyla kapanır
            _logger.LogWarning(ex, "BO logoff sırasında hata.");
        }
    }

    /// <summary>
    /// BO token'ı header'da çift tırnak içinde bekler: X-SAP-LogonToken: "COMMANDCOM:6400@{...}"
    /// Gövdeden gelen token tırnaksızsa tırnak eklenir (doğrulanmalı).
    /// </summary>
    private static string NormalizeToken(string token)
    {
        token = token.Trim();
        return token.StartsWith('"') && token.EndsWith('"') ? token : $"\"{token}\"";
    }

    public void Dispose() => _lock.Dispose();
}
