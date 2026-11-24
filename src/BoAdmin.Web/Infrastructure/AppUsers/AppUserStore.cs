using System.Text.Encodings.Web;
using System.Text.Json;
using BoAdmin.Web.Authorization;
using Microsoft.Extensions.Options;

namespace BoAdmin.Web.Infrastructure.AppUsers;

/// <summary>Uygulamaya giriş yapabilen (yönetici olmayan) kullanıcı.</summary>
public sealed class AppUser
{
    /// <summary>Sicil (sAMAccountName, domain'siz). Büyük harfle saklanır.</summary>
    public string Sicil { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }

    /// <summary>Ortam anahtarı -> erişebildiği sayfa anahtarları ("Reports.Index" gibi).</summary>
    public Dictionary<string, List<string>> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public bool HasPage(string environment, string pageKey) =>
        IsActive
        && Permissions.TryGetValue(environment, out var pages)
        && pages.Contains(pageKey, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> PagesIn(string environment) =>
        IsActive && Permissions.TryGetValue(environment, out var pages) ? pages : Array.Empty<string>();
}

/// <summary>
/// Uygulama kullanıcılarını JSON dosyasında tutar. Dosya bellekte önbelleklenir; dosya dışarıdan
/// değiştirilirse (değişiklik zamanından anlaşılır) yeniden okunur. Yazma geçici dosya + değiştirme ile yapılır.
/// </summary>
public sealed class AppUserStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Türkçe karakterler okunur kalsın
        PropertyNameCaseInsensitive = true
    };

    private readonly object _lock = new();
    private readonly string _path;
    private readonly ILogger<AppUserStore> _logger;

    private List<AppUser> _users = new();
    private DateTime _loadedStamp = DateTime.MinValue;

    public AppUserStore(IOptions<AccessControlOptions> options, IWebHostEnvironment env, ILogger<AppUserStore> logger)
    {
        var file = string.IsNullOrWhiteSpace(options.Value.UsersFile) ? "data/kullanicilar.json" : options.Value.UsersFile;
        _path = Path.IsPathRooted(file) ? file : Path.Combine(env.ContentRootPath, file);
        _logger = logger;
    }

    public string FilePath => _path;

    public IReadOnlyList<AppUser> GetAll()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _users.OrderBy(u => u.DisplayName ?? u.Sicil, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public AppUser? Find(string? sicil)
    {
        if (string.IsNullOrWhiteSpace(sicil)) return null;
        lock (_lock)
        {
            EnsureLoaded();
            return _users.FirstOrDefault(u => string.Equals(u.Sicil, sicil.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Kullanıcıyı ekler veya günceller. Dönen değer: yeni eklendiyse true.</summary>
    public bool Save(AppUser user, string actor)
    {
        lock (_lock)
        {
            EnsureLoaded();
            user.Sicil = Normalize(user.Sicil);
            var existing = _users.FindIndex(u => string.Equals(u.Sicil, user.Sicil, StringComparison.OrdinalIgnoreCase));
            var now = DateTime.Now;

            if (existing >= 0)
            {
                user.CreatedBy = _users[existing].CreatedBy;
                user.CreatedAt = _users[existing].CreatedAt;
                user.UpdatedBy = actor;
                user.UpdatedAt = now;
                _users[existing] = user;
            }
            else
            {
                user.CreatedBy = actor;
                user.CreatedAt = now;
                _users.Add(user);
            }

            Persist();
            return existing < 0;
        }
    }

    public bool Delete(string sicil)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var removed = _users.RemoveAll(u => string.Equals(u.Sicil, sicil, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Persist();
            return removed;
        }
    }

    public static string Normalize(string? sicil) =>
        AccountNameHelper.GetSamAccountName(sicil?.Trim()).ToUpperInvariant();

    // ------------------------------------------------------------------

    private void EnsureLoaded()
    {
        var stamp = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        if (stamp == _loadedStamp) return;

        if (stamp == DateTime.MinValue)
        {
            _users = new List<AppUser>();
            _loadedStamp = stamp;
            return;
        }

        try
        {
            var text = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<AppUser>>(text, Json) ?? new List<AppUser>();
            foreach (var u in list)
                u.Permissions = new Dictionary<string, List<string>>(u.Permissions ?? new(), StringComparer.OrdinalIgnoreCase);
            _users = list;
            _loadedStamp = stamp;
            _logger.LogInformation("Uygulama kullanıcıları okundu: {Count} kullanıcı, {Path}", list.Count, _path);
        }
        catch (Exception ex)
        {
            // Bozuk dosyada eldeki liste korunur; yöneticiler appsettings'ten girmeye devam eder
            _logger.LogError(ex, "Uygulama kullanıcı dosyası okunamadı: {Path}", _path);
        }
    }

    private void Persist()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_users, Json));

        if (File.Exists(_path))
        {
            // Önceki hâli .bak olarak saklanır
            File.Replace(temp, _path, _path + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, _path);
        }

        _loadedStamp = File.GetLastWriteTimeUtc(_path);
    }
}
