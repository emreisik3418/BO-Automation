using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Reports.Dtos;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Application.Reports;

public interface IReportService
{
    Task<PagedResult<ReportListItemDto>> SearchAsync(ReportSearchCriteria criteria, CancellationToken ct = default);

    Task<IReadOnlyList<FolderDto>> GetFoldersAsync(CancellationToken ct = default);

    Task<DeletePreviewDto> PrepareDeleteAsync(IReadOnlyCollection<int> reportIds, CancellationToken ct = default);

    /// <summary>
    /// Raporları siler veya arşive taşır. Her rapor ayrı işlenir; bir hata diğerlerini durdurmaz.
    /// Her rapor için audit kaydı yazılır.
    /// </summary>
    Task<IReadOnlyList<DeleteItemResult>> DeleteAsync(IReadOnlyCollection<int> reportIds, DeleteOptions options);

    /// <summary>TXT içeriğindeki CUID'leri çözer, hedef klasörü doğrular ve önizleme döndürür.</summary>
    Task<BulkBackupPreviewDto> PrepareBulkBackupAsync(string fileContent, int targetFolderId, CancellationToken ct = default);

    /// <summary>Raporları yedek klasörüne taşır (orijinal konumdan kalkar). Her rapor ayrı işlenir ve audit'e yazılır.</summary>
    Task<IReadOnlyList<DeleteItemResult>> ExecuteBulkBackupAsync(IReadOnlyCollection<int> reportIds, int targetFolderId, int confirmationCount);
}

public sealed class ReportService : IReportService
{
    public const string ModuleName = "Reports";

    /// <summary>Rapor arama ekranından tek seferde işlenebilecek rapor sayısı.</summary>
    public const int MaxBatchSize = 200;

    /// <summary>TXT ile toplu işlemde tek seferde işlenebilecek rapor sayısı.</summary>
    public const int MaxBulkSize = 500;

