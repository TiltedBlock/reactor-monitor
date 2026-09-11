using System.Text.Json;
using System.Text.Json.Serialization;
using Reactor.Core.Diagnostics;

namespace Reactor.Core.Configuration;

/// <summary>Loads/saves <see cref="AppConfig"/> as plain JSON next to the log file.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string Path { get; }

    public ConfigStore(string path) => Path = path;

    public static string DefaultDirectory =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Reactor");

    public static ConfigStore Default() =>
        new(System.IO.Path.Combine(DefaultDirectory, "config.json"));

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                var fresh = new AppConfig().Normalized();
                Save(fresh);
                Log.Info($"Config created at {Path}");
                return fresh;
            }

            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path), Options)
                         ?? new AppConfig();
            Log.Info($"Config loaded from {Path}");
            return config.Normalized();
        }
        catch (Exception ex)
        {
            Log.Error($"Config load failed, using defaults ({Path})", ex);
            return new AppConfig().Normalized();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(config.Normalized(), Options));
        }
        catch (Exception ex)
        {
            Log.Error($"Config save failed ({Path})", ex);
        }
    }
}
