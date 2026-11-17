using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoAdmin.Infrastructure.Audit;

/// <summary>appsettings.json > "Audit" bölümü.</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>
    /// Audit dosyalarının klasörü. Göreli yol verilirse uygulama klasörüne göre çözülür.
    /// IIS uygulama havuzu kimliğinin bu klasörde "Değiştirme (Modify)" yetkisi olmalı.
    /// </summary>
    public string Directory { get; set; } = "logs/audit";
}

/// <summary>
/// Denetim kayıtlarını aylık JSON Lines dosyalarına yazar:
///   {Directory}\audit-2026-11.jsonl  (her satır bir kayıt)
/// Dosyalar Notepad/VS Code ile okunabilir; İşlem Geçmişi ekranı da bu dosyaları okur.
/// Kayıtlar ayrıca Serilog dosyasına "AUDIT ..." satırı olarak da düşer.
/// </summary>
public sealed class FileAuditLogService : IAuditLogService
{
    private const string FilePrefix = "audit-";
    private const string FileExtension = ".jsonl";
    private const int MaxWriteAttempts = 5;

    // Aynı süreç içindeki eşzamanlı yazmaları sıraya koyar
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        // Türkçe karakterler dosyada ş yerine ş olarak görünsün
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly ILogger<FileAuditLogService> _logger;

    public FileAuditLogService(IOptions<AuditOptions> options, IHostEnvironment environment, ILogger<FileAuditLogService> logger)
    {
        var dir = string.IsNullOrWhiteSpace(options.Value.Directory) ? "logs/audit" : options.Value.Directory;
        _directory = Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(environment.ContentRootPath, dir));
        _logger = logger;
    }

    public string AuditDirectory => _directory;

    // ------------------------------------------------------------------
    // Yazma
    // ------------------------------------------------------------------

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        // 1) Uygulama loguna da yaz (yedek iz)
        _logger.LogInformation(
            "AUDIT {Module}/{Operation} Kullanıcı={User} Nesne={ObjectId} '{ObjectName}' Klasör={Folder} Hedef={Target} Sonuç={Success} Mesaj={Message} Batch={BatchId}",
            entry.Module, entry.Operation, entry.UserName, entry.ObjectId, entry.ObjectName,
            entry.FolderPath, entry.TargetPath, entry.Success ? "Başarılı" : "Başarısız", entry.Message, entry.BatchId);

        // 2) Audit dosyası
        try
        {
            var bytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(entry, JsonOptions) + "\r\n");
            var path = GetFilePath(entry.TimestampUtc.ToLocalTime());

            // Audit yazımı yarıda kesilmesin diye iptal token'ı kullanılmaz
            await WriteLock.WaitAsync(CancellationToken.None);
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                await AppendWithRetryAsync(path, bytes);
            }
            finally
            {
                WriteLock.Release();
            }
        }
        catch (Exception ex)
        {
            // Asıl işlemin sonucunu bozmamak için exception yutulur; kayıt yukarıdaki uygulama logunda var
            _logger.LogError(ex, "Audit dosyasına yazılamadı. Klasör: {Directory}, Nesne: {ObjectId}, Batch: {BatchId}",
                _directory, entry.ObjectId, entry.BatchId);
        }
    }

    private static async Task AppendWithRetryAsync(string path, byte[] bytes)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // FileShare.ReadWrite: app pool recycle sırasında iki süreç aynı anda açabilir
                await using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                await fs.WriteAsync(bytes);
                await fs.FlushAsync();
                return;
            }
            catch (IOException) when (attempt < MaxWriteAttempts)
            {
                await Task.Delay(100 * attempt);
            }
        }
    }

    /// <summary>
    /// Başlangıçta klasörün yazılabilir olduğunu kontrol eder. Yazılamıyorsa exception fırlatır.
    /// </summary>
    public void EnsureWritable()
    {
        System.IO.Directory.CreateDirectory(_directory);
        var probe = Path.Combine(_directory, $".write-test-{Environment.ProcessId}.tmp");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    // ------------------------------------------------------------------
    // Okuma (İşlem Geçmişi)
    // ------------------------------------------------------------------

    public async Task<PagedResult<AuditEntry>> QueryAsync(AuditQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 10, 200);

        // Ekrandaki tarihler yerel saat; kayıtlar UTC
        DateTime? fromUtc = q.From is null ? null
            : DateTime.SpecifyKind(q.From.Value.Date, DateTimeKind.Local).ToUniversalTime();
        DateTime? toUtc = q.To is null ? null
            : DateTime.SpecifyKind(q.To.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

        var entries = new List<AuditEntry>();
        foreach (var file in GetFilesInRange(q.From, q.To))
            entries.AddRange(await ReadFileAsync(file, ct));

        IEnumerable<AuditEntry> query = entries;

        if (fromUtc is not null) query = query.Where(x => x.TimestampUtc >= fromUtc);
        if (toUtc is not null) query = query.Where(x => x.TimestampUtc < toUtc);

        if (!string.IsNullOrWhiteSpace(q.Module))
            query = query.Where(x => Eq(x.Module, q.Module));

        if (!string.IsNullOrWhiteSpace(q.Operation))
            query = query.Where(x => Eq(x.Operation, q.Operation));

        if (q.Success is not null)
            query = query.Where(x => x.Success == q.Success);

        if (!string.IsNullOrWhiteSpace(q.UserContains))
        {
            var u = q.UserContains.Trim();
            query = query.Where(x => Has(x.UserName, u) || Has(x.DisplayName, u));
        }

        if (!string.IsNullOrWhiteSpace(q.ObjectContains))
        {
            var o = q.ObjectContains.Trim();
            var isId = int.TryParse(o, out var objectId);
            query = query.Where(x => (isId && x.ObjectId == objectId) || Has(x.ObjectName, o) || Has(x.FolderPath, o));
        }

        var filtered = query.OrderByDescending(x => x.TimestampUtc).ToList();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<AuditEntry>(items, page, pageSize, page * pageSize < filtered.Count, filtered.Count);
    }

    /// <summary>Tarih aralığına giren aylık dosyalar (aralık yoksa hepsi).</summary>
    private IEnumerable<string> GetFilesInRange(DateTime? from, DateTime? to)
    {
        if (!System.IO.Directory.Exists(_directory)) return Array.Empty<string>();

        var fromMonth = from is null ? (DateTime?)null : new DateTime(from.Value.Year, from.Value.Month, 1);
        var toMonth = to is null ? (DateTime?)null : new DateTime(to.Value.Year, to.Value.Month, 1);

        return System.IO.Directory
            .EnumerateFiles(_directory, $"{FilePrefix}*{FileExtension}")
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f)[FilePrefix.Length..];
                if (!DateTime.TryParseExact(name, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                    return false;
                return (fromMonth is null || month >= fromMonth) && (toMonth is null || month <= toMonth);
            })
            .OrderBy(f => f);
    }

    private async Task<List<AuditEntry>> ReadFileAsync(string path, CancellationToken ct)
    {
        var list = new List<AuditEntry>();
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            string? line;
            var lineNo = 0;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions);
                    if (entry is not null) list.Add(entry);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Audit satırı okunamadı: {File} satır {Line}", Path.GetFileName(path), lineNo);
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Audit dosyası okunamadı: {File}", path);
        }

        return list;
    }

    private string GetFilePath(DateTime localTime) =>
        Path.Combine(_directory, $"{FilePrefix}{localTime:yyyy-MM}{FileExtension}");

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Has(string? value, string part) =>
        value?.Contains(part, StringComparison.CurrentCultureIgnoreCase) == true;
}
