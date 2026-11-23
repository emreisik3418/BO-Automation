using System.Globalization;
using System.Text.Json;
using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using BoAdmin.Application.Principals;
using BoAdmin.Infrastructure.Bo.Models;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// Kullanıcı ve grupları CMS sorgusuyla okur (CI_SYSTEMOBJECTS).
/// Üyelikler ilişki sorgusuyla bulunur:
///   Grubun kullanıcıları : CHILDREN("SI_NAME='UserGroup-User'", "SI_ID=grup")
///   Grubun alt grupları  : CHILDREN("SI_NAME='UserGroup-UserGroup'", "SI_ID=grup")
///   Grubun üst grupları  : PARENTS ("SI_NAME='UserGroup-UserGroup'", "SI_ID=grup")
///   Kullanıcının grupları: PARENTS ("SI_NAME='UserGroup-User'", "SI_ID=kullanıcı")
/// İlişki sorgusu hata verirse nesnenin üyelik alanlarından (SI_GROUP_MEMBERS, SI_SUBGROUPS,
/// SI_USERGROUPS) ID listesi okunarak aynı sonuç elde edilir.
/// </summary>
public sealed class BoPrincipalGateway : IBoPrincipalGateway
{
    private const string UserFields =
        "SI_ID, SI_CUID, SI_NAME, SI_USERFULLNAME, SI_EMAIL_ADDRESS, SI_DISABLED, SI_LASTLOGONTIME, SI_CREATION_TIME, SI_DESCRIPTION";

    private const string GroupFields = "SI_ID, SI_CUID, SI_NAME, SI_DESCRIPTION, SI_CREATION_TIME";

    private const string UserKind = "SI_KIND = 'User'";
    private const string GroupKind = "SI_KIND = 'UserGroup'";

    private const string RelUserGroupUser = "UserGroup-User";
    private const string RelUserGroupGroup = "UserGroup-UserGroup";

    private const int InClauseChunkSize = 100;
    private const int QueryPageSize = 500;

    private readonly BoRestClient _client;
    private readonly ILogger<BoPrincipalGateway> _logger;

