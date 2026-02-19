using System.IO;
using System.Text.Json;
using Onibox.Models;

namespace Onibox.Services;

public sealed class SettingsStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public string AppDataDirectory => AppPaths.AppDataDirectory;
    public string SettingsPath => AppPaths.SettingsPath;
    public string ConfigPath => AppPaths.ConfigPath;
    public string RuntimeConfigPath => AppPaths.RuntimeConfigPath;
    public string LogsDirectory => AppPaths.LogsDirectory;
    public string SingBoxLogPath => AppPaths.SingBoxLogPath;

    public Settings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new Settings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<Settings>(json, SerializerOptions) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        EnsureAppDataDirectory();

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsPath, true);
    }

    public void EnsureAppDataDirectory()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }

    public void EnsureLogsDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
    }
}