    private readonly IBoReportGateway _gateway;
    private readonly IAuditLogService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentEnvironment _environment;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IBoReportGateway gateway,
        IAuditLogService audit,
        ICurrentUser currentUser,
        ICurrentEnvironment environment,
        ILogger<ReportService> logger)
    {
        _gateway = gateway;
        _audit = audit;
        _currentUser = currentUser;
        _environment = environment;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Arama
    // ------------------------------------------------------------------

    public Task<PagedResult<ReportListItemDto>> SearchAsync(ReportSearchCriteria criteria, CancellationToken ct = default)
    {
        criteria.Page = Math.Max(1, criteria.Page);
        criteria.PageSize = Math.Clamp(criteria.PageSize, 10, 200);
        criteria.NameContains = Clean(criteria.NameContains);
        criteria.OwnerContains = Clean(criteria.OwnerContains);

        if (!string.IsNullOrEmpty(criteria.Kind))
        {
            criteria.Kind = ReportKinds.Normalize(criteria.Kind)
                ?? throw new AppValidationException("Geçersiz rapor türü seçildi.");
        }

        // Tarihler ters girildiyse düzelt
        if (criteria.UpdatedFrom > criteria.UpdatedTo)
            (criteria.UpdatedFrom, criteria.UpdatedTo) = (criteria.UpdatedTo, criteria.UpdatedFrom);

        return _gateway.SearchAsync(criteria, ct);
    }

    public Task<IReadOnlyList<FolderDto>> GetFoldersAsync(CancellationToken ct = default) =>
        _gateway.GetFoldersAsync(ct);

    // ------------------------------------------------------------------
    // Silme (arama ekranından)
    // ------------------------------------------------------------------

    public async Task<DeletePreviewDto> PrepareDeleteAsync(IReadOnlyCollection<int> reportIds, CancellationToken ct = default)
    {
        var ids = ValidateIds(reportIds, MaxBatchSize);

        var reports = await _gateway.GetReportsByIdsAsync(ids, ct);
        var items = await WithInstanceCountsAsync(reports, ct);
        var archive = await _gateway.GetArchiveFolderAsync(ct);
        var notFound = ids.Except(reports.Select(r => r.Id)).ToList();

        return new DeletePreviewDto(items, notFound, archive);
    }

    public async Task<IReadOnlyList<DeleteItemResult>> DeleteAsync(IReadOnlyCollection<int> reportIds, DeleteOptions options)
    {
        var ids = ValidateIds(reportIds, MaxBatchSize);
        ValidateConfirmation(options.ConfirmationCount, ids.Count);

        FolderDto? archive = null;
        if (options.MoveToArchive)
        {
            archive = await _gateway.GetArchiveFolderAsync(CancellationToken.None)
                ?? throw new AppValidationException($"{_environment.DisplayName} ortamında arşiv klasörü tanımlı değil (ArchiveFolderId).");
        }

        var operation = options.MoveToArchive ? ReportOperation.Archive : ReportOperation.Delete;
        return await RunBatchAsync(ids, operation, options.DeleteInstances, archive);
    }

    // ------------------------------------------------------------------
    // TXT ile toplu yedekleme (yedek klasörüne taşıma)
    // ------------------------------------------------------------------

    public async Task<BulkBackupPreviewDto> PrepareBulkBackupAsync(string fileContent, int targetFolderId, CancellationToken ct = default)
    {
        var parsed = CuidListParser.Parse(fileContent ?? string.Empty);

        if (parsed.Cuids.Count == 0)
            throw new AppValidationException("Dosyada geçerli CUID bulunamadı. Her satıra bir CUID yazılmalı.");

        if (parsed.Cuids.Count > MaxBulkSize)
            throw new AppValidationException(
                $"Tek seferde en fazla {MaxBulkSize} CUID işlenebilir. Dosyada: {parsed.Cuids.Count}. Lütfen dosyayı bölün.");

        var folder = await GetTargetFolderAsync(targetFolderId, ct);

        var reports = await _gateway.GetReportsByCuidsAsync(parsed.Cuids, ct);
        var items = await WithInstanceCountsAsync(reports, ct);

        var foundCuids = new HashSet<string>(reports.Select(r => r.Cuid ?? string.Empty), StringComparer.Ordinal);
        var notFound = parsed.Cuids.Where(c => !foundCuids.Contains(c)).ToList();

        return new BulkBackupPreviewDto(items, notFound, parsed.InvalidEntries, parsed.DuplicateCount, folder);
    }

    public async Task<IReadOnlyList<DeleteItemResult>> ExecuteBulkBackupAsync(
        IReadOnlyCollection<int> reportIds, int targetFolderId, int confirmationCount)
    {
        var ids = ValidateIds(reportIds, MaxBulkSize);
        ValidateConfirmation(confirmationCount, ids.Count);

        var folder = await GetTargetFolderAsync(targetFolderId, CancellationToken.None);
        return await RunBatchAsync(ids, ReportOperation.BackupMove, deleteInstances: false, folder);
    }

    private async Task<FolderDto> GetTargetFolderAsync(int targetFolderId, CancellationToken ct)
    {
        if (targetFolderId <= 0)
            throw new AppValidationException("Lütfen geçerli bir yedek klasör ID'si girin.");

        return await _gateway.GetFolderAsync(targetFolderId, ct)
            ?? throw new AppValidationException(
                $"{targetFolderId} ID'li klasör {_environment.DisplayName} ortamında bulunamadı.");
    }

    // ------------------------------------------------------------------
    // Ortak toplu işlem
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<DeleteItemResult>> RunBatchAsync(
        List<int> ids, ReportOperation operation, bool deleteInstances, FolderDto? targetFolder)
    {
        // Toplu işlem kullanıcı tarayıcıyı kapatsa bile yarıda kalmasın diye
        // istek iptal token'ı kullanılmaz.
        var ct = CancellationToken.None;
        var batchId = Guid.NewGuid();

        _logger.LogInformation(
            "Toplu işlem başladı. Batch: {BatchId}, Ortam: {Environment}, Kullanıcı: {User}, İşlem: {Operation}, Rapor: {Count}, Hedef: {Target}, Instance sil: {Inst}",
            batchId, _environment.Key, _currentUser.UserName, operation, ids.Count, targetFolder?.Path, deleteInstances);

        // Güvenlik: yalnızca gerçekten rapor olan nesneleri işle (POST edilen ID'ye güvenme)
        var reports = (await _gateway.GetReportsByIdsAsync(ids, ct)).ToDictionary(r => r.Id);

        var results = new List<DeleteItemResult>(ids.Count);
        foreach (var id in ids)
        {
            DeleteItemResult result;
            if (reports.TryGetValue(id, out var report))
            {
                result = await ProcessOneAsync(report, operation, deleteInstances, targetFolder, ct);
            }
            else
            {
                result = new DeleteItemResult(id, $"(ID: {id})", null, operation, false,
                    "Rapor bulunamadı veya desteklenen bir rapor türü değil.");
            }

            results.Add(result);
            await WriteAuditAsync(result, report, targetFolder, batchId);
        }

        _logger.LogInformation("Toplu işlem bitti. Batch: {BatchId}, Başarılı: {Ok}, Başarısız: {Fail}",
            batchId, results.Count(r => r.Success), results.Count(r => !r.Success));

        return results;
    }

    private async Task<DeleteItemResult> ProcessOneAsync(
        ReportListItemDto report, ReportOperation operation, bool deleteInstances, FolderDto? targetFolder, CancellationToken ct)
    {
        int instancesDeleted = 0, instancesFailed = 0;
        string? firstInstanceError = null;

        try
        {
            // 1) Instance'lar
            if (deleteInstances)
            {
                var instanceIds = await _gateway.GetInstanceIdsAsync(report.Id, ct);
                foreach (var instanceId in instanceIds)
                {
                    try
                    {
                        await _gateway.DeleteObjectAsync(instanceId, report.Kind, report.Id, ct);
                        instancesDeleted++;
                    }
                    catch (Exception ex) when (ex is BoException or AppValidationException)
                    {
                        instancesFailed++;
                        firstInstanceError ??= UserMessageOf(ex);
                        _logger.LogWarning(ex, "Instance silinemedi. Rapor: {ReportId}, Instance: {InstanceId}", report.Id, instanceId);
                    }
                }
            }

            // 2) Rapor
            string message;
            if (operation is ReportOperation.Archive or ReportOperation.BackupMove)
            {
                if (report.ParentFolderId == targetFolder!.Id)
                {
                    return new DeleteItemResult(report.Id, report.Name, report.FolderPath, operation, false,
                        "Rapor zaten hedef klasörde.", instancesDeleted, instancesFailed);
                }

                await _gateway.MoveToFolderAsync(report.Id, report.Kind, targetFolder.Id, ct);
                message = operation == ReportOperation.BackupMove
                    ? $"Yedek klasörüne taşındı: {targetFolder.Path}"
                    : $"Arşiv klasörüne taşındı: {targetFolder.Path}";
            }
            else
            {
                var outcome = await _gateway.DeleteObjectAsync(report.Id, report.Kind, report.ParentFolderId, ct);
                message = outcome == DeleteOutcome.MovedToRecycleBin
                    ? "Silindi (BO Geri Dönüşüm Kutusu'na alındı)."
                    : "Silindi.";
            }

            if (deleteInstances)
            {
                message += $" Instance: {instancesDeleted} silindi";
                message += instancesFailed > 0 ? $", {instancesFailed} silinemedi ({firstInstanceError})." : ".";
            }

            return new DeleteItemResult(report.Id, report.Name, report.FolderPath, operation, true,
                message, instancesDeleted, instancesFailed);
        }
        catch (Exception ex) when (ex is BoException or AppValidationException)
        {
            _logger.LogWarning(ex, "Rapor işlemi başarısız. Rapor: {ReportId} {ReportName}", report.Id, report.Name);
            return new DeleteItemResult(report.Id, report.Name, report.FolderPath, operation, false,
                UserMessageOf(ex), instancesDeleted, instancesFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rapor işleminde beklenmeyen hata. Rapor: {ReportId} {ReportName}", report.Id, report.Name);
            return new DeleteItemResult(report.Id, report.Name, report.FolderPath, operation, false,
                $"Beklenmeyen hata: {ex.Message}", instancesDeleted, instancesFailed);
        }
    }

    private async Task WriteAuditAsync(DeleteItemResult result, ReportListItemDto? report, FolderDto? targetFolder, Guid batchId)
    {
        var isMove = result.Operation is ReportOperation.Archive or ReportOperation.BackupMove;

        var entry = new AuditEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Environment = _environment.Key,
            UserName = _currentUser.UserName,
            DisplayName = _currentUser.DisplayName,
            Module = ModuleName,
            Operation = result.Operation.ToString(),
            ObjectId = result.ReportId,
            ObjectName = result.ReportName,
            ObjectKind = report?.Kind,
            ObjectCuid = report?.Cuid,
            FolderPath = result.FolderPath,
            TargetPath = isMove ? targetFolder?.Path : null,
            Success = result.Success,
            Message = result.Message,
            InstanceCount = result.InstancesDeleted > 0 ? result.InstancesDeleted : null,
            BatchId = batchId
        };

        await _audit.WriteAsync(entry);
    }

    // ------------------------------------------------------------------

    private async Task<List<DeletePreviewItemDto>> WithInstanceCountsAsync(IReadOnlyList<ReportListItemDto> reports, CancellationToken ct)
    {
        var counts = await _gateway.GetInstanceCountsAsync(reports.Select(r => r.Id).ToList(), ct);

        return reports
            .OrderBy(r => r.FolderPath).ThenBy(r => r.Name)
            .Select(r => new DeletePreviewItemDto(r, counts.TryGetValue(r.Id, out var c) ? c : 0))
            .ToList();
    }

    private static List<int> ValidateIds(IReadOnlyCollection<int>? reportIds, int max)
    {
        var ids = (reportIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();

        if (ids.Count == 0)
            throw new AppValidationException("Lütfen en az bir rapor seçin.");

        if (ids.Count > max)
            throw new AppValidationException($"Tek seferde en fazla {max} rapor işlenebilir. Seçili: {ids.Count}.");

        return ids;
    }

    private static void ValidateConfirmation(int confirmationCount, int expected)
    {
        if (confirmationCount != expected)
            throw new AppValidationException(
                $"Onay için yazılan sayı ({confirmationCount}) işlenecek rapor sayısıyla ({expected}) eşleşmiyor.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string UserMessageOf(Exception ex) => ex switch
    {
        BoException bo => bo.UserMessage,
        AppValidationException v => v.UserMessage,
        _ => ex.Message
    };
}
