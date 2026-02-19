using System;
using System.IO;

namespace Onibox.Services;

public static class AppPaths
{
    private const string AppFolderName = "Onibox";

    public static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    public static readonly string SettingsPath = Path.Combine(AppDataDirectory, "settings.json");
    public static readonly string ConfigPath = Path.Combine(AppDataDirectory, "config.json");
    public static readonly string RuntimeConfigPath = Path.Combine(AppDataDirectory, "runtime-config.json");
    public static readonly string LogsDirectory = Path.Combine(AppDataDirectory, "logs");
    public static readonly string SingBoxLogPath = Path.Combine(LogsDirectory, "singbox.log");
    public static readonly string AppLogPath = Path.Combine(LogsDirectory, "app.log");
}
