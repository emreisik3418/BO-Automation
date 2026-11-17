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
}

public sealed class ReportService : IReportService
{
    public const string ModuleName = "Reports";
    public const int MaxBatchSize = 200;

    private readonly IBoReportGateway _gateway;
    private readonly IAuditLogService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IBoReportGateway gateway,
        IAuditLogService audit,
        ICurrentUser currentUser,
        ILogger<ReportService> logger)
    {
        _gateway = gateway;
        _audit = audit;
        _currentUser = currentUser;
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
    // Silme önizleme
    // ------------------------------------------------------------------

    public async Task<DeletePreviewDto> PrepareDeleteAsync(IReadOnlyCollection<int> reportIds, CancellationToken ct = default)
    {
        var ids = ValidateIds(reportIds);

        var reports = await _gateway.GetReportsByIdsAsync(ids, ct);
        var counts = await _gateway.GetInstanceCountsAsync(reports.Select(r => r.Id).ToList(), ct);
        var archive = await _gateway.GetArchiveFolderAsync(ct);

        var items = reports
            .OrderBy(r => r.FolderPath).ThenBy(r => r.Name)
            .Select(r => new DeletePreviewItemDto(r, counts.TryGetValue(r.Id, out var c) ? c : 0))
            .ToList();

        var notFound = ids.Except(reports.Select(r => r.Id)).ToList();

        return new DeletePreviewDto(items, notFound, archive);
    }

    // ------------------------------------------------------------------
    // Silme / arşive taşıma
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<DeleteItemResult>> DeleteAsync(IReadOnlyCollection<int> reportIds, DeleteOptions options)
    {
        var ids = ValidateIds(reportIds);

        if (options.ConfirmationCount != ids.Count)
            throw new AppValidationException(
                $"Onay için yazılan sayı ({options.ConfirmationCount}) seçili rapor sayısıyla ({ids.Count}) eşleşmiyor.");

        // Toplu işlem kullanıcı tarayıcıyı kapatsa bile yarıda kalmasın diye
        // istek iptal token'ı kullanılmaz.
        var ct = CancellationToken.None;

        FolderDto? archive = null;
        if (options.MoveToArchive)
        {
            archive = await _gateway.GetArchiveFolderAsync(ct)
                ?? throw new AppValidationException("Arşiv klasörü tanımlı değil (SapBo:ArchiveFolderId).");
        }

        var batchId = Guid.NewGuid();
        var operation = options.MoveToArchive ? ReportOperation.Archive : ReportOperation.Delete;

        _logger.LogInformation("Toplu işlem başladı. Batch: {BatchId}, Kullanıcı: {User}, İşlem: {Operation}, Rapor: {Count}, Instance sil: {Inst}",
            batchId, _currentUser.UserName, operation, ids.Count, options.DeleteInstances);

        // Güvenlik: yalnızca gerçekten rapor olan nesneleri işle (POST edilen ID'ye güvenme)
        var reports = (await _gateway.GetReportsByIdsAsync(ids, ct)).ToDictionary(r => r.Id);

        var results = new List<DeleteItemResult>(ids.Count);
        foreach (var id in ids)
        {
            DeleteItemResult result;
            if (reports.TryGetValue(id, out var report))
            {
                result = await ProcessOneAsync(report, operation, options.DeleteInstances, archive, ct);
            }
            else
            {
                result = new DeleteItemResult(id, $"(ID: {id})", null, operation, false,
                    "Rapor bulunamadı veya desteklenen bir rapor türü değil.");
            }

            results.Add(result);
            await WriteAuditAsync(result, report?.Kind, archive, batchId);
        }

        _logger.LogInformation("Toplu işlem bitti. Batch: {BatchId}, Başarılı: {Ok}, Başarısız: {Fail}",
            batchId, results.Count(r => r.Success), results.Count(r => !r.Success));

        return results;
    }

    private async Task<DeleteItemResult> ProcessOneAsync(
        ReportListItemDto report, ReportOperation operation, bool deleteInstances, FolderDto? archive, CancellationToken ct)
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
            if (operation == ReportOperation.Archive)
            {
                if (report.ParentFolderId == archive!.Id)
                {
                    return new DeleteItemResult(report.Id, report.Name, report.FolderPath, operation, false,
                        "Rapor zaten arşiv klasöründe.", instancesDeleted, instancesFailed);
                }

                await _gateway.MoveToFolderAsync(report.Id, report.Kind, archive.Id, ct);
                message = $"Arşiv klasörüne taşındı: {archive.Path}";
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

    private async Task WriteAuditAsync(DeleteItemResult result, string? kind, FolderDto? archive, Guid batchId)
    {
        var entry = new AuditEntry
        {
            TimestampUtc = DateTime.UtcNow,
            UserName = _currentUser.UserName,
            DisplayName = _currentUser.DisplayName,
            Module = ModuleName,
            Operation = result.Operation.ToString(),
            ObjectId = result.ReportId,
            ObjectName = result.ReportName,
            ObjectKind = kind,
            FolderPath = result.FolderPath,
            TargetPath = result.Operation == ReportOperation.Archive ? archive?.Path : null,
            Success = result.Success,
            Message = result.Message,
            InstanceCount = result.InstancesDeleted > 0 ? result.InstancesDeleted : null,
            BatchId = batchId
        };

        await _audit.WriteAsync(entry);
    }

    // ------------------------------------------------------------------

    private static List<int> ValidateIds(IReadOnlyCollection<int>? reportIds)
    {
        var ids = (reportIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();

        if (ids.Count == 0)
            throw new AppValidationException("Lütfen en az bir rapor seçin.");

        if (ids.Count > MaxBatchSize)
            throw new AppValidationException($"Tek seferde en fazla {MaxBatchSize} rapor işlenebilir. Seçili: {ids.Count}.");

        return ids;
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
