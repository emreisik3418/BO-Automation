using System.Collections.Concurrent;
using BoAdmin.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoAdmin.Infrastructure.Bo.Credentials;

public interface IBoCredentialProvider
{
    /// <summary>Ortamın BO servis kullanıcısı. Sbox'tan gelen bilgi CacheMinutes boyunca bellekte tutulur.</summary>
    Task<BoCredential> GetAsync(string environment, CancellationToken ct = default);

    /// <summary>Bellekteki bilgiyi düşürür (BO "şifre hatalı" dediğinde: Sbox'ta şifre değişmiş olabilir).</summary>
    void Invalidate(string environment);

    /// <summary>Bilinen BO kullanıcı adı (Sbox'tan daha önce alınmışsa o, yoksa appsettings).</summary>
    string? KnownUserName(string environment);
}

public sealed class BoCredentialProvider : IBoCredentialProvider
{
    private sealed record CacheItem(BoCredential Credential, DateTimeOffset ExpiresAt);

    private readonly IOptionsMonitor<BoOptions> _options;
    private readonly ISboxClient _sbox;
    private readonly ILogger<BoCredentialProvider> _logger;
    private readonly ConcurrentDictionary<string, CacheItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public BoCredentialProvider(IOptionsMonitor<BoOptions> options, ISboxClient sbox, ILogger<BoCredentialProvider> logger)
    {
        _options = options;
        _sbox = sbox;
        _logger = logger;
    }

    public async Task<BoCredential> GetAsync(string environment, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        var env = options.Find(environment)
                  ?? throw new BoAuthenticationException($"'{environment}' ortamı yapılandırmada tanımlı değil.");

        if (!env.UsesSbox)
        {
            if (string.IsNullOrWhiteSpace(env.UserName))
                throw new BoAuthenticationException($"'{environment}' ortamı için UserName tanımlı değil.");
            if (string.IsNullOrWhiteSpace(env.Password))
                throw new BoAuthenticationException(
                    $"'{environment}' ortamı için şifre tanımlı değil (SapBo:Environments:{environment}:Password).");
            return new BoCredential(env.UserName, env.Password, "Config");
        }

        if (_cache.TryGetValue(environment, out var hit) && hit.ExpiresAt > DateTimeOffset.UtcNow)
            return hit.Credential;

        var gate = _locks.GetOrAdd(environment, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(environment, out hit) && hit.ExpiresAt > DateTimeOffset.UtcNow)
                return hit.Credential;

            var credential = await _sbox.GetAsync(options.Sbox, env.Sbox!, ct);
            var minutes = Math.Max(1, options.Sbox.CacheMinutes);
            _cache[environment] = new CacheItem(credential, DateTimeOffset.UtcNow.AddMinutes(minutes));

            _logger.LogInformation("BO kimlik bilgisi Sbox'tan alındı. Ortam: {Environment}, kullanıcı: {User}", environment, credential.UserName);
            return credential;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(string environment)
    {
        if (_cache.TryRemove(environment, out _))
            _logger.LogInformation("Sbox kimlik bilgisi bellekten silindi, bir sonraki logon'da yeniden alınacak. Ortam: {Environment}", environment);
    }

    public string? KnownUserName(string environment)
    {
        if (_cache.TryGetValue(environment, out var hit)) return hit.Credential.UserName;
        var env = _options.CurrentValue.Find(environment);
        return string.IsNullOrWhiteSpace(env?.UserName) ? null : env.UserName;
    }
}