    public BoPrincipalGateway(BoRestClient client, ILogger<BoPrincipalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Kullanıcılar
    // ------------------------------------------------------------------

    public async Task<PagedResult<UserListItemDto>> SearchUsersAsync(UserSearchCriteria c, CancellationToken ct = default)
    {
        var result = await _client.CmsQueryAsync(BuildUserQuery(c), c.Page, c.PageSize, ct);
        return new PagedResult<UserListItemDto>(result.Entries.Select(MapUser).ToList(), c.Page, c.PageSize, result.HasMore);
    }

    public async Task<IReadOnlyList<UserListItemDto>> GetAllUsersAsync(UserSearchCriteria c, int max, CancellationToken ct = default)
    {
        var entries = await _client.CmsQueryAllAsync(BuildUserQuery(c), QueryPageSize,
            maxPages: Math.Max(1, (int)Math.Ceiling(max / (double)QueryPageSize)), ct: ct);
        return entries.Take(max).Select(MapUser).ToList();
    }

    public async Task<UserListItemDto?> GetUserAsync(int id, CancellationToken ct = default)
    {
        var result = await _client.CmsQueryAsync(
            $"SELECT {UserFields} FROM CI_SYSTEMOBJECTS WHERE {UserKind} AND SI_ID = {Id(id)}", 1, 1, ct);
        return result.Entries.Select(MapUser).FirstOrDefault();
    }

    public async Task<IReadOnlyList<GroupListItemDto>> GetGroupsOfUserAsync(int userId, CancellationToken ct = default)
    {
        var entries = await RelatedAsync("PARENTS", RelUserGroupUser, userId, GroupFields, GroupKind, "SI_USERGROUPS", int.MaxValue, ct);
        return entries.Items.Select(MapGroup).ToList();
    }

    private static string BuildUserQuery(UserSearchCriteria c)
    {
        var where = new List<string> { UserKind };

        if (!string.IsNullOrWhiteSpace(c.Text))
        {
            where.Add("(" + string.Join(" OR ",
                CmsQueryBuilder.LikeContains("SI_NAME", c.Text),
                CmsQueryBuilder.LikeContains("SI_USERFULLNAME", c.Text),
                CmsQueryBuilder.LikeContains("SI_EMAIL_ADDRESS", c.Text)) + ")");
        }

        if (c.Status == UserStatusFilter.Active) where.Add("SI_DISABLED = 0");
        else if (c.Status == UserStatusFilter.Disabled) where.Add("SI_DISABLED = 1");

        if (c.LastLogonBefore is not null)
            where.Add($"SI_LASTLOGONTIME < {CmsQueryBuilder.DateLiteral(c.LastLogonBefore.Value.Date)}");

        var orderField = c.SortBy switch
        {
            UserSortField.FullName => "SI_USERFULLNAME",
            UserSortField.LastLogon => "SI_LASTLOGONTIME",
            UserSortField.Created => "SI_CREATION_TIME",
            _ => "SI_NAME"
        };

        return $"SELECT {UserFields} FROM CI_SYSTEMOBJECTS WHERE {string.Join(" AND ", where)} " +
               $"ORDER BY {orderField} {(c.SortDescending ? "DESC" : "ASC")}";
    }

    // ------------------------------------------------------------------
    // Gruplar
    // ------------------------------------------------------------------

    public async Task<PagedResult<GroupListItemDto>> SearchGroupsAsync(GroupSearchCriteria c, CancellationToken ct = default)
    {
        var result = await _client.CmsQueryAsync(BuildGroupQuery(c.Text), c.Page, c.PageSize, ct);
        return new PagedResult<GroupListItemDto>(result.Entries.Select(MapGroup).ToList(), c.Page, c.PageSize, result.HasMore);
    }

    public async Task<IReadOnlyList<GroupListItemDto>> GetAllGroupsAsync(string? text, int max, CancellationToken ct = default)
    {
        var entries = await _client.CmsQueryAllAsync(BuildGroupQuery(text), QueryPageSize,
            maxPages: Math.Max(1, (int)Math.Ceiling(max / (double)QueryPageSize)), ct: ct);
        return entries.Take(max).Select(MapGroup).ToList();
    }

    public async Task<GroupListItemDto?> GetGroupAsync(int id, CancellationToken ct = default)
    {
        var result = await _client.CmsQueryAsync(
            $"SELECT {GroupFields} FROM CI_SYSTEMOBJECTS WHERE {GroupKind} AND SI_ID = {Id(id)}", 1, 1, ct);
        return result.Entries.Select(MapGroup).FirstOrDefault();
    }

    public async Task<(IReadOnlyList<UserListItemDto> Users, bool Truncated)> GetGroupUsersAsync(int groupId, int max, CancellationToken ct = default)
    {
        var related = await RelatedAsync("CHILDREN", RelUserGroupUser, groupId, UserFields, UserKind, "SI_GROUP_MEMBERS", max, ct);
        return (related.Items.Select(MapUser).ToList(), related.Truncated);
    }

    public async Task<IReadOnlyList<GroupListItemDto>> GetSubgroupsAsync(int groupId, CancellationToken ct = default)
    {
        var related = await RelatedAsync("CHILDREN", RelUserGroupGroup, groupId, GroupFields, GroupKind, "SI_SUBGROUPS", int.MaxValue, ct);
        return related.Items.Where(e => e.GetInt("SI_ID") != groupId).Select(MapGroup).ToList();
    }

    public async Task<IReadOnlyList<GroupListItemDto>> GetParentGroupsAsync(int groupId, CancellationToken ct = default)
    {
        var related = await RelatedAsync("PARENTS", RelUserGroupGroup, groupId, GroupFields, GroupKind, "SI_USERGROUPS", int.MaxValue, ct);
        return related.Items.Where(e => e.GetInt("SI_ID") != groupId).Select(MapGroup).ToList();
    }

    private static string BuildGroupQuery(string? text)
    {
        var where = new List<string> { GroupKind };
        if (!string.IsNullOrWhiteSpace(text))
            where.Add(CmsQueryBuilder.LikeContains("SI_NAME", text));

        return $"SELECT {GroupFields} FROM CI_SYSTEMOBJECTS WHERE {string.Join(" AND ", where)} ORDER BY SI_NAME ASC";
    }

    // ------------------------------------------------------------------
    // İlişki sorgusu (+ yedek yöntem)
    // ------------------------------------------------------------------

    /// <summary>Hangi yöntemin çalıştığı ortam bazında hatırlanır; yedek yöntem gerekiyorsa ilişki sorgusu tekrar denenmez.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> UseBagFallback = new(StringComparer.OrdinalIgnoreCase);

    private async Task<(IReadOnlyList<CmsEntry> Items, bool Truncated)> RelatedAsync(
        string function, string relation, int id, string fields, string kindClause, string bagField, int max, CancellationToken ct)
    {
        var memoryKey = $"{_client.EnvironmentKey}|{function}|{relation}";

        if (!UseBagFallback.ContainsKey(memoryKey))
        {
            var query = $"SELECT {fields} FROM CI_SYSTEMOBJECTS " +
                        $"WHERE {function}(\"SI_NAME='{relation}'\", \"SI_ID={Id(id)}\") AND {kindClause} ORDER BY SI_NAME ASC";
            try
            {
                return await QueryLimitedAsync(query, max, ct);
            }
            catch (BoException ex) when (ex is not BoAuthenticationException)
            {
                _logger.LogWarning(ex, "İlişki sorgusu çalışmadı ({Function} {Relation}); üyelik alanı ({Bag}) ile devam ediliyor.",
                    function, relation, bagField);
                UseBagFallback[memoryKey] = true;
            }
        }

        return await RelatedFromBagAsync(id, fields, kindClause, bagField, max, ct);
    }

    /// <summary>Yedek yöntem: nesnenin üyelik alanındaki ID'leri okuyup o nesneleri getirir.</summary>
    private async Task<(IReadOnlyList<CmsEntry> Items, bool Truncated)> RelatedFromBagAsync(
        int id, string fields, string kindClause, string bagField, int max, CancellationToken ct)
    {
        var owner = await _client.CmsQueryAsync($"SELECT SI_ID, {bagField} FROM CI_SYSTEMOBJECTS WHERE SI_ID = {Id(id)}", 1, 1, ct);
        var entry = owner.Entries.FirstOrDefault();
        if (entry is null || !entry.Properties.TryGetValue(bagField, out var bag))
            return (Array.Empty<CmsEntry>(), false);

        var ids = ExtractIds(bag).Where(i => i > 0 && i != id).Distinct().ToList();
        var truncated = ids.Count > max;
        if (truncated) ids = ids.Take(max).ToList();

        var list = new List<CmsEntry>();
        foreach (var chunk in ids.Chunk(InClauseChunkSize))
        {
            var query = $"SELECT {fields} FROM CI_SYSTEMOBJECTS WHERE {CmsQueryBuilder.InList("SI_ID", chunk)} AND {kindClause}";
            list.AddRange(await _client.CmsQueryAllAsync(query, InClauseChunkSize, ct: ct));
        }

        return (list, truncated);
    }

    private async Task<(IReadOnlyList<CmsEntry> Items, bool Truncated)> QueryLimitedAsync(string query, int max, CancellationToken ct)
    {
        var list = new List<CmsEntry>();
        for (var page = 1; ; page++)
        {
            var result = await _client.CmsQueryAsync(query, page, QueryPageSize, ct);
            list.AddRange(result.Entries);

            if (list.Count > max) return (list.Take(max).ToList(), true);
            if (!result.HasMore || result.Entries.Count == 0) return (list, false);
        }
    }

    /// <summary>
    /// Üyelik alanı (property bag) farklı biçimlerde gelebilir:
    /// [12, 13] veya { "1": 12, "2": 13, "SI_TOTAL": 2 } ya da iç içe nesneler. Sayısal değerler toplanır;
    /// SI_TOTAL gibi sayaçlar atlanır.
    /// </summary>
    private static IEnumerable<int> ExtractIds(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number when e.TryGetInt32(out var n):
                yield return n;
                break;
            case JsonValueKind.String when int.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s):
                yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var id in ExtractIds(item)) yield return id;
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name.Equals("SI_TOTAL", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var id in ExtractIds(p.Value)) yield return id;
                }
                break;
        }
    }

    // ------------------------------------------------------------------
    // Eşleme
    // ------------------------------------------------------------------

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static UserListItemDto MapUser(CmsEntry e) => new(
        Id: e.GetInt("SI_ID") ?? 0,
        Cuid: e.GetString("SI_CUID"),
        Name: e.GetString("SI_NAME") ?? string.Empty,
        FullName: NullIfEmpty(e.GetString("SI_USERFULLNAME")),
        Email: NullIfEmpty(e.GetString("SI_EMAIL_ADDRESS")),
        Disabled: ReadFlag(e, "SI_DISABLED"),
        LastLogon: e.GetDateTime("SI_LASTLOGONTIME"),
        CreatedAt: e.GetDateTime("SI_CREATION_TIME"),
        Description: NullIfEmpty(e.GetString("SI_DESCRIPTION")));

    private static GroupListItemDto MapGroup(CmsEntry e) => new(
        Id: e.GetInt("SI_ID") ?? 0,
        Cuid: e.GetString("SI_CUID"),
        Name: e.GetString("SI_NAME") ?? string.Empty,
        Description: NullIfEmpty(e.GetString("SI_DESCRIPTION")),
        CreatedAt: e.GetDateTime("SI_CREATION_TIME"));

    /// <summary>true/false, 1/0 veya "true"/"1" biçimlerini okur.</summary>
    private static bool ReadFlag(CmsEntry e, string name)
    {
        if (e.GetBool(name) is { } b) return b;
        if (e.GetInt(name) is { } n) return n != 0;
        return string.Equals(e.GetString(name), "1", StringComparison.Ordinal);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
