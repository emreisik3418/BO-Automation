using System.Collections.Concurrent;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;

namespace BoAdmin.Web.Infrastructure.Jobs;

public enum JobState
{
    Running,
    Completed,
    Failed
}

/// <summary>Ekrana gönderilen anlık durum (JSON).</summary>
public sealed record JobSnapshot(
    Guid Id,
    string Title,
    JobState State,
    int Step,
    int StepCount,
    string Phase,
    int Done,
    int Total,
    string? CurrentItem,
    int SuccessCount,
    int FailCount,
    double ElapsedSeconds,
    double? RemainingSeconds,
    int Percent,
    string? Error);

/// <summary>Arka planda çalışan bir işin durumu. Tüm erişimler kilit altında.</summary>
public sealed class BackgroundJob : IOperationProgress
{
    private readonly object _lock = new();

    private int _step;
    private string _phase = "Başlatılıyor";
    private int _done;
    private int _total;
    private string? _current;
    private int _success;
    private int _fail;
    private DateTime _phaseStartedUtc;

    public BackgroundJob(string title, string owner, EnvironmentInfo environment, int stepCount)
    {
        Title = title;
        Owner = owner;
        Environment = environment;
        StepCount = Math.Max(1, stepCount);
        StartedUtc = _phaseStartedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public string Owner { get; }
    public EnvironmentInfo Environment { get; }
    public int StepCount { get; }
    public DateTime StartedUtc { get; }
    public DateTime? FinishedUtc { get; private set; }
    public JobState State { get; private set; } = JobState.Running;
    public string? Error { get; private set; }

    /// <summary>İş bitince ekranda gösterilecek sonuç (view model).</summary>
    public object? Result { get; private set; }

    // ---------------- IOperationProgress ----------------

    public void Phase(string title, int total = 0)
    {
        lock (_lock)
        {
            _step = Math.Min(_step + 1, StepCount);
            _phase = title;
            _total = Math.Max(0, total);
            _done = 0;
            _current = null;
            _phaseStartedUtc = DateTime.UtcNow;
        }
    }

    public void Step(string? item = null, bool? success = null)
    {
        lock (_lock)
        {
            _done = _total > 0 ? Math.Min(_done + 1, _total) : _done + 1;
            if (success == true) _success++;
            else if (success == false) _fail++;
            if (item is not null) _current = item;
        }
    }

    public void Current(string? item)
    {
        lock (_lock) _current = item;
    }

    // ---------------- Sonuç ----------------

    internal void Complete(object? result)
    {
        lock (_lock)
        {
            Result = result;
            State = JobState.Completed;
            FinishedUtc = DateTime.UtcNow;
            _step = StepCount;
            _done = _total;
        }
    }

    internal void Fail(string error)
    {
        lock (_lock)
        {
            Error = error;
            State = JobState.Failed;
            FinishedUtc = DateTime.UtcNow;
        }
    }

    public JobSnapshot Snapshot()
    {
        lock (_lock)
        {
            var now = FinishedUtc ?? DateTime.UtcNow;
            var elapsed = (now - StartedUtc).TotalSeconds;

            // Kalan süre: bu adımdaki ortalama hızla (en az 2 iş bittikten sonra)
            double? remaining = null;
            if (State == JobState.Running && _total > 0 && _done >= 2 && _done < _total)
            {
                var phaseElapsed = (DateTime.UtcNow - _phaseStartedUtc).TotalSeconds;
                remaining = phaseElapsed / _done * (_total - _done);
            }

            // Genel yüzde: tamamlanan adımlar + içinde bulunulan adımın oranı
            var stepFraction = _total > 0 ? (double)_done / _total : 0;
            var percent = State == JobState.Completed
                ? 100
                : (int)Math.Floor((Math.Max(0, _step - 1) + stepFraction) / StepCount * 100);

            return new JobSnapshot(Id, Title, State, Math.Max(1, _step), StepCount, _phase, _done, _total,
                _current, _success, _fail, elapsed, remaining, Math.Clamp(percent, 0, 100), Error);
        }
    }
}

/// <summary>
/// Uzun işleri (toplu önizleme / toplu silme) arka planda çalıştırır; ekran durumunu saniyede bir sorar.
/// İşler bellekte tutulur: uygulama havuzu yeniden başlarsa yarım kalan işin durumu kaybolur
/// (o ana kadar yapılanlar audit loguna yazılmıştır). Bitmiş işler 2 saat sonra temizlenir.
/// </summary>
public sealed class JobService
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<Guid, BackgroundJob> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<JobService> _logger;

    public JobService(IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime, ILogger<JobService> logger)
    {
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>
    /// İşi başlatır ve hemen döner. <paramref name="work"/>, işi başlatan kullanıcı ve ortamla
    /// hazırlanmış yeni bir DI scope'unda çalışır.
    /// </summary>
    public BackgroundJob Start(
        string title, string owner, string? ownerDisplayName, EnvironmentInfo environment, int stepCount,
        Func<IServiceProvider, IOperationProgress, CancellationToken, Task<object?>> work)
    {
        Cleanup();

        var job = new BackgroundJob(title, owner, environment, stepCount);
        _jobs[job.Id] = job;

        // İstek bağlamı (HttpContext) arka plan işine taşınmasın
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => RunAsync(job, ownerDisplayName, work));
        }

        _logger.LogInformation("Arka plan işi başladı: {JobId} '{Title}' ({User}, {Environment})",
            job.Id, title, owner, environment.Key);
        return job;
    }

    /// <summary>İş yalnızca başlatan kullanıcıya gösterilir.</summary>
    public BackgroundJob? Get(Guid id, string? user)
    {
        Cleanup();
        return _jobs.TryGetValue(id, out var job) && string.Equals(job.Owner, user, StringComparison.OrdinalIgnoreCase)
            ? job
            : null;
    }

    private async Task RunAsync(
        BackgroundJob job, string? ownerDisplayName,
        Func<IServiceProvider, IOperationProgress, CancellationToken, Task<object?>> work)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<BackgroundJobContext>();
            context.UserName = job.Owner;
            context.DisplayName = ownerDisplayName;
            context.Environment = job.Environment;

            var result = await work(scope.ServiceProvider, job, _lifetime.ApplicationStopping);
            job.Complete(result);

            _logger.LogInformation("Arka plan işi bitti: {JobId} ({Seconds:N1} sn)", job.Id, job.Snapshot().ElapsedSeconds);
        }
        catch (AppValidationException ex)
        {
            job.Fail(ex.UserMessage);
        }
        catch (BoException ex)
        {
            _logger.LogWarning(ex, "Arka plan işi BO hatasıyla bitti: {JobId}", job.Id);
            job.Fail(ex.UserMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Arka plan işi beklenmeyen hatayla bitti: {JobId}", job.Id);
            job.Fail("Beklenmeyen bir hata oluştu: " + ex.Message);
        }
    }

    private void Cleanup()
    {
        var limit = DateTime.UtcNow - Retention;
        foreach (var (id, job) in _jobs)
        {
            if (job.FinishedUtc is { } finished && finished < limit)
                _jobs.TryRemove(id, out _);
        }
    }
}
