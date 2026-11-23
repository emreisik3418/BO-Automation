using BoAdmin.Application.Common;
using BoAdmin.Application.Principals;

namespace BoAdmin.Application.Abstractions;

/// <summary>BO kullanıcı ve grupları (CI_SYSTEMOBJECTS) için okuma işlemleri.</summary>
public interface IBoPrincipalGateway
{
    Task<PagedResult<UserListItemDto>> SearchUsersAsync(UserSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Kriterlere uyan tüm kullanıcılar (Excel için), en fazla <paramref name="max"/>.</summary>
    Task<IReadOnlyList<UserListItemDto>> GetAllUsersAsync(UserSearchCriteria criteria, int max, CancellationToken ct = default);

    Task<UserListItemDto?> GetUserAsync(int id, CancellationToken ct = default);

    /// <summary>Kullanıcının doğrudan üyesi olduğu gruplar.</summary>
    Task<IReadOnlyList<GroupListItemDto>> GetGroupsOfUserAsync(int userId, CancellationToken ct = default);

    Task<PagedResult<GroupListItemDto>> SearchGroupsAsync(GroupSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Adı filtreye uyan tüm gruplar, en fazla <paramref name="max"/>.</summary>
    Task<IReadOnlyList<GroupListItemDto>> GetAllGroupsAsync(string? text, int max, CancellationToken ct = default);

    Task<GroupListItemDto?> GetGroupAsync(int id, CancellationToken ct = default);

    /// <summary>Grubun doğrudan kullanıcı üyeleri. <paramref name="max"/> aşılırsa Truncated = true.</summary>
    Task<(IReadOnlyList<UserListItemDto> Users, bool Truncated)> GetGroupUsersAsync(int groupId, int max, CancellationToken ct = default);

    /// <summary>Grubun doğrudan alt grupları.</summary>
    Task<IReadOnlyList<GroupListItemDto>> GetSubgroupsAsync(int groupId, CancellationToken ct = default);

    /// <summary>Grubun üyesi olduğu üst gruplar.</summary>
    Task<IReadOnlyList<GroupListItemDto>> GetParentGroupsAsync(int groupId, CancellationToken ct = default);

    // ---------------- Değişiklik işlemleri ----------------

    /// <summary>Hesap adlarına (SI_NAME) göre kullanıcılar.</summary>
    Task<IReadOnlyList<UserListItemDto>> GetUsersByNamesAsync(IReadOnlyCollection<string> names, CancellationToken ct = default);

    /// <summary>Kullanıcıları ID ile getirir.</summary>
    Task<IReadOnlyList<UserListItemDto>> GetUsersByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    /// <summary>Seçili ortamda BO'ya bağlanan servis hesabı (kendini pasif yapmasın / silmesin diye).</summary>
    string? ServiceAccountName { get; }

    /// <summary>Kullanıcıyı aktif/pasif yapar ve doğrular (GET + PUT /v1/users/{id}, "disabled" alanı).</summary>
    Task SetUserDisabledAsync(int id, bool disabled, CancellationToken ct = default);

    /// <summary>Kullanıcıyı siler ve silindiğini doğrular (DELETE /v1/users/{id}).</summary>
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}
