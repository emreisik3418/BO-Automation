using BoAdmin.Infrastructure.Bo.Credentials;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Infrastructure.Bo.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// Her BO ortamı için ayrı servis hesabı oturumu yönetir:
///  - POST /logon/long ile token alır ve ortam bazında önbellekte tutar
///  - Süre dolunca (TokenLifetimeMinutes) otomatik yeniler, eski oturumu /logoff ile kapatır
///  - 401 alınınca Invalidate ile token düşürülür, bir sonraki istekte yeniden logon olunur
///  - Uygulama kapanırken tüm ortamlar için /logoff (BoLogoffHostedService)
/// </summary>
public interface IBoSessionService
{
    Task<string> GetTokenAsync(string environment, CancellationToken ct = default);

    /// <summary>Verilen token hâlâ geçerli token ise düşürür (eşzamanlı isteklerde çift logon'u önler).</summary>
    void Invalidate(string environment, string token);

    Task LogoffAllAsync(CancellationToken ct = default);
}

public sealed class BoSessionService : IBoSessionService, IDisposable
{
    public const string HttpClientName = "BoAuth";
    public const string TokenHeader = "X-SAP-LogonToken";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<BoOptions> _options;
    private readonly IBoCredentialProvider _credentials;
    private readonly ILogger<BoSessionService> _logger;

    private sealed class SessionSlot
    {
        public readonly SemaphoreSlim Lock = new(1, 1);
        public SessionState? State; // Volatile/Interlocked ile erişilir
    }

