namespace BoAdmin.Web.Infrastructure.Jobs;

/// <summary>İlerleme ekranının saniyede bir sorduğu durum (tüm modüllerde aynı biçim).</summary>
public static class JobStatusJson
{
    public static object From(BackgroundJob job)
    {
        var s = job.Snapshot();
        return new
        {
            state = s.State.ToString().ToLowerInvariant(),
            title = s.Title,
            step = s.Step,
            stepCount = s.StepCount,
            phase = s.Phase,
            done = s.Done,
            total = s.Total,
            currentItem = s.CurrentItem,
            success = s.SuccessCount,
            fail = s.FailCount,
            elapsed = Math.Round(s.ElapsedSeconds),
            remaining = s.RemainingSeconds is null ? (double?)null : Math.Ceiling(s.RemainingSeconds.Value),
            percent = s.Percent,
            error = s.Error
        };
    }
}
