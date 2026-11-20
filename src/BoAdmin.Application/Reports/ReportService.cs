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

    Task<FolderDto?> GetFolderAsync(int folderId, CancellationToken ct = default);

    /// <summary>Yolunda verilen metni içeren klasörler (otomatik tamamlama için, en fazla <paramref name="take"/>).</summary>
    Task<IReadOnlyList<FolderDto>> SearchFoldersAsync(string term, int take = 25, CancellationToken ct = default);

    Task<DeletePreviewDto> PrepareDeleteAsync(IReadOnlyCollection<int> reportIds, CancellationToken ct = default);

    /// <summary>
    /// Raporları siler veya arşive taşır. Her rapor ayrı işlenir; bir hata diğerlerini durdurmaz.
    /// Her rapor için audit kaydı yazılır.
    /// </summary>
    Task<IReadOnlyList<DeleteItemResult>> DeleteAsync(IReadOnlyCollection<int> reportIds, DeleteOptions options);

    /// <summary>TXT içeriğindeki CUID'leri çözer, hedef klasörü doğrular ve önizleme döndürür.</summary>
    Task<BulkBackupPreviewDto> PrepareBulkBackupAsync(string fileContent, int targetFolderId,
        IOperationProgress? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Her raporu yedek klasörüne "{ad}_{CUID}" adıyla kopyalar, kopya doğrulanınca orijinali siler.
    /// Her rapor ayrı işlenir ve audit'e yazılır.
    /// </summary>
    Task<IReadOnlyList<DeleteItemResult>> ExecuteBulkBackupAsync(IReadOnlyCollection<int> reportIds, int targetFolderId, int confirmationCount,
        IOperationProgress? progress = null);

    /// <summary>Excel satırlarındaki CUID'leri çözer, yeni adları doğrular ve önizleme döndürür.</summary>
    Task<RenamePreviewDto> PrepareRenameAsync(IReadOnlyList<RenameRow> rows, IReadOnlyList<RenameRowIssue> fileIssues,
        IOperationProgress? progress = null, CancellationToken ct = default);

    /// <summary>Onaylanan ad değişikliklerini uygular. Her rapor ayrı işlenir ve audit'e yazılır.</summary>
    Task<IReadOnlyList<DeleteItemResult>> ExecuteRenameAsync(IReadOnlyList<RenameRequest> requests, int confirmationCount,
        IOperationProgress? progress = null);

    /// <summary>Excel satırlarındaki CUID'leri çözer, mevcut keyword'leri okur ve önizleme döndürür.</summary>
    Task<KeywordPreviewDto> PrepareKeywordsAsync(IReadOnlyList<KeywordRow> rows, IReadOnlyList<RenameRowIssue> fileIssues,
        IOperationProgress? progress = null, CancellationToken ct = default);

    /// <summary>Onaylanan keyword değişikliklerini uygular. Her rapor ayrı işlenir ve audit'e yazılır.</summary>
    Task<IReadOnlyList<DeleteItemResult>> ExecuteKeywordsAsync(IReadOnlyList<KeywordRequest> requests, int confirmationCount,
        IOperationProgress? progress = null);
}

public sealed class ReportService : IReportService
{
    public const string ModuleName = "Reports";

    /// <summary>Rapor arama ekranından tek seferde işlenebilecek rapor sayısı.</summary>
    public const int MaxBatchSize = 200;

    /// <summary>TXT ile toplu işlemde tek seferde işlenebilecek rapor sayısı.</summary>
    public const int MaxBulkSize = 500;

    /// <summary>BO nesne adı için güvenli üst sınır (SI_NAME).</summary>
    public const int MaxObjectNameLength = 255;

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

    public Task<FolderDto?> GetFolderAsync(int folderId, CancellationToken ct = default) =>
        _gateway.GetFolderAsync(folderId, ct);

