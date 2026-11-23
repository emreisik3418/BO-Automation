using BoAdmin.Application.Abstractions;
using BoAdmin.Application.Audit;
using BoAdmin.Application.Common;
using BoAdmin.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Application.Principals;

public interface IUserActionService
{
    Task<UserActionPreviewDto> PrepareAsync(UserActionKind kind, IReadOnlyList<UserActionRow> rows, IReadOnlyList<PrincipalRowIssue> fileIssues,
        IOperationProgress? progress = null, CancellationToken ct = default);

    Task<IReadOnlyList<UserActionResult>> ExecuteAsync(UserActionKind kind, IReadOnlyList<UserActionRequest> requests, int confirmationCount,
        IOperationProgress? progress = null);
}

/// <summary>
/// Excel ile toplu kullanıcı aktif/pasif yapma ve silme.
/// Sistem hesapları ve uygulamanın BO servis hesabı hiçbir zaman işlenmez.
/// </summary>
public sealed class UserActionService : IUserActionService
{
    public const int MaxBatchSize = 500;
    public const int PreviewSteps = 2;
    public const int ExecuteSteps = 2;
    private const string ModuleName = "Users";
    private const int LookupChunkSize = 100;

    /// <summary>BO'nun kendi hesapları: pasif yapılamaz, silinemez.</summary>
    private static readonly HashSet<string> ProtectedAccounts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Administrator", "Guest", "SMAdmin", "QaaWSServletPrincipal"
    };

    private readonly IBoPrincipalGateway _gateway;
    private readonly IAuditLogService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentEnvironment _environment;
    private readonly ILogger<UserActionService> _logger;

    public UserActionService(IBoPrincipalGateway gateway, IAuditLogService audit, ICurrentUser currentUser,
        ICurrentEnvironment environment, ILogger<UserActionService> logger)
    {
        _gateway = gateway;
        _audit = audit;
        _currentUser = currentUser;
        _environment = environment;
        _logger = logger;
    }

    public async Task<UserActionPreviewDto> PrepareAsync(UserActionKind kind, IReadOnlyList<UserActionRow> rows,
        IReadOnlyList<PrincipalRowIssue> fileIssues, IOperationProgress? progress = null, CancellationToken ct = default)
    {
        progress ??= NullOperationProgress.Instance;

        // 1) Satırlar
        var problems = new List<PrincipalRowIssue>(fileIssues);
        var valid = new List<UserActionRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (kind == UserActionKind.SetStatus && row.Disable is null)
            {
                problems.Add(new PrincipalRowIssue(row.RowNumber, row.Account, "İşlem 'Aktif' veya 'Pasif' olmalı."));
                continue;
            }
            if (!seen.Add(row.Account))
            {
                problems.Add(new PrincipalRowIssue(row.RowNumber, row.Account, "Bu hesap dosyada daha önce geçiyor; yalnızca ilk satır işlenir."));
                continue;
            }
            valid.Add(row);
        }

        if (valid.Count == 0)
            throw new AppValidationException(problems.Count > 0
                ? $"Dosyada işlenebilecek satır yok ({problems.Count} satır hatalı). İlk hata: satır {problems[0].RowNumber} - {problems[0].Reason}"
                : "Dosyada kullanıcı hesabı bulunamadı.");

        if (valid.Count > MaxBatchSize)
            throw new AppValidationException($"Tek seferde en fazla {MaxBatchSize} kullanıcı işlenebilir. Dosyada: {valid.Count}. Lütfen dosyayı bölün.");

        // 2) Kullanıcılar
        progress.Phase("Kullanıcılar BO'da aranıyor", valid.Count);
        var users = new List<UserListItemDto>();
        foreach (var chunk in valid.Select(r => r.Account).Chunk(LookupChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            users.AddRange(await _gateway.GetUsersByNamesAsync(chunk, ct));
            for (var i = 0; i < chunk.Length; i++) progress.Step();
        }

        var byName = users.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        progress.Phase("Önizleme hazırlanıyor", valid.Count);
        var items = new List<UserActionPreviewItem>();
        foreach (var row in valid)
        {
            progress.Step();
            if (!byName.TryGetValue(row.Account, out var user))
            {
                problems.Add(new PrincipalRowIssue(row.RowNumber, row.Account, "Bu ortamda böyle bir kullanıcı hesabı yok."));
                continue;
            }

            var blocked = ProtectionReason(user);
            if (blocked is not null)
            {
                items.Add(new UserActionPreviewItem(row.RowNumber, user, row.Disable, false, blocked));
                continue;
            }

            if (kind == UserActionKind.SetStatus && user.Disabled == row.Disable)
            {
                items.Add(new UserActionPreviewItem(row.RowNumber, user, row.Disable, false,
                    user.Disabled ? "Kullanıcı zaten pasif." : "Kullanıcı zaten aktif."));
                continue;
            }

            items.Add(new UserActionPreviewItem(row.RowNumber, user, row.Disable, true));
        }

        return new UserActionPreviewDto(kind, items, problems.OrderBy(p => p.RowNumber).ToList());
    }

    public async Task<IReadOnlyList<UserActionResult>> ExecuteAsync(UserActionKind kind, IReadOnlyList<UserActionRequest> requests,
        int confirmationCount, IOperationProgress? progress = null)
    {
        progress ??= NullOperationProgress.Instance;
        var ct = CancellationToken.None; // yarıda kalmasın

        var distinct = requests.Where(r => r.UserId > 0).GroupBy(r => r.UserId).Select(g => g.First()).ToList();
        if (distinct.Count == 0) throw new AppValidationException("İşlenecek kullanıcı yok.");
        if (distinct.Count > MaxBatchSize) throw new AppValidationException($"Tek seferde en fazla {MaxBatchSize} kullanıcı işlenebilir.");
        if (confirmationCount != distinct.Count)
            throw new AppValidationException($"Onay sayısı ({confirmationCount}) işlenecek kullanıcı sayısıyla ({distinct.Count}) eşleşmiyor.");

        progress.Phase("Kullanıcılar doğrulanıyor");
        var users = (await _gateway.GetUsersByIdsAsync(distinct.Select(r => r.UserId).ToList(), ct)).ToDictionary(u => u.Id);
        var batchId = Guid.NewGuid();

        _logger.LogInformation("Toplu kullanıcı işlemi başladı ({Kind}). Batch: {BatchId}, Ortam: {Environment}, Kullanıcı: {User}, Adet: {Count}",
            kind, batchId, _environment.Key, _currentUser.UserName, distinct.Count);

        progress.Phase(kind == UserActionKind.Delete ? "Kullanıcılar siliniyor" : "Kullanıcı durumları güncelleniyor", distinct.Count);
        var results = new List<UserActionResult>(distinct.Count);

        foreach (var request in distinct)
        {
            var operation = kind == UserActionKind.Delete ? UserOperations.Delete
                : request.Disable == true ? UserOperations.Disable : UserOperations.Enable;

            UserActionResult result;
            users.TryGetValue(request.UserId, out var user);

            if (user is null)
            {
                result = new UserActionResult(request.UserId, $"(ID: {request.UserId})", null, operation, false, "Kullanıcı bulunamadı.");
            }
            else if (ProtectionReason(user) is { } blocked)
            {
                result = new UserActionResult(user.Id, user.Name, user.FullName, operation, false, blocked);
            }
            else
            {
                progress.Current(user.Name);
                try
                {
                    if (kind == UserActionKind.Delete)
                    {
                        await _gateway.DeleteUserAsync(user.Id, ct);
                        result = new UserActionResult(user.Id, user.Name, user.FullName, operation, true, "Kullanıcı silindi.");
                    }
                    else if (user.Disabled == request.Disable)
                    {
                        result = new UserActionResult(user.Id, user.Name, user.FullName, operation, true,
                            "Değişiklik gerekmedi; kullanıcı zaten bu durumda.");
                    }
                    else
                    {
                        await _gateway.SetUserDisabledAsync(user.Id, request.Disable == true, ct);
                        result = new UserActionResult(user.Id, user.Name, user.FullName, operation, true,
                            request.Disable == true ? "Kullanıcı pasif yapıldı." : "Kullanıcı aktif yapıldı.");
                    }
                }
                catch (Exception ex) when (ex is BoException or AppValidationException)
                {
                    _logger.LogWarning(ex, "Kullanıcı işlemi başarısız ({Operation}): {UserId} {UserName}", operation, user.Id, user.Name);
                    var message = ex is BoException bo ? bo.UserMessage : ((AppValidationException)ex).UserMessage;
                    result = new UserActionResult(user.Id, user.Name, user.FullName, operation, false, message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kullanıcı işleminde beklenmeyen hata ({Operation}): {UserId}", operation, user.Id);
                    result = new UserActionResult(user.Id, user.Name, user.FullName, operation, false, $"Beklenmeyen hata: {ex.Message}");
                }
            }

            results.Add(result);
            await _audit.WriteAsync(new AuditEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Environment = _environment.Key,
                UserName = _currentUser.UserName,
                DisplayName = _currentUser.DisplayName,
                Module = ModuleName,
                Operation = operation,
                ObjectId = result.UserId,
                ObjectName = result.UserName,
                ObjectKind = "User",
                ObjectCuid = user?.Cuid,
                Success = result.Success,
                Message = result.Message,
                BatchId = batchId
            });
            progress.Step(result.UserName, result.Success);
        }

        _logger.LogInformation("Toplu kullanıcı işlemi bitti. Batch: {BatchId}, Başarılı: {Ok}, Başarısız: {Fail}",
            batchId, results.Count(r => r.Success), results.Count(r => !r.Success));

        return results;
    }

    private string? ProtectionReason(UserListItemDto user)
    {
        if (ProtectedAccounts.Contains(user.Name))
            return "BO sistem hesabı; bu uygulamadan değiştirilemez.";

        var service = _gateway.ServiceAccountName;
        if (!string.IsNullOrEmpty(service) && string.Equals(user.Name, service, StringComparison.OrdinalIgnoreCase))
            return "Uygulamanın BO'ya bağlandığı servis hesabı; değiştirilemez.";

        return null;
    }
}
