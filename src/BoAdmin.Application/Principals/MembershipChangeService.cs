using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Application.Principals;

public interface IMembershipChangeService
{
    Task<MembershipChangePreviewDto> PrepareAsync(MemberType type, IReadOnlyList<MembershipChangeRow> rows,
        IReadOnlyList<PrincipalRowIssue> fileIssues, IOperationProgress? progress = null, CancellationToken ct = default);

    Task<IReadOnlyList<MembershipChangeResult>> ExecuteAsync(MemberType type, IReadOnlyList<MembershipChangeRequest> requests,
        int confirmationCount, IOperationProgress? progress = null);
}

/// <summary>
/// Excel ile toplu grup üyeliği değişikliği:
///  MemberType.User : gruba kullanıcı ekleme / çıkarma
///  MemberType.Group: gruba alt grup ekleme / çıkarma
/// Önizlemede mevcut üyelik kontrol edilir; zaten üye olan eklenmez, üye olmayan çıkarılmaz.
/// Her değişiklikten sonra üyelik tekrar okunarak doğrulanır.
/// </summary>
public sealed class MembershipChangeService : IMembershipChangeService
{
    public const int MaxBatchSize = 1000;
    public const int PreviewSteps = 3;
    public const int ExecuteSteps = 2;
    private const int LookupChunkSize = 100;

    /// <summary>Üyeliği BO'nun kendisinin yönettiği gruplar.</summary>
    private static readonly HashSet<string> ManagedGroups = new(StringComparer.OrdinalIgnoreCase) { "Everyone" };

    private readonly IBoPrincipalGateway _gateway;
    private readonly IAuditLogService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentEnvironment _environment;
    private readonly ILogger<MembershipChangeService> _logger;

    public MembershipChangeService(IBoPrincipalGateway gateway, IAuditLogService audit, ICurrentUser currentUser,
        ICurrentEnvironment environment, ILogger<MembershipChangeService> logger)
    {
        _gateway = gateway;
        _audit = audit;
        _currentUser = currentUser;
        _environment = environment;
        _logger = logger;
    }

    public async Task<MembershipChangePreviewDto> PrepareAsync(MemberType type, IReadOnlyList<MembershipChangeRow> rows,
        IReadOnlyList<PrincipalRowIssue> fileIssues, IOperationProgress? progress = null, CancellationToken ct = default)
    {
        progress ??= NullOperationProgress.Instance;
        var problems = new List<PrincipalRowIssue>(fileIssues);

        // 1) Satır kontrolü
        var valid = new List<MembershipChangeRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var label = $"{row.GroupName} / {row.MemberName}";
            if (row.Add is null)
            {
                problems.Add(new PrincipalRowIssue(row.RowNumber, label, "İşlem 'Ekle' veya 'Çıkar' olmalı."));
                continue;
            }
            if (type == MemberType.Group && string.Equals(row.GroupName, row.MemberName, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new PrincipalRowIssue(row.RowNumber, label, "Bir grup kendisine alt grup olarak eklenemez."));
                continue;
            }
            if (!seen.Add($"{row.GroupName}\u0001{row.MemberName}"))
            {
                problems.Add(new PrincipalRowIssue(row.RowNumber, label, "Bu grup-üye çifti dosyada daha önce geçiyor; yalnızca ilk satır işlenir."));
                continue;
            }
            valid.Add(row);
        }

        if (valid.Count == 0)
            throw new AppValidationException(problems.Count > 0
                ? $"Dosyada işlenebilecek satır yok ({problems.Count} satır hatalı). İlk hata: satır {problems[0].RowNumber} - {problems[0].Reason}"
                : "Dosyada grup ve üye bilgisi bulunamadı.");

        if (valid.Count > MaxBatchSize)
            throw new AppValidationException($"Tek seferde en fazla {MaxBatchSize} satır işlenebilir. Dosyada: {valid.Count}. Lütfen dosyayı bölün.");

