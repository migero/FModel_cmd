using System.Text.Json.Serialization;

namespace FModel.Core.Configuration;

/// <summary>
/// Cross-platform game configuration (replaces Windows registry).
/// Stored as JSON in ~/.config/fmodel/config.json or %APPDATA%/FModel/config.json
/// </summary>
public class GameConfig
{
    [JsonPropertyName("gameDirectory")]
    public string GameDirectory { get; set; } = string.Empty;

    [JsonPropertyName("outputDirectory")]
    public string OutputDirectory { get; set; } = string.Empty;

    [JsonPropertyName("ueVersion")]
    public string UeVersion { get; set; } = "GAME_UE5_LATEST";

    [JsonPropertyName("texturePlatform")]
    public string TexturePlatform { get; set; } = "PC";

    [JsonPropertyName("aesKeys")]
    public Dictionary<string, string> AesKeys { get; set; } = [];

    [JsonPropertyName("mappingsPath")]
    public string? MappingsPath { get; set; }

    public static string GetConfigDirectory()
    {
        var configDir = Environment.OSVersion.Platform switch
        {
            PlatformID.Unix => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fmodel"),
            PlatformID.MacOSX => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fmodel"),
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FModel")
        };

        Directory.CreateDirectory(configDir);
        return configDir;
    }

    public static string GetConfigFilePath() => Path.Combine(GetConfigDirectory(), "config.json");

    public static GameConfig LoadOrDefault()
    {
        var path = GetConfigFilePath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<GameConfig>(json) ?? new GameConfig();
            }
            catch
            {
                return new GameConfig();
            }
        }

        return new GameConfig();
    }

    public void Save()
    {
        var path = GetConfigFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
