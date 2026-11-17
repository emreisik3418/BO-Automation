using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Infrastructure.Bo.Models;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// /biprws için typed HttpClient. Token ekleme ve 401 yenilemesi BoLogonTokenHandler'dadır.
/// Tüm hatalar BoException türlerine çevrilir; üst katmanlar HttpClient ayrıntısı görmez.
/// Yollar başında "/" olmadan verilir: "v1/cmsquery", "v1/infoobjects/123".
/// </summary>
public sealed class BoRestClient
{
    private const int MaxLoggedBodyLength = 4000;

    private readonly HttpClient _http;
    private readonly ILogger<BoRestClient> _logger;

    public BoRestClient(HttpClient http, ILogger<BoRestClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // CMS sorgusu
    // ------------------------------------------------------------------

    /// <summary>
    /// POST /v1/cmsquery?page={page}&amp;pagesize={pageSize}
    /// Yanıt biçimi (doğrulanmalı): { "entries": [ {...}, ... ], "next": {...} }
    /// </summary>
    public async Task<CmsQueryResult> CmsQueryAsync(string query, int page, int pageSize, CancellationToken ct = default)
    {
        _logger.LogDebug("CMS sorgusu (sayfa {Page}, {Size}): {Query}", page, pageSize, query);

        var json = await SendForJsonAsync(
            HttpMethod.Post,
            $"v1/cmsquery?page={page}&pagesize={pageSize}",
            new CmsQueryRequest(query),
            ct);

        var entries = new List<CmsEntry>();
        var hasMore = false;
        var nextFieldFound = false;

        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in json.EnumerateObject())
            {
                if (string.Equals(p.Name, "entries", StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.Array)
                {
                    entries.AddRange(p.Value.EnumerateArray().Select(e => new CmsEntry(e)));
                }
                else if (string.Equals(p.Name, "next", StringComparison.OrdinalIgnoreCase))
                {
                    nextFieldFound = true;
                    hasMore = p.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
                }
            }
        }

        // "next" alanı hiç yoksa: tam sayfa geldiyse devamı olabilir
        if (!nextFieldFound && entries.Count >= pageSize)
            hasMore = true;

        return new CmsQueryResult(entries, hasMore);
    }

    /// <summary>Sorgunun tüm sayfalarını çeker (klasör listesi gibi küçük kümeler için).</summary>
    public async Task<IReadOnlyList<CmsEntry>> CmsQueryAllAsync(string query, int pageSize = 500, int maxPages = 200, CancellationToken ct = default)
    {
        var all = new List<CmsEntry>();
        for (var page = 1; page <= maxPages; page++)
        {
            var result = await CmsQueryAsync(query, page, pageSize, ct);
            all.AddRange(result.Entries);
            if (!result.HasMore || result.Entries.Count == 0) break;
        }
        return all;
    }

    // ------------------------------------------------------------------
    // Genel HTTP metotları
    // ------------------------------------------------------------------

    public Task<JsonElement> GetJsonAsync(string path, CancellationToken ct = default) =>
        SendForJsonAsync(HttpMethod.Get, path, null, ct);

    public Task<JsonElement> PostJsonAsync(string path, object body, CancellationToken ct = default) =>
        SendForJsonAsync(HttpMethod.Post, path, body, ct);

    public Task<JsonElement> PutJsonAsync(string path, object body, CancellationToken ct = default) =>
        SendForJsonAsync(HttpMethod.Put, path, body, ct);

    public async Task DeleteAsync(string path, CancellationToken ct = default) =>
        await SendForJsonAsync(HttpMethod.Delete, path, null, ct);

    // ------------------------------------------------------------------

    private async Task<JsonElement> SendForJsonAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BoUnavailableException($"BO isteği gönderilemedi: {method} {path}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BoUnavailableException($"BO isteği zaman aşımına uğradı: {method} {path}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                await ThrowForErrorAsync(method, path, response, ct);

            var text = await response.Content.ReadAsStringAsync(ct);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("BO yanıtı {Method} {Path} -> {Status}: {Body}",
                    method, path, (int)response.StatusCode,
                    text.Length > MaxLoggedBodyLength ? text[..MaxLoggedBodyLength] + "..." : text);
            }

            if (string.IsNullOrWhiteSpace(text))
                return default; // ValueKind == Undefined (ör. DELETE yanıtı boş olabilir)

            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new BoOperationException("BO beklenmeyen (JSON olmayan) bir yanıt döndürdü.", null,
                    (int)response.StatusCode, ex);
            }
        }
    }

    private async Task ThrowForErrorAsync(HttpMethod method, string path, HttpResponseMessage response, CancellationToken ct)
    {
        var (code, message) = await BoErrorParser.ReadAsync(response, ct);
        var status = (int)response.StatusCode;

        _logger.LogWarning("BO hatası {Method} {Path} -> HTTP {Status}, Kod: {Code}, Mesaj: {Message}",
            method, path, status, code, message);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => (BoException)new BoAuthenticationException(message, code, status),
            HttpStatusCode.Forbidden => new BoAccessDeniedException(message, code),
            HttpStatusCode.NotFound => new BoNotFoundException(message, code),
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                => new BoUnavailableException($"BO HTTP {status}: {message}"),
            _ => new BoOperationException(message, code, status)
        };
    }
}
