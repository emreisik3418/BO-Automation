using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Reports;
using BoAdmin.Application.Reports.Dtos;
using BoAdmin.Infrastructure.Bo.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// IBoReportGateway'in /biprws implementasyonu.
/// Silme ve taşıma sonrasında sonuç CMS sorgusuyla DOĞRULANIR; böylece
/// endpoint "başarılı" dönüp aslında bir şey yapmazsa kullanıcıya hata gösterilir.
/// </summary>
public sealed class BoReportGateway : IBoReportGateway
{
    private const string ReportFields = "SI_ID, SI_CUID, SI_NAME, SI_KIND, SI_OWNER, SI_PARENTID, SI_UPDATE_TS";
    private const int InClauseChunkSize = 100;

    private readonly BoRestClient _client;
    private readonly FolderPathCache _folders;
    private readonly IOptionsMonitor<BoOptions> _options;
    private readonly ILogger<BoReportGateway> _logger;

    public BoReportGateway(
        BoRestClient client,
        FolderPathCache folders,
        IOptionsMonitor<BoOptions> options,
        ILogger<BoReportGateway> logger)
    {
        _client = client;
        _folders = folders;
        _options = options;
        _logger = logger;
    }

    /// <summary>Kullanıcının seçtiği ortamın ayarları.</summary>
    private BoEnvironmentOptions Env =>
        _options.CurrentValue.Find(_client.EnvironmentKey)
        ?? throw new BoOperationException($"'{_client.EnvironmentKey}' ortamı yapılandırmada tanımlı değil.");

    private static string SupportedKindsClause =>
        CmsQueryBuilder.InList("SI_KIND", ReportKinds.All.Select(k => k.Kind));

    // ------------------------------------------------------------------
    // Arama
    // ------------------------------------------------------------------

    public async Task<PagedResult<ReportListItemDto>> SearchAsync(ReportSearchCriteria c, CancellationToken ct = default)
    {
        var tree = await _folders.GetTreeAsync(ct);
        var where = new List<string> { "SI_INSTANCE = 0" };

        where.Add(string.IsNullOrEmpty(c.Kind)
            ? SupportedKindsClause
            : CmsQueryBuilder.InList("SI_KIND", new[] { c.Kind }));

        if (!string.IsNullOrWhiteSpace(c.NameContains))
            where.Add(CmsQueryBuilder.LikeContains("SI_NAME", c.NameContains));

        if (!string.IsNullOrWhiteSpace(c.OwnerContains))
            where.Add(CmsQueryBuilder.LikeContains("SI_OWNER", c.OwnerContains));

        if (c.FolderId is > 0)
        {
            var folderIds = c.IncludeSubfolders
                ? tree.GetSelfAndDescendants(c.FolderId.Value)
                : new[] { c.FolderId.Value };

            if (folderIds.Count > 1000)
                throw new AppValidationException(
                    "Seçilen klasörün çok fazla alt klasörü var. Lütfen daha alt bir klasör seçin veya 'Alt klasörler dahil' seçeneğini kapatın.");

            where.Add(CmsQueryBuilder.InList("SI_PARENTID", folderIds));
        }

        if (c.UpdatedFrom is not null)
            where.Add($"SI_UPDATE_TS >= {CmsQueryBuilder.DateLiteral(c.UpdatedFrom.Value.Date)}");

        if (c.UpdatedTo is not null)
            where.Add($"SI_UPDATE_TS < {CmsQueryBuilder.DateLiteral(c.UpdatedTo.Value.Date.AddDays(1))}");

        var orderField = c.SortBy switch
        {
            ReportSortField.Kind => "SI_KIND",
            ReportSortField.Owner => "SI_OWNER",
            ReportSortField.UpdatedAt => "SI_UPDATE_TS",
            ReportSortField.Id => "SI_ID",
            _ => "SI_NAME"
        };
        var direction = c.SortDescending ? "DESC" : "ASC";

        var query = $"SELECT {ReportFields} FROM CI_INFOOBJECTS WHERE {string.Join(" AND ", where)} ORDER BY {orderField} {direction}";

        var result = await _client.CmsQueryAsync(query, c.Page, c.PageSize, ct);
        var items = result.Entries.Select(e => Map(e, tree)).ToList();

        return new PagedResult<ReportListItemDto>(items, c.Page, c.PageSize, result.HasMore);
    }

