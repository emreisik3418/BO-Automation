using BoAdmin.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace BoAdmin.Infrastructure.Audit;

/// <summary>Denetim kayıtları veritabanı (yalnızca dbo.AuditLog tablosu).</summary>
public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditEntry> AuditLogs => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AuditEntry>();

        e.ToTable("AuditLog", "dbo");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).UseIdentityColumn();

        e.Property(x => x.TimestampUtc).HasColumnType("datetime2(3)").IsRequired();
        e.Property(x => x.UserName).HasMaxLength(256).IsRequired();
        e.Property(x => x.DisplayName).HasMaxLength(256);
        e.Property(x => x.Module).HasMaxLength(50).IsRequired();
        e.Property(x => x.Operation).HasMaxLength(50).IsRequired();
        e.Property(x => x.ObjectName).HasMaxLength(512);
        e.Property(x => x.ObjectKind).HasMaxLength(100);
        e.Property(x => x.FolderPath).HasMaxLength(2000);
        e.Property(x => x.TargetPath).HasMaxLength(2000);
        e.Property(x => x.Message).HasMaxLength(4000);

        e.HasIndex(x => x.TimestampUtc).HasDatabaseName("IX_AuditLog_TimestampUtc");
        e.HasIndex(x => x.UserName).HasDatabaseName("IX_AuditLog_UserName");
        e.HasIndex(x => x.ObjectId).HasDatabaseName("IX_AuditLog_ObjectId");
        e.HasIndex(x => x.BatchId).HasDatabaseName("IX_AuditLog_BatchId");
    }
}
