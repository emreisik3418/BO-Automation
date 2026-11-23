using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;

namespace BoAdmin.Application.Principals;

public interface IPrincipalService
{
    Task<PagedResult<UserListItemDto>> SearchUsersAsync(UserSearchCriteria criteria, CancellationToken ct = default);
    Task<IReadOnlyList<UserListItemDto>> ExportUsersAsync(UserSearchCriteria criteria, CancellationToken ct = default);
    Task<UserDetailDto?> GetUserDetailAsync(int id, CancellationToken ct = default);

    Task<PagedResult<GroupListItemDto>> SearchGroupsAsync(GroupSearchCriteria criteria, CancellationToken ct = default);
    Task<GroupDetailDto?> GetGroupDetailAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Üyelik raporu. <paramref name="groupId"/> verilirse yalnızca o grup, verilmezse adı
    /// <paramref name="text"/> filtresine uyan gruplar (en fazla MaxReportGroups).
    /// </summary>
    Task<MembershipReport> BuildMembershipReportAsync(int? groupId, string? text, bool includeNested,
        IOperationProgress? progress = null, CancellationToken ct = default);
}

public sealed class PrincipalService : IPrincipalService
{
    public const int MaxUserExport = 20_000;
    public const int MaxDetailUsers = 500;
    public const int MaxReportGroups = 500;
    public const int MaxUsersPerGroup = 50_000;

    /// <summary>Alt gruplar gezilirken en fazla bu kadar grup açılır (döngüye karşı da koruma).</summary>
    public const int MaxNestedGroups = 2_000;

    public const int MembershipReportSteps = 2;

    private readonly IBoPrincipalGateway _gateway;

    public PrincipalService(IBoPrincipalGateway gateway) => _gateway = gateway;

    // ------------------------------------------------------------------
    // Kullanıcılar
    // ------------------------------------------------------------------

    public Task<PagedResult<UserListItemDto>> SearchUsersAsync(UserSearchCriteria criteria, CancellationToken ct = default)
    {
        criteria.Normalize();
        return _gateway.SearchUsersAsync(criteria, ct);
    }

    public Task<IReadOnlyList<UserListItemDto>> ExportUsersAsync(UserSearchCriteria criteria, CancellationToken ct = default)
    {
        criteria.Normalize();
        return _gateway.GetAllUsersAsync(criteria, MaxUserExport, ct);
    }

    public async Task<UserDetailDto?> GetUserDetailAsync(int id, CancellationToken ct = default)
    {
        var user = await _gateway.GetUserAsync(id, ct);
        if (user is null) return null;

        var groups = await _gateway.GetGroupsOfUserAsync(id, ct);
        return new UserDetailDto(user, groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    // ------------------------------------------------------------------
    // Gruplar
    // ------------------------------------------------------------------

    public Task<PagedResult<GroupListItemDto>> SearchGroupsAsync(GroupSearchCriteria criteria, CancellationToken ct = default)
    {
        criteria.Normalize();
        return _gateway.SearchGroupsAsync(criteria, ct);
    }

    public async Task<GroupDetailDto?> GetGroupDetailAsync(int id, CancellationToken ct = default)
    {
        var group = await _gateway.GetGroupAsync(id, ct);
        if (group is null) return null;

        var parents = await _gateway.GetParentGroupsAsync(id, ct);
        var subgroups = await _gateway.GetSubgroupsAsync(id, ct);
        var (users, truncated) = await _gateway.GetGroupUsersAsync(id, MaxDetailUsers, ct);

        return new GroupDetailDto(group, SortGroups(parents), SortGroups(subgroups),
            users.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), truncated);
    }

    public async Task<MembershipReport> BuildMembershipReportAsync(int? groupId, string? text, bool includeNested,
        IOperationProgress? progress = null, CancellationToken ct = default)
    {
        progress ??= NullOperationProgress.Instance;
        var warnings = new List<string>();

        // 1) Gruplar
        progress.Phase("Gruplar bulunuyor");
        IReadOnlyList<GroupListItemDto> roots;
        if (groupId is > 0)
        {
            var group = await _gateway.GetGroupAsync(groupId.Value, ct)
                        ?? throw new AppValidationException("Grup bulunamadı.");
            roots = new[] { group };
        }
        else
        {
            roots = await _gateway.GetAllGroupsAsync(text, MaxReportGroups + 1, ct);
            if (roots.Count > MaxReportGroups)
            {
                roots = roots.Take(MaxReportGroups).ToList();
                warnings.Add($"Filtreye uyan grup sayısı {MaxReportGroups}'ü aşıyor; ilk {MaxReportGroups} grup rapora alındı. Filtreyi daraltın.");
            }
        }

        if (roots.Count == 0)
            throw new AppValidationException("Filtreye uyan grup bulunamadı.");

        // 2) Üyeler
        progress.Phase("Üyeler okunuyor", roots.Count);
        var rows = new List<MembershipRow>();
        var opened = 0;

        foreach (var root in SortGroups(roots))
        {
            ct.ThrowIfCancellationRequested();
            progress.Current(root.Name);

            // Alt grupları gezmek için kuyruk: (grup, yol). Aynı grup bir kökte iki kez açılmaz.
            var visited = new HashSet<int>();
            var queue = new Queue<(GroupListItemDto Group, string Path)>();
            queue.Enqueue((root, root.Name));

            while (queue.Count > 0)
            {
                var (group, path) = queue.Dequeue();
                if (!visited.Add(group.Id)) continue;

                if (++opened > MaxNestedGroups)
                {
                    warnings.Add($"Alt gruplarla birlikte {MaxNestedGroups} grup sınırına ulaşıldı; rapor eksik olabilir.");
                    queue.Clear();
                    break;
                }

                var (users, truncated) = await _gateway.GetGroupUsersAsync(group.Id, MaxUsersPerGroup, ct);
                if (truncated)
                    warnings.Add($"'{path}' grubunda {MaxUsersPerGroup}'den fazla kullanıcı var; ilk {MaxUsersPerGroup} kullanıcı alındı.");

                foreach (var u in users.OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase))
                    rows.Add(new MembershipRow(root.Name, path, "Kullanıcı", u.Name, u.FullName, u.Email, u.Disabled, u.LastLogon));

                var subgroups = await _gateway.GetSubgroupsAsync(group.Id, ct);
                foreach (var sub in SortGroups(subgroups))
                {
                    rows.Add(new MembershipRow(root.Name, path, "Grup", sub.Name, null, null, null, null));
                    if (includeNested) queue.Enqueue((sub, $"{path} > {sub.Name}"));
                }
            }

            progress.Step(root.Name);
            if (opened > MaxNestedGroups) break;
        }

        return new MembershipReport(rows, roots.Count, includeNested, warnings.Distinct().ToList());
    }

    private static List<GroupListItemDto> SortGroups(IEnumerable<GroupListItemDto> groups) =>
        groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}
