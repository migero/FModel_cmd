using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Serilog;

namespace FModel.Core.Services;

public class ProviderService : IDisposable
{
    private AbstractVfsFileProvider? _provider;
    public AbstractVfsFileProvider? Provider => _provider;

    public async Task<bool> InitializeAsync(string gameDirectory, EGame game = EGame.GAME_UE5_LATEST, ETexturePlatform platform = ETexturePlatform.PC)
    {
        try
        {
            if (!Directory.Exists(gameDirectory))
            {
                Log.Error("Game directory not found: {GameDir}", gameDirectory);
                return false;
            }

            var versionContainer = new VersionContainer(
                game: game,
                platform: platform,
                customVersions: null,
                optionOverrides: null,
                mapStructTypesOverrides: null);

            _provider = new DefaultFileProvider(
                gameDirectory,
                SearchOption.AllDirectories,
                versionContainer,
                StringComparer.OrdinalIgnoreCase);

            _provider.Initialize();

            Log.Information("Provider initialized: {Game} | Archives: {Archives} | Files: {Files}",
                _provider.Versions.Game,
                _provider.MountedVfs.Count + _provider.UnloadedVfs.Count,
                _provider.Files.Count);

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize provider");
            return false;
        }
    }

    public IEnumerable<string> ListFiles(string? filter = null)
    {
        if (_provider is null) return [];

        var files = _provider.Files.Values
            .Select(f => f.Path)
            .Where(p => filter is null || p.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p);

        return files;
    }

    public IEnumerable<string> ListAssets(string? directory = null)
    {
        if (_provider is null) return [];

        var assets = _provider.Files.Values
            .Where(f => f.Path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(f => directory is null || f.Path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path)
            .OrderBy(p => p);

        return assets;
    }

    public GameFile? TryGetFile(string path)
    {
        if (_provider is null) return null;

        var normalized = path.StartsWith('/') ? path : "/" + path;
        if (_provider.Files.TryGetValue(normalized, out var file))
            return file;

        var pathWithoutExt = Path.Combine(Path.GetDirectoryName(normalized) ?? "/", Path.GetFileNameWithoutExtension(normalized));
        if (_provider.Files.TryGetValue(pathWithoutExt, out var fileWithoutExt))
            return fileWithoutExt;

        return null;
    }

    public void Dispose()
    {
        _provider?.Dispose();
    }
}