    public async Task<IReadOnlyList<FolderDto>> SearchFoldersAsync(string term, int take = 25, CancellationToken ct = default)
    {
        term = term?.Trim() ?? string.Empty;
        if (term.Length < 2) return Array.Empty<FolderDto>();

        var folders = await _gateway.GetFoldersAsync(ct);
        var isId = int.TryParse(term, out var id);

        return folders
            .Where(f => (isId && f.Id == id) || f.Path.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            // Klasör adı terimle başlayanlar ve kısa yollar önce
            .OrderByDescending(f => isId && f.Id == id)
            .ThenByDescending(f => f.Name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(f => f.Path.Length)
            .Take(Math.Clamp(take, 1, 50))
            .ToList();
    }

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
    // TXT ile toplu yedekleme: kopyala ({ad}_{CUID}) + orijinali sil
    // ------------------------------------------------------------------

    /// <summary>Önizleme adım sayısı (ilerleme çubuğu için).</summary>
    public const int BulkPreviewSteps = 4;

    /// <summary>Toplu yedekleme adım sayısı (ilerleme çubuğu için).</summary>
    public const int BulkExecuteSteps = 2;

    private const int LookupChunkSize = 100;

    public async Task<BulkBackupPreviewDto> PrepareBulkBackupAsync(string fileContent, int targetFolderId,
        IOperationProgress? progress = null, CancellationToken ct = default)
    {
        progress ??= NullOperationProgress.Instance;

        progress.Phase("CUID listesi okunuyor");
        var parsed = CuidListParser.Parse(fileContent ?? string.Empty);

        if (parsed.Cuids.Count == 0)
            throw new AppValidationException("Dosyada geçerli CUID bulunamadı. Her satıra bir CUID yazılmalı.");

        if (parsed.Cuids.Count > MaxBulkSize)
            throw new AppValidationException(
                $"Tek seferde en fazla {MaxBulkSize} CUID işlenebilir. Dosyada: {parsed.Cuids.Count}. Lütfen dosyayı bölün.");

        progress.Phase("Yedek klasörü kontrol ediliyor");
        var folder = await GetTargetFolderAsync(targetFolderId, ct);

        // CUID'ler 100'lük gruplar halinde aranır; her grup bir ilerleme adımı
        var cuidChunks = parsed.Cuids.Chunk(LookupChunkSize).ToList();
        progress.Phase("Raporlar BO'da aranıyor", parsed.Cuids.Count);

        var reports = new List<ReportListItemDto>();
        foreach (var chunk in cuidChunks)
        {
            reports.AddRange(await _gateway.GetReportsByCuidsAsync(chunk, ct));
            for (var i = 0; i < chunk.Length; i++) progress.Step();
        }

        progress.Phase("Instance sayıları alınıyor", reports.Count);
        var withCounts = await WithInstanceCountsAsync(reports, ct, progress);

        var items = withCounts.Select(i =>
        {
            var r = i.Report;
            var backupName = BuildBackupName(r.Name, r.Cuid ?? string.Empty);

            if (!ReportKinds.IsCopyable(r.Kind))
                return new BulkPreviewItemDto(r, i.InstanceCount, backupName, false,
                    $"{r.KindDisplayName} raporları REST ile kopyalanamıyor; yalnızca Web Intelligence desteklenir.");

            if (string.IsNullOrEmpty(r.Cuid))
                return new BulkPreviewItemDto(r, i.InstanceCount, backupName, false, "Raporun CUID'i okunamadı.");

            return new BulkPreviewItemDto(r, i.InstanceCount, backupName, true);
        }).ToList();

        var foundCuids = new HashSet<string>(reports.Select(r => r.Cuid ?? string.Empty), StringComparer.Ordinal);
        var notFound = parsed.Cuids.Where(c => !foundCuids.Contains(c)).ToList();

        return new BulkBackupPreviewDto(items, notFound, parsed.InvalidEntries, parsed.DuplicateCount, folder);
    }

    public async Task<IReadOnlyList<DeleteItemResult>> ExecuteBulkBackupAsync(
        IReadOnlyCollection<int> reportIds, int targetFolderId, int confirmationCount, IOperationProgress? progress = null)
    {
        progress ??= NullOperationProgress.Instance;

        var ids = ValidateIds(reportIds, MaxBulkSize);
        ValidateConfirmation(confirmationCount, ids.Count);

        progress.Phase("Raporlar ve yedek klasörü doğrulanıyor");
        var folder = await GetTargetFolderAsync(targetFolderId, CancellationToken.None);
        return await RunBatchAsync(ids, ReportOperation.BackupCopy, deleteInstances: false, folder, progress,
            "Raporlar yedekleniyor ve siliniyor");
    }

    // ------------------------------------------------------------------
    // Excel ile toplu ad güncelleme
    // ------------------------------------------------------------------

    /// <summary>Ad güncelleme önizlemesi adım sayısı.</summary>
    public const int RenamePreviewSteps = 3;

    /// <summary>Ad güncelleme adım sayısı.</summary>
    public const int RenameExecuteSteps = 2;

    public async Task<RenamePreviewDto> PrepareRenameAsync(IReadOnlyList<RenameRow> rows, IReadOnlyList<RenameRowIssue> fileIssues,
        IOperationProgress? progress = null, CancellationToken ct = default)
    {
        progress ??= NullOperationProgress.Instance;

        progress.Phase("Excel satırları kontrol ediliyor", rows.Count);
        var invalid = new List<RenameRowIssue>(fileIssues);
        var valid = new List<RenameRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            progress.Step();

            var nameError = ValidateNewName(row.NewName);
            if (nameError is not null)
            {
                invalid.Add(new RenameRowIssue(row.RowNumber, row.Cuid, nameError));
                continue;
            }

            if (!seen.Add(row.Cuid))
            {
                invalid.Add(new RenameRowIssue(row.RowNumber, row.Cuid, "Bu CUID dosyada daha önce geçiyor; yalnızca ilk satır işlenir."));
                continue;
            }

            valid.Add(row with { NewName = row.NewName.Trim() });
        }

        if (valid.Count == 0)
            throw new AppValidationException(invalid.Count > 0
                ? $"Excel'de işlenebilecek satır yok ({invalid.Count} satır hatalı). İlk hata: satır {invalid[0].RowNumber} - {invalid[0].Reason}"
                : "Excel'de CUID ve yeni ad içeren satır bulunamadı.");

        if (valid.Count > MaxBulkSize)
            throw new AppValidationException(
                $"Tek seferde en fazla {MaxBulkSize} rapor işlenebilir. Dosyada: {valid.Count}. Lütfen dosyayı bölün.");

        progress.Phase("Raporlar BO'da aranıyor", valid.Count);
        var reports = new List<ReportListItemDto>();
        foreach (var chunk in valid.Select(r => r.Cuid).Chunk(LookupChunkSize))
        {
            reports.AddRange(await _gateway.GetReportsByCuidsAsync(chunk, ct));
            for (var i = 0; i < chunk.Length; i++) progress.Step();
        }

        progress.Phase("Önizleme hazırlanıyor");
        var byCuid = reports.Where(r => r.Cuid is not null)
            .GroupBy(r => r.Cuid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var items = new List<RenamePreviewItemDto>();
        var notFound = new List<RenameRowIssue>();

        foreach (var row in valid)
        {
            if (!byCuid.TryGetValue(row.Cuid, out var report))
            {
                notFound.Add(new RenameRowIssue(row.RowNumber, row.Cuid, "Bu ortamda bulunamadı veya desteklenen bir rapor türü değil."));
                continue;
            }

            items.Add(string.Equals(report.Name, row.NewName, StringComparison.Ordinal)
                ? new RenamePreviewItemDto(row.RowNumber, report, row.NewName, false, "Yeni ad mevcut adla aynı.")
                : new RenamePreviewItemDto(row.RowNumber, report, row.NewName, true));
        }

        return new RenamePreviewDto(items, notFound, invalid.OrderBy(i => i.RowNumber).ToList());
    }

    public async Task<IReadOnlyList<DeleteItemResult>> ExecuteRenameAsync(IReadOnlyList<RenameRequest> requests, int confirmationCount,
        IOperationProgress? progress = null)
    {
        progress ??= NullOperationProgress.Instance;
        var ct = CancellationToken.None; // yarıda kalmasın

        var distinct = requests.Where(r => r.ReportId > 0).GroupBy(r => r.ReportId).Select(g => g.First()).ToList();
        var ids = ValidateIds(distinct.Select(r => r.ReportId).ToList(), MaxBulkSize);
        ValidateConfirmation(confirmationCount, ids.Count);

        progress.Phase("Raporlar doğrulanıyor");
        var reports = (await _gateway.GetReportsByIdsAsync(ids, ct)).ToDictionary(r => r.Id);
        var batchId = Guid.NewGuid();

        _logger.LogInformation("Toplu ad güncelleme başladı. Batch: {BatchId}, Ortam: {Environment}, Kullanıcı: {User}, Rapor: {Count}",
            batchId, _environment.Key, _currentUser.UserName, ids.Count);

        progress.Phase("Rapor adları güncelleniyor", distinct.Count);
        var results = new List<DeleteItemResult>(distinct.Count);

        foreach (var request in distinct)
        {
            DeleteItemResult result;
            reports.TryGetValue(request.ReportId, out var report);

            if (report is null)
            {
                result = new DeleteItemResult(request.ReportId, $"(ID: {request.ReportId})", null, ReportOperation.Rename, false,
                    "Rapor bulunamadı veya desteklenen bir rapor türü değil.");
            }
            else
            {
                progress.Current(report.Name);
                var newName = request.NewName.Trim();
                var nameError = ValidateNewName(newName);

                if (nameError is not null)
                {
                    result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Rename, false, nameError);
                }
                else if (string.Equals(report.Name, newName, StringComparison.Ordinal))
                {
                    result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Rename, false,
                        "Yeni ad mevcut adla aynı; işlem yapılmadı.");
                }
                else
                {
                    try
                    {
                        await _gateway.RenameAsync(report.Id, newName, ct);
                        result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Rename, true,
                            $"Ad güncellendi: '{report.Name}' → '{newName}'");
                    }
                    catch (Exception ex) when (ex is BoException or AppValidationException)
                    {
                        _logger.LogWarning(ex, "Ad güncellenemedi. Rapor: {ReportId} {ReportName}", report.Id, report.Name);
                        result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Rename, false,
                            $"Ad güncellenemedi: {UserMessageOf(ex)}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ad güncellemede beklenmeyen hata. Rapor: {ReportId}", report.Id);
                        result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Rename, false,
                            $"Beklenmeyen hata: {ex.Message}");
                    }
                }
            }

            results.Add(result);
            await WriteAuditAsync(result, report, null, batchId);
            progress.Step(result.ReportName, result.Success);
        }

        _logger.LogInformation("Toplu ad güncelleme bitti. Batch: {BatchId}, Başarılı: {Ok}, Başarısız: {Fail}",
            batchId, results.Count(r => r.Success), results.Count(r => !r.Success));

        return results;
    }

    // ------------------------------------------------------------------
    // Excel ile toplu keyword güncelleme
    // ------------------------------------------------------------------

    public const int KeywordPreviewSteps = 3;
    public const int KeywordExecuteSteps = 2;

    public async Task<KeywordPreviewDto> PrepareKeywordsAsync(IReadOnlyList<KeywordRow> rows, IReadOnlyList<RenameRowIssue> fileIssues,
        IOperationProgress? progress = null, CancellationToken ct = default)
    {
        progress ??= NullOperationProgress.Instance;

        // 1) Satır kontrolü
        progress.Phase("Excel satırları kontrol ediliyor", rows.Count);
        var invalid = new List<RenameRowIssue>(fileIssues);
        var valid = new List<KeywordRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            progress.Step();

            var keywords = KeywordList.Parse(row.Keywords);
            if (keywords.Count == 0 && row.Mode == KeywordMode.Add)
            {
                invalid.Add(new RenameRowIssue(row.RowNumber, row.Cuid, "Eklenecek keyword yok. Tüm keyword'leri silmek için İşlem sütununa 'Değiştir' yazın."));
                continue;
            }

            if (KeywordList.Format(keywords).Length > KeywordList.MaxLength)
            {
                invalid.Add(new RenameRowIssue(row.RowNumber, row.Cuid, $"Keyword metni en fazla {KeywordList.MaxLength} karakter olabilir."));
                continue;
            }

            if (!seen.Add(row.Cuid))
            {
                invalid.Add(new RenameRowIssue(row.RowNumber, row.Cuid, "Bu CUID dosyada daha önce geçiyor; yalnızca ilk satır işlenir."));
                continue;
            }

            valid.Add(row);
        }

        if (valid.Count == 0)
            throw new AppValidationException(invalid.Count > 0
                ? $"Excel'de işlenebilecek satır yok ({invalid.Count} satır hatalı). İlk hata: satır {invalid[0].RowNumber} - {invalid[0].Reason}"
                : "Excel'de CUID ve keyword içeren satır bulunamadı.");

        if (valid.Count > MaxBulkSize)
            throw new AppValidationException(
                $"Tek seferde en fazla {MaxBulkSize} rapor işlenebilir. Dosyada: {valid.Count}. Lütfen dosyayı bölün.");

        // 2) Raporlar
        progress.Phase("Raporlar BO'da aranıyor", valid.Count);
        var reports = new List<ReportListItemDto>();
        foreach (var chunk in valid.Select(r => r.Cuid).Chunk(LookupChunkSize))
        {
            reports.AddRange(await _gateway.GetReportsByCuidsAsync(chunk, ct));
            for (var i = 0; i < chunk.Length; i++) progress.Step();
        }

        var byCuid = reports.Where(r => r.Cuid is not null)
            .GroupBy(r => r.Cuid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var found = new List<(KeywordRow Row, ReportListItemDto Report)>();
        var notFound = new List<RenameRowIssue>();
        foreach (var row in valid)
        {
            if (byCuid.TryGetValue(row.Cuid, out var report)) found.Add((row, report));
            else notFound.Add(new RenameRowIssue(row.RowNumber, row.Cuid, "Bu ortamda bulunamadı veya desteklenen bir rapor türü değil."));
        }

        // 3) Mevcut keyword'ler (rapor başına bir istek)
        progress.Phase("Mevcut keyword'ler okunuyor", found.Count);
        var items = new List<KeywordPreviewItemDto>();
        foreach (var (row, report) in found)
        {
            progress.Current(report.Name);
            var requested = KeywordList.Format(KeywordList.Parse(row.Keywords));

            string current;
            try
            {
                current = await _gateway.GetKeywordsAsync(report.Id, ct);
            }
            catch (BoException ex)
            {
                items.Add(new KeywordPreviewItemDto(row.RowNumber, report, row.Mode, requested, "?", "?", false,
                    $"Mevcut keyword'ler okunamadı: {ex.UserMessage}"));
                progress.Step(report.Name);
                continue;
            }

            var result = KeywordList.Apply(current, row.Keywords, row.Mode);
            var unchanged = string.Equals(KeywordList.Format(KeywordList.Parse(current)), result, StringComparison.Ordinal);

            items.Add(new KeywordPreviewItemDto(row.RowNumber, report, row.Mode, requested,
                KeywordList.Format(KeywordList.Parse(current)), result,
                CanProcess: !unchanged,
                Reason: unchanged ? "Değişiklik yok (keyword'ler zaten aynı)." : null));

            progress.Step(report.Name);
        }

        return new KeywordPreviewDto(items, notFound, invalid.OrderBy(i => i.RowNumber).ToList());
    }

    public async Task<IReadOnlyList<DeleteItemResult>> ExecuteKeywordsAsync(IReadOnlyList<KeywordRequest> requests, int confirmationCount,
        IOperationProgress? progress = null)
    {
        progress ??= NullOperationProgress.Instance;
        var ct = CancellationToken.None; // yarıda kalmasın

        var distinct = requests.Where(r => r.ReportId > 0).GroupBy(r => r.ReportId).Select(g => g.First()).ToList();
        var ids = ValidateIds(distinct.Select(r => r.ReportId).ToList(), MaxBulkSize);
        ValidateConfirmation(confirmationCount, ids.Count);

        progress.Phase("Raporlar doğrulanıyor");
        var reports = (await _gateway.GetReportsByIdsAsync(ids, ct)).ToDictionary(r => r.Id);
        var batchId = Guid.NewGuid();

        _logger.LogInformation("Toplu keyword güncelleme başladı. Batch: {BatchId}, Ortam: {Environment}, Kullanıcı: {User}, Rapor: {Count}",
            batchId, _environment.Key, _currentUser.UserName, ids.Count);

        progress.Phase("Keyword'ler güncelleniyor", distinct.Count);
        var results = new List<DeleteItemResult>(distinct.Count);

        foreach (var request in distinct)
        {
            DeleteItemResult result;
            reports.TryGetValue(request.ReportId, out var report);

            if (report is null)
            {
                result = new DeleteItemResult(request.ReportId, $"(ID: {request.ReportId})", null, ReportOperation.Keywords, false,
                    "Rapor bulunamadı veya desteklenen bir rapor türü değil.");
            }
            else
            {
                progress.Current(report.Name);
                try
                {
                    // Sonuç, önizlemeden bu yana yapılmış değişiklikleri kaybetmemek için güncel değerden hesaplanır
                    var current = await _gateway.GetKeywordsAsync(report.Id, ct);
                    var newValue = KeywordList.Apply(current, request.Keywords, request.Mode);
                    var currentFormatted = KeywordList.Format(KeywordList.Parse(current));

                    if (string.Equals(currentFormatted, newValue, StringComparison.Ordinal))
                    {
                        result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Keywords, true,
                            $"Değişiklik gerekmedi; keyword'ler zaten: '{currentFormatted}'");
                    }
                    else
                    {
                        await _gateway.SetKeywordsAsync(report.Id, newValue, ct);
                        var modeText = request.Mode == KeywordMode.Replace ? "değiştirildi" : "eklendi";
                        result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Keywords, true,
                            $"Keyword'ler {modeText}: '{currentFormatted}' → '{newValue}'");
                    }
                }
                catch (Exception ex) when (ex is BoException or AppValidationException)
                {
                    _logger.LogWarning(ex, "Keyword güncellenemedi. Rapor: {ReportId} {ReportName}", report.Id, report.Name);
                    result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Keywords, false,
                        $"Keyword'ler güncellenemedi: {UserMessageOf(ex)}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Keyword güncellemede beklenmeyen hata. Rapor: {ReportId}", report.Id);
                    result = new DeleteItemResult(report.Id, report.Name, report.FolderPath, ReportOperation.Keywords, false,
                        $"Beklenmeyen hata: {ex.Message}");
                }
            }

            results.Add(result);
            await WriteAuditAsync(result, report, null, batchId);
            progress.Step(result.ReportName, result.Success);
        }

        _logger.LogInformation("Toplu keyword güncelleme bitti. Batch: {BatchId}, Başarılı: {Ok}, Başarısız: {Fail}",
            batchId, results.Count(r => r.Success), results.Count(r => !r.Success));

        return results;
    }

    /// <summary>Yeni ad kuralları. Hata yoksa null.</summary>
    public static string? ValidateNewName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0) return "Yeni ad boş.";
        if (value.Length > MaxObjectNameLength) return $"Yeni ad en fazla {MaxObjectNameLength} karakter olabilir ({value.Length}).";
        if (value.Any(char.IsControl)) return "Yeni ad satır sonu veya görünmeyen karakter içeriyor.";
        return null;
    }

    /// <summary>Yedek kopyanın adı: "{orijinal ad}_{orijinal CUID}". Uzunsa orijinal ad kısaltılır, CUID korunur.</summary>
    public static string BuildBackupName(string name, string cuid)
    {
        var suffix = "_" + cuid;
        var maxNameLength = Math.Max(1, MaxObjectNameLength - suffix.Length);
        var trimmed = name.Length > maxNameLength ? name[..maxNameLength].TrimEnd() : name;
        return trimmed + suffix;
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
        List<int> ids, ReportOperation operation, bool deleteInstances, FolderDto? targetFolder,
        IOperationProgress? progress = null, string? phaseTitle = null)
    {
        progress ??= NullOperationProgress.Instance;

        // Toplu işlem kullanıcı tarayıcıyı kapatsa bile yarıda kalmasın diye
        // istek iptal token'ı kullanılmaz.
        var ct = CancellationToken.None;
        var batchId = Guid.NewGuid();

        _logger.LogInformation(
            "Toplu işlem başladı. Batch: {BatchId}, Ortam: {Environment}, Kullanıcı: {User}, İşlem: {Operation}, Rapor: {Count}, Hedef: {Target}, Instance sil: {Inst}",
            batchId, _environment.Key, _currentUser.UserName, operation, ids.Count, targetFolder?.Path, deleteInstances);

        // Güvenlik: yalnızca gerçekten rapor olan nesneleri işle (POST edilen ID'ye güvenme)
        var reports = (await _gateway.GetReportsByIdsAsync(ids, ct)).ToDictionary(r => r.Id);

        progress.Phase(phaseTitle ?? "Raporlar işleniyor", ids.Count);

        var results = new List<DeleteItemResult>(ids.Count);
        foreach (var id in ids)
        {
            DeleteItemResult result;
            if (reports.TryGetValue(id, out var report))
            {
                progress.Current(report.Name);
                result = operation == ReportOperation.BackupCopy
                    ? await ProcessBackupCopyAsync(report, targetFolder!, ct)
                    : await ProcessOneAsync(report, operation, deleteInstances, targetFolder, ct);
            }
            else
            {
                result = new DeleteItemResult(id, $"(ID: {id})", null, operation, false,
                    "Rapor bulunamadı veya desteklenen bir rapor türü değil.");
            }

            results.Add(result);
            await WriteAuditAsync(result, report, targetFolder, batchId);
            progress.Step(result.ReportName, result.Success);
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
            if (operation == ReportOperation.Archive)
            {
                if (report.ParentFolderId == targetFolder!.Id)
                {
                    return new DeleteItemResult(report.Id, report.Name, report.FolderPath, operation, false,
                        "Rapor zaten hedef klasörde.", instancesDeleted, instancesFailed);
                }

                await _gateway.MoveToFolderAsync(report.Id, report.Kind, targetFolder.Id, ct);
                message = $"Arşiv klasörüne taşındı: {targetFolder.Path}";
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

    /// <summary>
    /// 1) Yedek klasörüne "{ad}_{CUID}" adıyla kopyala ve kopyayı doğrula
    /// 2) Orijinali sil ve silindiğini doğrula
    /// Kopya oluşmazsa orijinale dokunulmaz.
    /// </summary>
    private async Task<DeleteItemResult> ProcessBackupCopyAsync(ReportListItemDto report, FolderDto targetFolder, CancellationToken ct)
    {
        const ReportOperation operation = ReportOperation.BackupCopy;

        DeleteItemResult Fail(string message) =>
            new(report.Id, report.Name, report.FolderPath, operation, false, message);

        if (!ReportKinds.IsCopyable(report.Kind))
            return Fail($"{report.KindDisplayName} raporları REST ile kopyalanamıyor; işlem yapılmadı.");

        if (string.IsNullOrEmpty(report.Cuid))
            return Fail("Raporun CUID'i okunamadı; işlem yapılmadı.");

        var backupName = BuildBackupName(report.Name, report.Cuid);

        // 1) Kopyala
        CopyResult copy;
        try
        {
            copy = await _gateway.CopyToFolderAsync(report.Id, report.Cuid, report.Kind, targetFolder.Id, backupName, ct);
        }
        catch (Exception ex) when (ex is BoException or AppValidationException)
        {
            _logger.LogWarning(ex, "Yedek kopya oluşturulamadı. Rapor: {ReportId} {ReportName}", report.Id, report.Name);
            return Fail($"Yedek kopya oluşturulamadı, orijinal rapor silinmedi: {UserMessageOf(ex)}");
        }

        var copyInfo = $"{targetFolder.Path.TrimEnd('/')}/{copy.NewName} (yeni ID: {copy.NewId})"
                       + (copy.AlreadyExisted ? " [yedek önceki bir çalıştırmada oluşturulmuştu]" : "");

        // 2) Orijinali sil
        try
        {
            var outcome = await _gateway.DeleteObjectAsync(report.Id, report.Kind, report.ParentFolderId, ct);
            var deleted = outcome == DeleteOutcome.MovedToRecycleBin
                ? "Orijinal silindi (BO Geri Dönüşüm Kutusu'na alındı)."
                : "Orijinal silindi.";

            return new DeleteItemResult(report.Id, report.Name, report.FolderPath, operation, true,
                $"Yedeklendi: {copyInfo}. {deleted}");
        }
        catch (Exception ex) when (ex is BoException or AppValidationException)
        {
            _logger.LogWarning(ex, "Yedek oluştu ama orijinal silinemedi. Rapor: {ReportId} {ReportName}", report.Id, report.Name);
            return Fail($"Yedek oluşturuldu ({copyInfo}) ancak orijinal silinemedi: {UserMessageOf(ex)}");
        }
    }

    private async Task WriteAuditAsync(DeleteItemResult result, ReportListItemDto? report, FolderDto? targetFolder, Guid batchId)
    {
        var isMove = result.Operation is ReportOperation.Archive or ReportOperation.BackupCopy;

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

    private async Task<List<DeletePreviewItemDto>> WithInstanceCountsAsync(
        IReadOnlyList<ReportListItemDto> reports, CancellationToken ct, IOperationProgress? progress = null)
    {
        var counts = new Dictionary<int, int>();
        foreach (var chunk in reports.Select(r => r.Id).Chunk(LookupChunkSize))
        {
            foreach (var (id, count) in await _gateway.GetInstanceCountsAsync(chunk, ct))
                counts[id] = count;
            for (var i = 0; i < chunk.Length; i++) progress?.Step();
        }

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
