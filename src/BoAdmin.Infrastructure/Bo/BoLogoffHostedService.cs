using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// Uygulama kapanırken (IIS app pool recycle/stop dahil) tüm ortamlardaki açık BO oturumlarını /logoff ile kapatır.
/// </summary>
public sealed class BoLogoffHostedService : IHostedService
{
    private static readonly TimeSpan LogoffTimeout = TimeSpan.FromSeconds(5);

    private readonly IBoSessionService _session;
    private readonly ILogger<BoLogoffHostedService> _logger;

    public BoLogoffHostedService(IBoSessionService session, ILogger<BoLogoffHostedService> logger)
    {
        _session = session;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LogoffTimeout);

        try
        {
            await _session.LogoffAllAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kapanışta BO logoff yapılamadı.");
        }
    }
}