        // 2) Gruplar ve üyeler
        var groupNames = valid.Select(r => r.GroupName).ToList();
        var memberNames = valid.Select(r => r.MemberName).ToList();
        progress.Phase("Gruplar ve üyeler BO'da aranıyor", groupNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                                                            + memberNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var groups = await LookupGroupsAsync(type == MemberType.Group ? groupNames.Concat(memberNames) : groupNames, progress, ct);
        var users = type == MemberType.User
            ? await LookupUsersAsync(memberNames, progress, ct)
            : new Dictionary<string, List<UserListItemDto>>(StringComparer.OrdinalIgnoreCase);

        // 3) Mevcut üyelikler: üye başına bir sorgu (üyenin bağlı olduğu gruplar)
        var pending = new List<(MembershipChangeRow Row, GroupListItemDto Group, int MemberId, string MemberName, string? Detail)>();
        foreach (var row in valid)
        {
            var label = $"{row.GroupName} / {row.MemberName}";

            if (!TryResolveGroup(groups, row.GroupName, out var group, out var groupError))
            {
                problems.Add(new PrincipalRowIssue(row.RowNumber, label, groupError!));
                continue;
            }

            if (type == MemberType.User)
            {
                if (!users.TryGetValue(row.MemberName, out var matches) || matches.Count == 0)
                {
                    problems.Add(new PrincipalRowIssue(row.RowNumber, label, "Bu ortamda böyle bir kullanıcı hesabı yok."));
                    continue;
                }
                var user = matches[0];
                pending.Add((row, group!, user.Id, user.Name, user.FullName));
            }
            else
            {
                if (!TryResolveGroup(groups, row.MemberName, out var member, out var memberError))
                {
                    problems.Add(new PrincipalRowIssue(row.RowNumber, label, "Alt grup: " + memberError));
                    continue;
                }
                pending.Add((row, group!, member!.Id, member.Name, member.Description));
            }
        }

        var memberIds = pending.Select(p => p.MemberId).Distinct().ToList();
        progress.Phase("Mevcut üyelikler kontrol ediliyor", memberIds.Count);
        var memberships = new Dictionary<int, HashSet<int>>();
        foreach (var id in memberIds)
        {
            ct.ThrowIfCancellationRequested();
            memberships[id] = await ParentGroupIdsAsync(type, id, ct);
            progress.Step();
        }

        var items = new List<MembershipChangeItem>();
        foreach (var (row, group, memberId, memberName, detail) in pending)
        {
            var add = row.Add!.Value;
            var isMember = memberships[memberId].Contains(group.Id);
            string? reason = null;

            if (ManagedGroups.Contains(group.Name))
                reason = $"'{group.Name}' grubunun üyeliğini BO kendisi yönetir; değiştirilemez.";
            else if (type == MemberType.Group && memberId == group.Id)
                reason = "Bir grup kendisine alt grup olarak eklenemez.";
            else if (add && isMember)
                reason = "Zaten bu grubun üyesi.";
            else if (!add && !isMember)
                reason = "Bu grubun doğrudan üyesi değil.";

            items.Add(new MembershipChangeItem(row.RowNumber, group, memberId, memberName, detail, add, reason is null, reason));
        }

        return new MembershipChangePreviewDto(type, items, problems.OrderBy(p => p.RowNumber).ToList());
    }

