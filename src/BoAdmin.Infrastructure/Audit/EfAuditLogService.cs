using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Infrastructure.Audit;

/// <summary>
/// Audit kayıtlarını SQL Server'a yazar. Her kayıt ayrıca Serilog dosyasına da
/// "AUDIT" satırı olarak düşer; veritabanı yazılamazsa iz kaybolmaz.
/// </summary>
public sealed class EfAuditLogService : IAuditLogService
{
    private readonly IDbContextFactory<AuditDbContext> _dbFactory;
    private readonly ILogger<EfAuditLogService> _logger;

    public EfAuditLogService(IDbContextFactory<AuditDbContext> dbFactory, ILogger<EfAuditLogService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        // 1) Dosya logu (her durumda)
        _logger.LogInformation(
            "AUDIT {Module}/{Operation} Kullanıcı={User} Nesne={ObjectId} '{ObjectName}' Klasör={Folder} Hedef={Target} Sonuç={Success} Mesaj={Message} Batch={BatchId}",
            entry.Module, entry.Operation, entry.UserName, entry.ObjectId, entry.ObjectName,
            entry.FolderPath, entry.TargetPath, entry.Success ? "Başarılı" : "Başarısız", entry.Message, entry.BatchId);

        // 2) Veritabanı
        try
        {
            Truncate(entry);
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Asıl işlemin sonucunu bozmamak için exception yutulur; kayıt yukarıdaki dosya logunda var
            _logger.LogError(ex, "Audit kaydı veritabanına yazılamadı. Nesne: {ObjectId}, Batch: {BatchId}",
                entry.ObjectId, entry.BatchId);
        }
    }

    public async Task<PagedResult<AuditEntry>> QueryAsync(AuditQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 10, 200);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q.Module))
            query = query.Where(x => x.Module == q.Module);

        if (!string.IsNullOrWhiteSpace(q.Operation))
            query = query.Where(x => x.Operation == q.Operation);

        if (q.Success is not null)
            query = query.Where(x => x.Success == q.Success);

        if (!string.IsNullOrWhiteSpace(q.UserContains))
        {
            var u = q.UserContains.Trim();
            query = query.Where(x => x.UserName.Contains(u) || (x.DisplayName != null && x.DisplayName.Contains(u)));
        }

        if (!string.IsNullOrWhiteSpace(q.ObjectContains))
        {
            var o = q.ObjectContains.Trim();
            if (int.TryParse(o, out var objectId))
                query = query.Where(x => x.ObjectId == objectId || (x.ObjectName != null && x.ObjectName.Contains(o)));
            else
                query = query.Where(x => (x.ObjectName != null && x.ObjectName.Contains(o))
                                         || (x.FolderPath != null && x.FolderPath.Contains(o)));
        }

        // Ekrandaki tarihler yerel saat; tablo UTC
        if (q.From is not null)
        {
            var fromUtc = DateTime.SpecifyKind(q.From.Value.Date, DateTimeKind.Local).ToUniversalTime();
            query = query.Where(x => x.TimestampUtc >= fromUtc);
        }

        if (q.To is not null)
        {
            var toUtc = DateTime.SpecifyKind(q.To.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
            query = query.Where(x => x.TimestampUtc < toUtc);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.TimestampUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<AuditEntry>(items, page, pageSize, page * pageSize < total, total);
    }

    private static void Truncate(AuditEntry e)
    {
        e.UserName = Cut(e.UserName, 256) ?? string.Empty;
        e.DisplayName = Cut(e.DisplayName, 256);
        e.ObjectName = Cut(e.ObjectName, 512);
        e.ObjectKind = Cut(e.ObjectKind, 100);
        e.FolderPath = Cut(e.FolderPath, 2000);
        e.TargetPath = Cut(e.TargetPath, 2000);
        e.Message = Cut(e.Message, 4000);
    }

    private static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
