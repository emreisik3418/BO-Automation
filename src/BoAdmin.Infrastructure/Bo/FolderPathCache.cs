using BoAdmin.Application.Reports.Dtos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BoAdmin.Infrastructure.Bo;

/// <summary>
/// CMS klasör yolu doğrudan sorgulanamadığı için tüm klasörler bir kez çekilir,
/// bellekte ağaç kurulur ve 10 dakika önbellekte tutulur.
/// </summary>
public sealed class FolderPathCache
{
    private const string CacheKey = "bo-folder-tree";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim LoadLock = new(1, 1);

    /// <summary>
    /// Public Folders kökünün SI_ID'si (standart kurulumda 23 - doğrulanmalı).
    /// Yollar bu klasörün altından başlar: /Satış/Aylık
    /// </summary>
    public const int RootFolderId = 23;

    private const string Query =
        "SELECT SI_ID, SI_NAME, SI_PARENTID FROM CI_INFOOBJECTS WHERE SI_KIND IN ('Folder','FavoritesFolder')";

    private readonly BoRestClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FolderPathCache> _logger;

    public FolderPathCache(BoRestClient client, IMemoryCache cache, ILogger<FolderPathCache> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public async Task<FolderTree> GetTreeAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out FolderTree? tree) && tree is not null)
            return tree;

        await LoadLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CacheKey, out tree) && tree is not null)
                return tree;

            var entries = await _client.CmsQueryAllAsync(Query, pageSize: 1000, ct: ct);

            var nodes = entries
                .Select(e => new FolderNode(
                    e.GetInt("SI_ID") ?? 0,
                    e.GetString("SI_NAME") ?? string.Empty,
                    e.GetInt("SI_PARENTID") ?? 0))
                .Where(n => n.Id > 0)
                .ToList();

            tree = new FolderTree(nodes);
            _cache.Set(CacheKey, tree, CacheDuration);

            _logger.LogInformation("Klasör ağacı yüklendi: {Count} klasör.", nodes.Count);
            return tree;
        }
        finally
        {
            LoadLock.Release();
        }
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}

public sealed record FolderNode(int Id, string Name, int ParentId);

/// <summary>Bellekteki klasör ağacı: ID -> yol, alt klasörler.</summary>
public sealed class FolderTree
{
    private readonly Dictionary<int, FolderNode> _nodes;
    private readonly Dictionary<int, List<int>> _children = new();
    private readonly Dictionary<int, string> _paths = new();

    public FolderTree(IEnumerable<FolderNode> nodes)
    {
        _nodes = nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var n in _nodes.Values)
        {
            if (!_children.TryGetValue(n.ParentId, out var list))
                _children[n.ParentId] = list = new List<int>();
            list.Add(n.Id);
        }

        foreach (var id in _nodes.Keys)
            _paths[id] = BuildPath(id);
    }

    public bool Contains(int id) => _nodes.ContainsKey(id);

    /// <summary>Klasörün tam yolu. Bilinmeyen ID için "(Klasör ID: x)".</summary>
    public string GetPath(int folderId)
    {
        if (folderId == FolderPathCache.RootFolderId) return "/";
        return _paths.TryGetValue(folderId, out var p) ? p : $"(Klasör ID: {folderId})";
    }

    public FolderDto? GetFolder(int folderId) =>
        _nodes.TryGetValue(folderId, out var n) ? new FolderDto(n.Id, n.Name, GetPath(n.Id)) : null;

    /// <summary>Klasörün kendisi + tüm alt klasörleri.</summary>
    public IReadOnlyList<int> GetSelfAndDescendants(int folderId)
    {
        var result = new List<int>();
        var stack = new Stack<int>();
        var visited = new HashSet<int>();
        stack.Push(folderId);

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id)) continue;
            result.Add(id);

            if (_children.TryGetValue(id, out var kids))
                foreach (var k in kids) stack.Push(k);
        }
        return result;
    }

    public IReadOnlyList<FolderDto> GetAll() =>
        _nodes.Values
            .Where(n => n.Id != FolderPathCache.RootFolderId)
            .Select(n => new FolderDto(n.Id, n.Name, GetPath(n.Id)))
            .OrderBy(f => f.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private string BuildPath(int folderId)
    {
        var parts = new List<string>();
        var visited = new HashSet<int>();
        var current = folderId;

        // Köke (Public Folders) veya bilinmeyen üst klasöre kadar yukarı çık; döngü koruması var
        while (current != 0 && current != FolderPathCache.RootFolderId
               && _nodes.TryGetValue(current, out var node) && visited.Add(current))
        {
            parts.Add(node.Name);
            current = node.ParentId;
        }

        parts.Reverse();
        return "/" + string.Join("/", parts);
    }
}
