using BoAdmin.Infrastructure.Bo;
using Microsoft.Extensions.Options;

namespace BoAdmin.Web.Infrastructure;

/// <summary>Ekranda gösterilecek ortam bilgisi.</summary>
/// <param name="IsCritical">Kritik ortam: onaylarda ek "Emin misiniz?" penceresi açılır.</param>
public sealed record EnvironmentInfo(string Key, string DisplayName, string Color, bool IsCritical = false);

/// <summary>Config'teki BO ortamlarının listesi (SapBo:Environments).</summary>
public sealed class EnvironmentCatalog
{
    private static readonly HashSet<string> AllowedColors =
        new(StringComparer.OrdinalIgnoreCase) { "primary", "secondary", "success", "danger", "warning", "info", "dark" };

    private readonly IOptionsMonitor<BoOptions> _options;

    public EnvironmentCatalog(IOptionsMonitor<BoOptions> options) => _options = options;

    public IReadOnlyList<EnvironmentInfo> All =>
        _options.CurrentValue.Ordered
            .Select(kv => new EnvironmentInfo(
                kv.Key,
                string.IsNullOrWhiteSpace(kv.Value.DisplayName) ? kv.Key : kv.Value.DisplayName,
                AllowedColors.Contains(kv.Value.Color) ? kv.Value.Color.ToLowerInvariant() : "secondary",
                kv.Value.IsCritical ?? string.Equals(kv.Value.Color, "danger", StringComparison.OrdinalIgnoreCase)))
            .ToList();

    public string DefaultKey =>
        _options.CurrentValue.NormalizeKey(_options.CurrentValue.DefaultEnvironment)
        ?? All.FirstOrDefault()?.Key
        ?? string.Empty;

    public EnvironmentInfo? Find(string? key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}