    private sealed record SessionState(string Token, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, SessionSlot> _slots = new(StringComparer.OrdinalIgnoreCase);

    public BoSessionService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<BoOptions> options,
        IBoCredentialProvider credentials,
        ILogger<BoSessionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _credentials = credentials;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(string environment, CancellationToken ct = default)
    {
        var slot = _slots.GetOrAdd(environment, _ => new SessionSlot());

        var state = Volatile.Read(ref slot.State);
        if (state is not null && DateTimeOffset.UtcNow < state.ExpiresAt)
            return state.Token;

        await slot.Lock.WaitAsync(ct);
        try
        {
            // Kilit beklerken başka bir istek yenilemiş olabilir
            state = Volatile.Read(ref slot.State);
            if (state is not null && DateTimeOffset.UtcNow < state.ExpiresAt)
                return state.Token;

            var oldToken = state?.Token;

            var token = await LogonAsync(environment, ct);
            var lifetime = TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.TokenLifetimeMinutes));
            Volatile.Write(ref slot.State, new SessionState(token, DateTimeOffset.UtcNow.Add(lifetime)));

            _logger.LogInformation("BO oturumu açıldı. Ortam: {Environment}, geçerlilik: {Minutes} dk.",
                environment, lifetime.TotalMinutes);

            // Süresi dolan (ama hâlâ açık olabilecek) eski oturumu arka planda kapat.
            // BO'da açık kalan oturumlar eşzamanlı lisans tüketir.
            if (oldToken is not null)
                _ = LogoffTokenSafeAsync(environment, oldToken, CancellationToken.None);

            return token;
        }
        finally
        {
            slot.Lock.Release();
        }
    }

    public void Invalidate(string environment, string token)
    {
        if (!_slots.TryGetValue(environment, out var slot)) return;

        var state = Volatile.Read(ref slot.State);
        if (state is null || state.Token != token) return;

        // Yalnızca hâlâ aynı token duruyorsa düşür (başka istek yenilediyse dokunma)
        if (ReferenceEquals(Interlocked.CompareExchange(ref slot.State, null, state), state))
            _logger.LogWarning("BO token'ı geçersiz sayıldı (401). Ortam: {Environment}. Bir sonraki istekte yeniden oturum açılacak.", environment);
    }

    public async Task LogoffAllAsync(CancellationToken ct = default)
    {
        foreach (var (environment, slot) in _slots)
        {
            var state = Interlocked.Exchange(ref slot.State, null);
            if (state is not null)
                await LogoffTokenSafeAsync(environment, state.Token, ct);
        }
    }

    // ------------------------------------------------------------------

    private BoEnvironmentOptions GetEnvironment(string environment) =>
        _options.CurrentValue.Find(environment)
        ?? throw new BoAuthenticationException($"'{environment}' ortamı yapılandırmada tanımlı değil.");

    private async Task<string> LogonAsync(string environment, CancellationToken ct)
    {
        var credential = await _credentials.GetAsync(environment, ct);
        try
        {
            return await LogonWithAsync(environment, credential, ct);
        }
        catch (BoAuthenticationException) when (credential.FromSbox)
        {
            // Sbox'taki şifre değişmiş olabilir: bellekteki bilgiyi atıp Sbox'tan yeniden alır, BİR KEZ daha dener.
            _credentials.Invalidate(environment);
            var fresh = await _credentials.GetAsync(environment, ct);
            _logger.LogWarning("BO logon Sbox bilgisiyle başarısız oldu; Sbox'tan yeniden alınan bilgiyle tekrar deneniyor. Ortam: {Environment}", environment);
            return await LogonWithAsync(environment, fresh, ct);
        }
    }

    private async Task<string> LogonWithAsync(string environment, BoCredential credential, CancellationToken ct)
    {
        var env = GetEnvironment(environment);

        if (string.IsNullOrWhiteSpace(env.BaseUrl))
            throw new BoAuthenticationException($"'{environment}' ortamı için BaseUrl tanımlı değil.");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = BoUrl.Combine(env.BaseUrl, "logon/long");
        var request = new BoLogonRequest(credential.UserName, credential.Password, env.AuthType);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(url, request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BoUnavailableException($"BO logon isteği gönderilemedi ({environment}): {url}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BoUnavailableException($"BO logon isteği zaman aşımına uğradı ({environment}): {url}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = await BoErrorParser.ReadAsync(response, ct);
                _logger.LogError("BO logon başarısız. Ortam: {Environment}, HTTP {Status}, Kod: {Code}, Mesaj: {Message}",
                    environment, (int)response.StatusCode, code, message);
                throw new BoAuthenticationException($"BO logon başarısız: {message}", code, (int)response.StatusCode);
            }

            // Öncelik header'da; yoksa gövdedeki logonToken
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

    private async Task LogoffTokenSafeAsync(string environment, string token, CancellationToken ct)
    {
        try
        {
            var env = GetEnvironment(environment);
            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Post, BoUrl.Combine(env.BaseUrl, "logoff"));
            request.Headers.TryAddWithoutValidation(TokenHeader, token);

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                _logger.LogInformation("BO oturumu kapatıldı. Ortam: {Environment}", environment);
            else
                _logger.LogWarning("BO logoff başarısız. Ortam: {Environment}, HTTP {Status}", environment, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Logoff hatası kritik değil; oturum BO tarafında zaman aşımıyla kapanır
            _logger.LogWarning(ex, "BO logoff sırasında hata. Ortam: {Environment}", environment);
        }
    }

    /// <summary>
    /// BO token'ı header'da çift tırnak içinde bekler: X-SAP-LogonToken: "COMMANDCOM:6400@{...}"
    /// Gövdeden gelen token tırnaksızsa tırnak eklenir.
    /// </summary>
    private static string NormalizeToken(string token)
    {
        token = token.Trim();
        return token.StartsWith('"') && token.EndsWith('"') ? token : $"\"{token}\"";
    }

    public void Dispose()
    {
        foreach (var slot in _slots.Values)
            slot.Lock.Dispose();
    }
}

/// <summary>URL yardımcıları.</summary>
internal static class BoUrl
{
    /// <summary>"http://x:6405/biprws" + "v1/cmsquery" -> "http://x:6405/biprws/v1/cmsquery"</summary>
    public static Uri Combine(string baseUrl, string relativePath) =>
        new(new Uri(baseUrl.TrimEnd('/') + "/"), relativePath.TrimStart('/'));
}
