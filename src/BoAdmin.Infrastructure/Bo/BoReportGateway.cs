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

    /// <summary>Ortam bazında çalıştığı görülen silme uç noktası (gereksiz 404 denemelerini önler).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BoDeleteStrategy> WorkingDeleteStrategy =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<DeleteOutcome> DeleteObjectAsync(int id, string kind, int parentId, CancellationToken ct = default)
    {
        var environment = _client.EnvironmentKey;
        var isWebi = string.Equals(kind, ReportKinds.Webi, StringComparison.OrdinalIgnoreCase);

        // Deneme sırası: daha önce çalışan -> config'teki -> diğerleri
        var order = new List<BoDeleteStrategy>();
        if (WorkingDeleteStrategy.TryGetValue(environment, out var known)) order.Add(known);
        order.Add(Env.DeleteStrategy);
        order.AddRange(new[] { BoDeleteStrategy.Documents, BoDeleteStrategy.InfoObjects, BoDeleteStrategy.Raylight });
        order = order.Distinct().Where(s => s != BoDeleteStrategy.Raylight || isWebi).ToList();

        BoNotFoundException? lastNotFound = null;
        BoDeleteStrategy? used = null;

        foreach (var strategy in order)
        {
            var path = strategy switch
            {
                BoDeleteStrategy.Documents => $"v1/documents/{id}",
                BoDeleteStrategy.Raylight => $"raylight/v1/documents/{id}",
                _ => $"v1/infoobjects/{id}"
            };

            try
            {
                await _client.DeleteAsync(path, ct);
                used = strategy;
                break;
            }
            catch (BoNotFoundException ex)
            {
                // Uç nokta yok ya da nesne zaten yok: sıradakini dene
                _logger.LogInformation("Silme uç noktası 404 döndü: DELETE {Path}. Sıradaki deneniyor.", path);
                lastNotFound = ex;
            }
        }

        if (used is null)
        {
            // Hiçbir uç nokta silemedi: nesne gerçekten yok mu?
            if (await GetObjectAsync(id, ct) is null)
            {
                _logger.LogInformation("Nesne zaten yok: {Id}", id);
                return DeleteOutcome.Deleted;
            }

            throw lastNotFound ?? new BoNotFoundException($"Silme uç noktası bulunamadı: {id}");
        }

        WorkingDeleteStrategy[environment] = used.Value;
        var strategyUsed = used.Value;

        // Doğrulama
        var after = await GetObjectAsync(id, ct);
        if (after is null)
        {
            _logger.LogInformation("Nesne silindi: {Id} ({Strategy})", id, strategyUsed);
            return DeleteOutcome.Deleted;
        }

        var newParent = after.GetInt("SI_PARENTID");
        if (newParent is not null && newParent != parentId)
        {
            _logger.LogInformation("Nesne silindi, geri dönüşüm kutusuna alındı: {Id} (yeni üst: {Parent})", id, newParent);
            return DeleteOutcome.MovedToRecycleBin;
        }

        throw new BoOperationException(
            $"Silme isteği başarılı döndü ancak nesne hâlâ BO'da duruyor ({strategyUsed}).");
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

        var body = new System.Xml.Linq.XElement("document",
                new System.Xml.Linq.XElement("name", name),
                new System.Xml.Linq.XElement("folderId", targetFolderId))
            .ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        await _client.PostXmlAsync($"raylight/v1/documents?sourceId={id}", body, ct);

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
    // Ad güncelleme (Excel ile toplu)
    // ------------------------------------------------------------------

    /// <summary>Ortam bazında çalıştığı görülen ad güncelleme uç noktası.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> WorkingRenameEndpoint =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ad güncelleme için denenen uç noktalar (sırayla; çalışan ortam bazında hatırlanır):
    ///   1) GET + PUT /v1/documents/{id}    (nesnenin JSON'u okunur, "name" değiştirilip geri yazılır)
    ///   2) GET + PUT /v1/infoobjects/{id}
    /// Her denemeden sonra yeni ad CMS sorgusuyla doğrulanır; değişmediyse sıradaki denenir.
    /// </summary>
    public async Task RenameAsync(int id, string newName, CancellationToken ct = default)
    {
        var environment = _client.EnvironmentKey;
        var endpoints = new List<string>();
        if (WorkingRenameEndpoint.TryGetValue(environment, out var known)) endpoints.Add(known);
        endpoints.AddRange(new[] { "v1/documents", "v1/infoobjects" });

        var errors = new List<string>();

        foreach (var endpoint in endpoints.Distinct())
        {
            var path = $"{endpoint}/{id}";
            try
            {
                var current = await _client.GetJsonAsync(path, ct);
                var body = BuildRenameBody(current, newName);
                if (body is null)
                {
                    errors.Add($"{endpoint}: yanıtta 'name' alanı yok");
                    continue;
                }

                await _client.PutJsonAsync(path, body, ct);
            }
            catch (BoException ex) when (ex is BoNotFoundException or BoOperationException)
            {
                _logger.LogInformation("Ad güncelleme uç noktası başarısız: {Path} ({Message}). Sıradaki deneniyor.", path, ex.Message);
                errors.Add($"{endpoint}: {ex.Message}");
                continue;
            }

            // Doğrulama
            var after = await GetObjectAsync(id, ct);
            var actual = after?.GetString("SI_NAME");
            if (string.Equals(actual, newName, StringComparison.Ordinal))
            {
                WorkingRenameEndpoint[environment] = endpoint;
                _logger.LogInformation("Rapor adı güncellendi: {Id} -> '{Name}' ({Endpoint})", id, newName, endpoint);
                return;
            }

            _logger.LogWarning("Ad güncelleme isteği kabul edildi ama ad değişmedi: {Path}. BO'daki ad: '{Actual}'", path, actual);
            errors.Add($"{endpoint}: istek kabul edildi ama ad değişmedi");
        }

        throw new BoOperationException("Ad güncellenemedi (" + string.Join("; ", errors) + ").");
    }

    /// <summary>
    /// GET yanıtındaki nesnenin "name" alanını değiştirir. Ad kökte veya tek bir sarmalayıcı nesnenin
    /// içinde olabilir ({ "document": { "name": ... } }). "__metadata" gibi salt-okunur alanlar çıkarılır.
    /// </summary>
    private static System.Text.Json.Nodes.JsonObject? BuildRenameBody(System.Text.Json.JsonElement current, string newName)
    {
        if (current.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var root = System.Text.Json.Nodes.JsonNode.Parse(current.GetRawText()) as System.Text.Json.Nodes.JsonObject;
        if (root is null) return null;

        root.Remove("__metadata");

        System.Text.Json.Nodes.JsonObject? target = null;
        var nameKey = root.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, "name", StringComparison.OrdinalIgnoreCase));
        if (nameKey is not null)
        {
            target = root;
        }
        else
        {
            foreach (var (_, value) in root)
            {
                if (value is System.Text.Json.Nodes.JsonObject inner)
                {
                    var innerKey = inner.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, "name", StringComparison.OrdinalIgnoreCase));
                    if (innerKey is not null)
                    {
                        inner.Remove("__metadata");
                        target = inner;
                        nameKey = innerKey;
                        break;
                    }
                }
            }
        }

        if (target is null || nameKey is null) return null;

        target[nameKey] = newName;
        return root;
    }

    // ------------------------------------------------------------------
    // Kopyalama (TXT ile toplu yedekleme)
    // ------------------------------------------------------------------

    /// <summary>
    /// Web Intelligence raporunu hedef klasöre yeni adla kopyalar (Raylight):
    ///   POST /raylight/v1/documents?sourceId={id}
    ///   &lt;document&gt;&lt;name&gt;...&lt;/name&gt;&lt;folderId&gt;123&lt;/folderId&gt;&lt;/document&gt;  (XML gövde)
    /// Kopyanın oluştuğu CMS sorgusuyla doğrulanır. Kopya yeni ID/CUID alır;
    /// instance'lar ve zamanlamalar kopyalanmaz.
    /// </summary>
    public async Task<CopyResult> CopyToFolderAsync(
        int sourceId, string sourceCuid, string kind, int targetFolderId, string newName, CancellationToken ct = default)
    {
        // Önceki bir çalıştırmada kopya oluşmuş ama orijinal silinememiş olabilir: yeniden kopyalama
        var existing = await FindBackupCopyByNameAsync(newName, sourceCuid, targetFolderId, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Yedek kopya zaten var: kaynak {SourceId} -> {CopyId}", sourceId, existing.NewId);
            return existing with { AlreadyExisted = true };
        }

        if (!ReportKinds.IsCopyable(kind))
            throw new AppValidationException($"{ReportKinds.GetDisplayName(kind)} raporları REST ile kopyalanamıyor.");

        // JSON gövde BO tarafından yok sayılıyor (kopya varsayılan adla, kaynak klasöre oluşuyor);
        // SAP örneğindeki XML gövde kullanılır:
        //   <document><name>...</name><folderId>123</folderId></document>
        // Yanıt: { "success": { "message": "... has been successfully created.", "id": "72705341" } }
        var body = new System.Xml.Linq.XElement("document",
                new System.Xml.Linq.XElement("name", newName),
                new System.Xml.Linq.XElement("folderId", targetFolderId))
            .ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

        var response = await _client.PostXmlAsync($"raylight/v1/documents?sourceId={sourceId}", body, ct);

        var newId = TryReadCreatedId(response);
        _logger.LogInformation("Raylight kopyalama yanıtı: yeni ID {NewId}", newId?.ToString() ?? "(okunamadı)");

        CmsEntry? created = null;
        if (newId is > 0)
        {
            // Yeni nesne CMS sorgusuna birkaç saniye geç yansıyabilir: kısa aralıklarla tekrar dene
            for (var attempt = 1; attempt <= 4 && created is null; attempt++)
            {
                created = await GetObjectAsync(newId.Value, ct);
                if (created is null)
                {
                    _logger.LogDebug("Kopya ({NewId}) CMS sorgusunda henüz yok, deneme {Attempt}", newId, attempt);
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
                }
            }

            // Hâlâ yoksa doğrudan Raylight'tan oku
            created ??= await GetRaylightDocumentAsync(newId.Value, ct);
        }

        // Yanıtta ID yoksa ada göre ara (yedek yöntem)
        if (created is null)
        {
            var byName = await FindBackupCopyByNameAsync(newName, sourceCuid, targetFolderId, ct);
            if (byName is not null)
                created = await GetObjectAsync(byName.NewId, ct);
        }

        if (created is null)
            throw new BoOperationException(
                $"Kopyalama isteği başarılı döndü ancak kopya BO'da bulunamadı (yanıttaki ID: {newId?.ToString() ?? "yok"}).");

        var copyId = created.GetInt("SI_ID") ?? 0;
        var parent = created.GetInt("SI_PARENTID");
        var copyName = created.GetString("SI_NAME") ?? newName;

        if (parent != targetFolderId)
        {
            // Yanlış yere oluşan kopyayı geri al (bu işlemde biz oluşturduk, ID BO yanıtından geldi)
            var rolledBack = await TryDeleteStrayCopyAsync(copyId, sourceId, parent ?? 0, ct);
            throw new BoOperationException(
                $"Kopya oluşturuldu (ID: {copyId}, ad: '{copyName}') ancak yedek klasörüne değil, klasör {parent} içine konuldu. " +
                (rolledBack ? "Hatalı kopya geri silindi. " : $"Hatalı kopya silinemedi, lütfen elle silin (ID: {copyId}). ") +
                "Orijinal rapor silinmedi.");
        }

        if (!string.Equals(copyName, newName, StringComparison.Ordinal))
            _logger.LogWarning("Kopyanın adı istenenden farklı. İstenen: '{Requested}', BO: '{Actual}'", newName, copyName);

        _logger.LogInformation("Rapor kopyalandı: {SourceId} -> {CopyId} '{Name}' (klasör {Folder})",
            sourceId, copyId, copyName, targetFolderId);

        return new CopyResult(copyId, created.GetString("SI_CUID"), copyName, false);
    }

    private async Task<bool> TryDeleteStrayCopyAsync(int copyId, int sourceId, int parentId, CancellationToken ct)
    {
        if (copyId <= 0 || copyId == sourceId) return false;
        try
        {
            await DeleteObjectAsync(copyId, ReportKinds.Webi, parentId, ct);
            _logger.LogWarning("Yanlış klasöre oluşan kopya geri silindi: {CopyId} (kaynak {SourceId})", copyId, sourceId);
            return true;
        }
        catch (Exception ex) when (ex is BoException or AppValidationException)
        {
            _logger.LogError(ex, "Yanlış klasöre oluşan kopya silinemedi: {CopyId} (kaynak {SourceId})", copyId, sourceId);
            return false;
        }
    }

    /// <summary>
    /// GET /raylight/v1/documents/{id} -> { "document": { "id", "cuid", "name", "folderId", ... } }
    /// CMS sorgusu sonucu gibi kullanılabilsin diye SI_* alanlarına çevrilir.
    /// </summary>
    private async Task<CmsEntry?> GetRaylightDocumentAsync(int id, CancellationToken ct)
    {
        try
        {
            var json = await _client.GetJsonAsync($"raylight/v1/documents/{id}", ct);
            if (json.ValueKind != System.Text.Json.JsonValueKind.Object
                || !json.TryGetProperty("document", out var doc)
                || doc.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            string? Read(string name)
            {
                foreach (var p in doc.EnumerateObject())
                {
                    if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    return p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
                }
                return null;
            }

            var mapped = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, string?>
            {
                ["SI_ID"] = Read("id") ?? id.ToString(),
                ["SI_CUID"] = Read("cuid"),
                ["SI_NAME"] = Read("name"),
                ["SI_PARENTID"] = Read("folderId")
            });

            _logger.LogInformation("Kopya Raylight'tan okundu: {Json}", doc.GetRawText());
            return new CmsEntry(mapped);
        }
        catch (BoException ex)
        {
            _logger.LogWarning(ex, "Kopya Raylight'tan okunamadı: {Id}", id);
            return null;
        }
    }

    /// <summary>Raylight yanıtından oluşturulan nesnenin ID'sini okur: success.id (metin veya sayı).</summary>
    private static int? TryReadCreatedId(System.Text.Json.JsonElement response)
    {
        if (response.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        foreach (var container in new[] { "success", "document" })
        {
            if (!response.TryGetProperty(container, out var node) || node.ValueKind != System.Text.Json.JsonValueKind.Object)
                continue;

            if (!node.TryGetProperty("id", out var idNode)) continue;

            if (idNode.ValueKind == System.Text.Json.JsonValueKind.Number && idNode.TryGetInt32(out var n)) return n;
            if (idNode.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(idNode.GetString(), out n)) return n;
        }

        return null;
    }

    /// <summary>
    /// Hedef klasörde tam olarak bu adı taşıyan (orijinalin kendisi olmayan) rapor.
    /// CMS sorgusu baştaki % joker karakterini desteklemediği için tam ad eşleşmesi kullanılır.
    /// Ad sorguda güvenle yazılamayan karakter (' " \ ;) içeriyorsa arama yapılmaz.
    /// </summary>
    private async Task<CopyResult?> FindBackupCopyByNameAsync(string name, string sourceCuid, int targetFolderId, CancellationToken ct)
    {
        if (CmsQueryBuilder.Sanitize(name) != name) return null;

        var query = "SELECT SI_ID, SI_CUID, SI_NAME FROM CI_INFOOBJECTS " +
                    $"WHERE SI_PARENTID = {targetFolderId} AND SI_INSTANCE = 0 AND SI_NAME = '{name}'";

        var result = await _client.CmsQueryAsync(query, 1, 10, ct);

        var match = result.Entries.FirstOrDefault(e =>
            string.Equals(e.GetString("SI_NAME"), name, StringComparison.Ordinal)
            && !string.Equals(e.GetString("SI_CUID"), sourceCuid, StringComparison.Ordinal));

        return match is null
            ? null
            : new CopyResult(match.GetInt("SI_ID") ?? 0, match.GetString("SI_CUID"), match.GetString("SI_NAME") ?? name, false);
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
            $"SELECT SI_ID, SI_CUID, SI_NAME, SI_PARENTID FROM CI_INFOOBJECTS WHERE SI_ID = {id}", 1, 1, ct);
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
