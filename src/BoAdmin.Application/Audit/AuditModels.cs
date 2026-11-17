using BoAdmin.Application.Common;

namespace BoAdmin.Application.Audit;

/// <summary>Denetim kaydı (AuditLog tablosunun bir satırı).</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    /// <summary>UTC</summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>DOMAIN\sicil</summary>
    public string UserName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>Reports, Users, Groups</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Delete, Archive, DeleteInstance ...</summary>
    public string Operation { get; set; } = string.Empty;

    public int? ObjectId { get; set; }
    public string? ObjectName { get; set; }
    public string? ObjectKind { get; set; }
    public string? FolderPath { get; set; }

    /// <summary>Taşıma işlemlerinde hedef klasör yolu</summary>
    public string? TargetPath { get; set; }

    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>Silinen instance sayısı</summary>
    public int? InstanceCount { get; set; }

    /// <summary>Aynı toplu işlemdeki kayıtları gruplar</summary>
    public Guid? BatchId { get; set; }
}

/// <summary>İşlem Geçmişi filtreleri.</summary>
public sealed class AuditQuery
{
    public string? Module { get; set; }
    public string? UserContains { get; set; }
    public string? ObjectContains { get; set; }
    public string? Operation { get; set; }
    public bool? Success { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Denetim kaydı servisi. WriteAsync asla exception fırlatmamalı:
/// audit yazılamazsa asıl işlem sonucu kaybolmasın (hata loglanır).
/// Implementasyon: Infrastructure/Audit (Parça 5).
/// </summary>
public interface IAuditLogService
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);

    Task<PagedResult<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}