    public async Task<IReadOnlyList<ReportListItemDto>> GetReportsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<ReportListItemDto>();

        var tree = await _folders.GetTreeAsync(ct);
        var list = new List<ReportListItemDto>();

        foreach (var chunk in ids.Distinct().Chunk(InClauseChunkSize))
        {
            var query = $"SELECT {ReportFields} FROM CI_INFOOBJECTS " +
                        $"WHERE {CmsQueryBuilder.InList("SI_ID", chunk)} AND SI_INSTANCE = 0 AND {SupportedKindsClause}";

            var entries = await _client.CmsQueryAllAsync(query, pageSize: InClauseChunkSize, ct: ct);
            list.AddRange(entries.Select(e => Map(e, tree)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ReportListItemDto>> GetReportsByCuidsAsync(IReadOnlyCollection<string> cuids, CancellationToken ct = default)
    {
        if (cuids.Count == 0) return Array.Empty<ReportListItemDto>();

        var tree = await _folders.GetTreeAsync(ct);
        var list = new List<ReportListItemDto>();

        foreach (var chunk in cuids.Distinct(StringComparer.Ordinal).Chunk(InClauseChunkSize))
        {
            var query = $"SELECT {ReportFields} FROM CI_INFOOBJECTS " +
                        $"WHERE {CmsQueryBuilder.InList("SI_CUID", chunk)} AND SI_INSTANCE = 0 AND {SupportedKindsClause}";

            var entries = await _client.CmsQueryAllAsync(query, pageSize: InClauseChunkSize, ct: ct);
            list.AddRange(entries.Select(e => Map(e, tree)));
        }

        return list;
    }

    // ------------------------------------------------------------------
    // Instance'lar
    // ------------------------------------------------------------------

    public async Task<IReadOnlyDictionary<int, int>> GetInstanceCountsAsync(IReadOnlyCollection<int> reportIds, CancellationToken ct = default)
    {
        var counts = new Dictionary<int, int>();
        if (reportIds.Count == 0) return counts;

        foreach (var chunk in reportIds.Distinct().Chunk(InClauseChunkSize))
        {
            var query = "SELECT SI_ID, SI_PARENTID FROM CI_INFOOBJECTS " +
                        $"WHERE SI_INSTANCE = 1 AND {CmsQueryBuilder.InList("SI_PARENTID", chunk)}";

            var entries = await _client.CmsQueryAllAsync(query, pageSize: 1000, ct: ct);
            foreach (var parentId in entries.Select(e => e.GetInt("SI_PARENTID") ?? 0).Where(p => p > 0))
                counts[parentId] = counts.TryGetValue(parentId, out var n) ? n + 1 : 1;
        }

        return counts;
    }

    public async Task<IReadOnlyList<int>> GetInstanceIdsAsync(int reportId, CancellationToken ct = default)
    {
        var query = $"SELECT SI_ID FROM CI_INFOOBJECTS WHERE SI_INSTANCE = 1 AND SI_PARENTID = {reportId}";
        var entries = await _client.CmsQueryAllAsync(query, pageSize: 1000, ct: ct);
        return entries.Select(e => e.GetInt("SI_ID") ?? 0).Where(id => id > 0).ToList();
    }

    // ------------------------------------------------------------------
    // Silme
    // ------------------------------------------------------------------

    public async Task<DeleteOutcome> DeleteObjectAsync(int id, string kind, int parentId, CancellationToken ct = default)
    {
        var strategy = Env.DeleteStrategy;

        switch (strategy)
        {
            case BoDeleteStrategy.Raylight:
                if (!string.Equals(kind, ReportKinds.Webi, StringComparison.OrdinalIgnoreCase))
                    throw new AppValidationException("Raylight silme yöntemi yalnızca Web Intelligence raporlarını destekler.");
                await _client.DeleteAsync($"raylight/v1/documents/{id}", ct);
                break;

            default:
                // doğrulanmalı: DELETE /v1/infoobjects/{id}
                await _client.DeleteAsync($"v1/infoobjects/{id}", ct);
                break;
        }

        // Doğrulama
        var after = await GetObjectAsync(id, ct);
        if (after is null)
        {
            _logger.LogInformation("Nesne silindi: {Id} ({Strategy})", id, strategy);
            return DeleteOutcome.Deleted;
        }

        var newParent = after.GetInt("SI_PARENTID");
        if (newParent is not null && newParent != parentId)
        {
            _logger.LogInformation("Nesne silindi, geri dönüşüm kutusuna alındı: {Id} (yeni üst: {Parent})", id, newParent);
            return DeleteOutcome.MovedToRecycleBin;
        }

        throw new BoOperationException(
            "Silme isteği başarılı döndü ancak nesne hâlâ BO'da duruyor. Ortamın 'DeleteStrategy' ayarını kontrol edin.");
    }

    // ------------------------------------------------------------------
    // Taşıma (kurtarılabilir silme)
    // ------------------------------------------------------------------

    public async Task MoveToFolderAsync(int id, string kind, int targetFolderId, CancellationToken ct = default)
    {
        var strategy = Env.MoveStrategy;

        switch (strategy)
        {
            case BoMoveStrategy.Raylight:
                await MoveWithRaylightAsync(id, kind, targetFolderId, ct);
                return; // kopyala + sil; doğrulama içeride

            default:
                // doğrulanmalı: PUT /v1/infoobjects/{id} gövdesinde üst klasör alanı
                await _client.PutJsonAsync($"v1/infoobjects/{id}", new { parentid = targetFolderId }, ct);
                break;
        }

        var after = await GetObjectAsync(id, ct);
        var parent = after?.GetInt("SI_PARENTID");
        if (parent != targetFolderId)
        {
            throw new BoOperationException(
                "Taşıma isteği kabul edildi ancak rapor hedef klasörde görünmüyor. Ortamın 'MoveStrategy' ayarını kontrol edin.");
        }

        _logger.LogInformation("Rapor taşındı: {Id} -> klasör {Folder}", id, targetFolderId);
    }

    /// <summary>
    /// Yedek yöntem (yalnızca Webi): raporu hedef klasöre kopyala, sonra orijinali sil.
    /// DİKKAT: Yeni nesnenin ID/CUID'i değişir, instance'lar ve zamanlamalar taşınmaz.
    /// Endpoint ve gövde biçimi doğrulanmalı: POST /raylight/v1/documents?sourceId={id}
    /// </summary>
    private async Task MoveWithRaylightAsync(int id, string kind, int targetFolderId, CancellationToken ct)
    {
        if (!string.Equals(kind, ReportKinds.Webi, StringComparison.OrdinalIgnoreCase))
            throw new AppValidationException("Raylight taşıma yöntemi yalnızca Web Intelligence raporlarını destekler.");

        var original = await GetObjectAsync(id, ct)
            ?? throw new BoNotFoundException($"Rapor bulunamadı: {id}");
        var name = original.GetString("SI_NAME") ?? $"Rapor_{id}";

        await _client.PostJsonAsync($"raylight/v1/documents?sourceId={id}",
            new { document = new { name, folderId = targetFolderId } }, ct);

        // Kopyanın oluştuğunu doğrula
        var copyQuery = "SELECT SI_ID FROM CI_INFOOBJECTS " +
                        $"WHERE SI_PARENTID = {targetFolderId} AND SI_INSTANCE = 0 AND SI_KIND = 'Webi' " +
                        $"AND SI_NAME = '{CmsQueryBuilder.Sanitize(name)}'";
        var copies = await _client.CmsQueryAsync(copyQuery, 1, 1, ct);
        if (copies.Entries.Count == 0)
            throw new BoOperationException("Rapor arşiv klasörüne kopyalanamadı; orijinal rapor silinmedi.");

        await DeleteObjectAsync(id, kind, original.GetInt("SI_PARENTID") ?? 0, ct);
    }

    // ------------------------------------------------------------------
    // Kopyalama (TXT ile toplu yedekleme)
    // ------------------------------------------------------------------

    /// <summary>
    /// Web Intelligence raporunu hedef klasöre yeni adla kopyalar (Raylight):
    ///   POST /raylight/v1/documents?sourceId={id}
    ///   { "document": { "name": "...", "folderId": 123 } }      (doğrulanmalı)
    /// Kopyanın oluştuğu CMS sorgusuyla doğrulanır. Kopya yeni ID/CUID alır;
    /// instance'lar ve zamanlamalar kopyalanmaz.
    /// </summary>
    public async Task<CopyResult> CopyToFolderAsync(
        int sourceId, string sourceCuid, string kind, int targetFolderId, string newName, CancellationToken ct = default)
    {
        // Önceki bir çalıştırmada kopya oluşmuş ama orijinal silinememiş olabilir: yeniden kopyalama
        var existing = await FindBackupCopyAsync(sourceCuid, targetFolderId, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Yedek kopya zaten var: kaynak {SourceId} -> {CopyId}", sourceId, existing.NewId);
            return existing with { AlreadyExisted = true };
        }

        if (!ReportKinds.IsCopyable(kind))
            throw new AppValidationException($"{ReportKinds.GetDisplayName(kind)} raporları REST ile kopyalanamıyor.");

        await _client.PostJsonAsync($"raylight/v1/documents?sourceId={sourceId}",
            new { document = new { name = newName, folderId = targetFolderId } }, ct);

        var copy = await FindBackupCopyAsync(sourceCuid, targetFolderId, ct)
            ?? throw new BoOperationException(
                "Kopyalama isteği başarılı döndü ancak kopya yedek klasöründe bulunamadı.");

        _logger.LogInformation("Rapor kopyalandı: {SourceId} -> {CopyId} '{Name}' (klasör {Folder})",
            sourceId, copy.NewId, copy.NewName, targetFolderId);

        return copy;
    }

    /// <summary>Hedef klasörde adı "_{cuid}" ile biten (orijinalin kendisi olmayan) rapor.</summary>
    private async Task<CopyResult?> FindBackupCopyAsync(string sourceCuid, int targetFolderId, CancellationToken ct)
    {
        var suffix = "_" + sourceCuid;
        var query = "SELECT SI_ID, SI_CUID, SI_NAME FROM CI_INFOOBJECTS " +
                    $"WHERE SI_PARENTID = {targetFolderId} AND SI_INSTANCE = 0 " +
                    $"AND SI_NAME LIKE '%{CmsQueryBuilder.Sanitize(sourceCuid)}'";

        var entries = await _client.CmsQueryAllAsync(query, pageSize: 50, maxPages: 2, ct: ct);

        var match = entries.FirstOrDefault(e =>
            (e.GetString("SI_NAME") ?? string.Empty).EndsWith(suffix, StringComparison.Ordinal)
            && !string.Equals(e.GetString("SI_CUID"), sourceCuid, StringComparison.Ordinal));

        return match is null
            ? null
            : new CopyResult(match.GetInt("SI_ID") ?? 0, match.GetString("SI_CUID"), match.GetString("SI_NAME") ?? string.Empty, false);
    }

    // ------------------------------------------------------------------
    // Klasörler
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<FolderDto>> GetFoldersAsync(CancellationToken ct = default) =>
        (await _folders.GetTreeAsync(ct)).GetAll();

    public async Task<FolderDto?> GetArchiveFolderAsync(CancellationToken ct = default)
    {
        var archiveId = Env.ArchiveFolderId;
        if (archiveId <= 0) return null;

        var tree = await _folders.GetTreeAsync(ct);
        return tree.GetFolder(archiveId)
            ?? throw new BoOperationException(
                $"Arşiv klasörü BO'da bulunamadı ({_client.EnvironmentKey} ortamı, ArchiveFolderId = {archiveId}).");
    }

    public async Task<FolderDto?> GetFolderAsync(int folderId, CancellationToken ct = default)
    {
        if (folderId <= 0) return null;
        var tree = await _folders.GetTreeAsync(ct);
        return tree.GetFolder(folderId);
    }

    // ------------------------------------------------------------------

    private async Task<CmsEntry?> GetObjectAsync(int id, CancellationToken ct)
    {
        var result = await _client.CmsQueryAsync(
            $"SELECT SI_ID, SI_NAME, SI_PARENTID FROM CI_INFOOBJECTS WHERE SI_ID = {id}", 1, 1, ct);
        return result.Entries.FirstOrDefault();
    }

    private static ReportListItemDto Map(CmsEntry e, FolderTree tree)
    {
        var kind = e.GetString("SI_KIND") ?? string.Empty;
        var parentId = e.GetInt("SI_PARENTID") ?? 0;

        return new ReportListItemDto(
            Id: e.GetInt("SI_ID") ?? 0,
            Name: e.GetString("SI_NAME") ?? string.Empty,
            Kind: kind,
            KindDisplayName: ReportKinds.GetDisplayName(kind),
            Owner: e.GetString("SI_OWNER"),
            ParentFolderId: parentId,
            FolderPath: tree.GetPath(parentId),
            UpdatedAt: e.GetDateTime("SI_UPDATE_TS"),
            Cuid: e.GetString("SI_CUID"));
    }
}