    public async Task<IReadOnlyList<MembershipChangeResult>> ExecuteAsync(MemberType type, IReadOnlyList<MembershipChangeRequest> requests,
        int confirmationCount, IOperationProgress? progress = null)
    {
        progress ??= NullOperationProgress.Instance;
        var ct = CancellationToken.None; // yarıda kalmasın

        var distinct = requests.GroupBy(r => (r.GroupId, r.MemberId)).Select(g => g.First()).ToList();
        if (distinct.Count == 0) throw new AppValidationException("İşlenecek üyelik değişikliği yok.");
        if (distinct.Count > MaxBatchSize) throw new AppValidationException($"Tek seferde en fazla {MaxBatchSize} değişiklik yapılabilir.");
        if (confirmationCount != distinct.Count)
            throw new AppValidationException($"Onay sayısı ({confirmationCount}) değişiklik sayısıyla ({distinct.Count}) eşleşmiyor.");

        var batchId = Guid.NewGuid();
        _logger.LogInformation("Toplu üyelik değişikliği başladı ({Type}). Batch: {BatchId}, Ortam: {Environment}, Kullanıcı: {User}, Adet: {Count}",
            type, batchId, _environment.Key, _currentUser.UserName, distinct.Count);

        progress.Phase("Hazırlanıyor");
        progress.Phase("Üyelikler güncelleniyor", distinct.Count);
        var results = new List<MembershipChangeResult>(distinct.Count);

        foreach (var r in distinct)
        {
            var operation = MembershipOperations.Of(type, r.Add);
            progress.Current($"{r.GroupName} / {r.MemberName}");
            MembershipChangeResult result;

            try
            {
                if (ManagedGroups.Contains(r.GroupName))
                    throw new AppValidationException($"'{r.GroupName}' grubunun üyeliğini BO kendisi yönetir; değiştirilemez.");

                var before = (await ParentGroupIdsAsync(type, r.MemberId, ct)).Contains(r.GroupId);
                if (before == r.Add)
                {
                    result = new MembershipChangeResult(r.GroupName, r.MemberName, operation, true,
                        r.Add ? "Değişiklik gerekmedi; zaten üye." : "Değişiklik gerekmedi; zaten üye değil.");
                }
                else
                {
                    if (r.Add) await _gateway.AddMemberAsync(r.GroupId, type, r.MemberId, ct);
                    else await _gateway.RemoveMemberAsync(r.GroupId, type, r.MemberId, ct);

                    var after = (await ParentGroupIdsAsync(type, r.MemberId, ct)).Contains(r.GroupId);
                    if (after != r.Add)
                        throw new BoOperationException("İstek kabul edildi ama BO'daki üyelik değişmedi.");

                    result = new MembershipChangeResult(r.GroupName, r.MemberName, operation, true,
                        r.Add ? "Gruba eklendi." : "Gruptan çıkarıldı.");
                }
            }
            catch (Exception ex) when (ex is BoException or AppValidationException)
            {
                _logger.LogWarning(ex, "Üyelik değişikliği başarısız ({Operation}): grup {GroupId}, üye {MemberId}", operation, r.GroupId, r.MemberId);
                var message = ex is BoException bo ? bo.UserMessage : ((AppValidationException)ex).UserMessage;
                result = new MembershipChangeResult(r.GroupName, r.MemberName, operation, false, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Üyelik değişikliğinde beklenmeyen hata ({Operation}): grup {GroupId}, üye {MemberId}", operation, r.GroupId, r.MemberId);
                result = new MembershipChangeResult(r.GroupName, r.MemberName, operation, false, $"Beklenmeyen hata: {ex.Message}");
            }

            results.Add(result);
            await _audit.WriteAsync(new AuditEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Environment = _environment.Key,
                UserName = _currentUser.UserName,
                DisplayName = _currentUser.DisplayName,
                Module = "Groups",
                Operation = operation,
                ObjectId = r.MemberId,
                ObjectName = r.MemberName,
                ObjectKind = type == MemberType.User ? "User" : "UserGroup",
                TargetPath = r.GroupName,
                Success = result.Success,
                Message = result.Message,
                BatchId = batchId
            });
            progress.Step($"{r.GroupName} / {r.MemberName}", result.Success);
        }

        _logger.LogInformation("Toplu üyelik değişikliği bitti. Batch: {BatchId}, Başarılı: {Ok}, Başarısız: {Fail}",
            batchId, results.Count(x => x.Success), results.Count(x => !x.Success));
        return results;
    }

    // ------------------------------------------------------------------

    private async Task<HashSet<int>> ParentGroupIdsAsync(MemberType type, int memberId, CancellationToken ct)
    {
        var parents = type == MemberType.User
            ? await _gateway.GetGroupsOfUserAsync(memberId, ct)
            : await _gateway.GetParentGroupsAsync(memberId, ct);
        return parents.Select(g => g.Id).ToHashSet();
    }

    private async Task<Dictionary<string, List<GroupListItemDto>>> LookupGroupsAsync(IEnumerable<string> names,
        IOperationProgress progress, CancellationToken ct)
    {
        var list = new List<GroupListItemDto>();
        foreach (var chunk in names.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(LookupChunkSize))
        {
            list.AddRange(await _gateway.GetGroupsByNamesAsync(chunk, ct));
            for (var i = 0; i < chunk.Length; i++) progress.Step();
        }
        return list.GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, List<UserListItemDto>>> LookupUsersAsync(IEnumerable<string> names,
        IOperationProgress progress, CancellationToken ct)
    {
        var list = new List<UserListItemDto>();
        foreach (var chunk in names.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(LookupChunkSize))
        {
            list.AddRange(await _gateway.GetUsersByNamesAsync(chunk, ct));
            for (var i = 0; i < chunk.Length; i++) progress.Step();
        }
        return list.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryResolveGroup(Dictionary<string, List<GroupListItemDto>> groups, string name,
        out GroupListItemDto? group, out string? error)
    {
        group = null;
        error = null;
        if (!groups.TryGetValue(name, out var matches) || matches.Count == 0)
        {
            error = $"'{name}' adında grup bu ortamda yok.";
            return false;
        }
        if (matches.Count > 1)
        {
            error = $"'{name}' adında birden fazla grup var ({string.Join(", ", matches.Select(m => m.Id))}); hangisi olduğu belirlenemedi.";
            return false;
        }
        group = matches[0];
        return true;
    }
}
