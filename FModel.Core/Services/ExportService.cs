using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using Serilog;

namespace FModel.Core.Services;

public class ExportService
{
    private readonly ProviderService _providerService;

    public ExportService(ProviderService providerService)
    {
        _providerService = providerService;
    }

    public async Task<bool> ExportFileAsync(string assetPath, string outputDirectory, ExportKind kind = ExportKind.Raw)
    {
        try
        {
            if (_providerService.Provider is null)
            {
                Log.Error("Provider not initialized");
                return false;
            }

            var file = _providerService.TryGetFile(assetPath);
            if (file is null)
            {
                Log.Error("File not found: {Path}", assetPath);
                return false;
            }

            Directory.CreateDirectory(outputDirectory);

            return kind switch
            {
                ExportKind.Raw => await ExportRawAsync(file, outputDirectory),
                ExportKind.Properties => await ExportPropertiesAsync(file, outputDirectory),
                ExportKind.Textures => await ExportTexturesAsync(file, outputDirectory),
                _ => false,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export failed for {Path}", assetPath);
            return false;
        }
    }

    private async Task<bool> ExportRawAsync(GameFile file, string outputDirectory)
    {
        try
        {
            var outPath = Path.Combine(outputDirectory, file.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            var data = _providerService.Provider!.SaveAsset(file);
            await File.WriteAllBytesAsync(outPath, data);

            Log.Information("Exported (raw): {Path}", outPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Raw export failed");
            return false;
        }
    }

    private async Task<bool> ExportPropertiesAsync(GameFile file, string outputDirectory)
    {
        try
        {
            var pkg = _providerService.Provider!.LoadPackage(file);
            if (pkg is null)
            {
                Log.Warning("Could not load package: {Path}", file.Path);
                return false;
            }

            var outPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file.Name) + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(pkg, Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(outPath, json);

            Log.Information("Exported (properties): {Path}", outPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Properties export failed");
            return false;
        }
    }

    private async Task<bool> ExportTexturesAsync(GameFile file, string outputDirectory)
    {
        try
        {
            if (!file.Path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("File is not a uasset: {Path}", file.Path);
                return false;
            }

            var pkg = _providerService.Provider!.LoadPackage(file);
            if (pkg is null)
            {
                Log.Warning("Could not load package: {Path}", file.Path);
                return false;
            }

            var exported = false;
            for (var i = 0; i < pkg.ExportMapLength; i++)
            {
                var export = pkg.GetExportOrNull<UTexture>(i);
                if (export is null) continue;

                try
                {
                    var texture = export.Decode(CUE4Parse.UE4.Versions.ETexturePlatform.PC, out _);
                    if (texture is null) continue;

                    var name = export.Name;
                    var outPath = Path.Combine(outputDirectory, $"{name}.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                    using var ms = new MemoryStream();
                    texture.EncodeTo(ms, SkiaSharp.SKEncodedImageFormat.Png);
                    await File.WriteAllBytesAsync(outPath, ms.ToArray());

                    Log.Information("Exported texture: {Path}", outPath);
                    exported = true;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to export texture: {Name}", export.Name);
                }
            }

            return exported;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Texture export failed");
            return false;
        }
    }
}

public enum ExportKind
{
    Raw,
    Properties,
    Textures,
    Models,
    Audio,
}
