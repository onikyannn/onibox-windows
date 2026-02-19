using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Onibox.Models;

namespace Onibox.Services;

public sealed record MixedInboundProxy(string Host, int Port);

public static class ConfigInspector
{
    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true
    };

    public static void BuildRuntimeConfig(string sourcePath, InboundMode inboundMode, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException(Localization.GetString("Error.ConfigFile.Missing"), sourcePath);
        }

        var json = File.ReadAllText(sourcePath);
        var root = JsonWithComments.ParseNode(json) as JsonObject;
        if (root is null)
        {
            throw new InvalidOperationException(Localization.GetString("Error.ConfigFile.Missing"));
        }

        var selectedInbound = FindInboundByType(root, inboundMode.ToInboundType());
        if (selectedInbound is null)
        {
            var modeLabel = GetInboundModeLabel(inboundMode);
            var details = string.Format(
                CultureInfo.CurrentCulture,
                Localization.GetString("Error.Inbound.MissingForMode"),
                modeLabel);
            throw new InvalidOperationException(details);
        }

        root["inbounds"] = new JsonArray(selectedInbound.DeepClone());
        SetLogOutputPath(root, AppPaths.SingBoxLogPath);
        SetCacheFilePath(root, destinationPath);

        var runtimeJson = root.ToJsonString(WriterOptions);
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(destinationPath, runtimeJson);
    }

    public static bool TryGetMixedInboundProxy(string configPath, out MixedInboundProxy? proxy, out string? error)
    {
        proxy = null;
        error = null;

        try
        {
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                error = Localization.GetString("Error.ConfigFile.Missing");
                return false;
            }

            var json = File.ReadAllText(configPath);
            using var document = JsonWithComments.ParseDocument(json);

            if (!document.RootElement.TryGetProperty("inbounds", out var inbounds) ||
                inbounds.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var inbound in inbounds.EnumerateArray())
            {
                if (inbound.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetString(inbound, "type", out var type) || string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                if (!string.Equals(type, "mixed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasPort = TryGetInt(inbound, "listen_port", out var port);
                if (!hasPort)
                {
                    hasPort = TryGetInt(inbound, "port", out port);
                }

                if (!hasPort || port <= 0 || port > 65535)
                {
                    error = Localization.GetString("Error.MixedInbound.PortMissing");
                    return false;
                }

                var host = TryGetString(inbound, "listen", out var listen) ? listen! : "127.0.0.1";
                host = NormalizeHost(host);

                proxy = new MixedInboundProxy(host, port);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static JsonObject? FindInboundByType(JsonObject root, string inboundType)
    {
        if (!root.TryGetPropertyValue("inbounds", out var inboundsNode) || inboundsNode is not JsonArray inbounds)
        {
            return null;
        }

        foreach (var node in inbounds)
        {
            if (node is not JsonObject inbound)
            {
                continue;
            }

            if (!TryGetString(inbound, "type", out var type) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            if (string.Equals(type, inboundType, StringComparison.OrdinalIgnoreCase))
            {
                return inbound;
            }
        }

        return null;
    }

    private static void SetLogOutputPath(JsonObject root, string outputPath)
    {
        JsonObject logObject;
        if (root.TryGetPropertyValue("log", out var logNode) && logNode is JsonObject existingLog)
        {
            logObject = existingLog;
        }
        else
        {
            logObject = new JsonObject();
            root["log"] = logObject;
        }

        logObject["output"] = outputPath;
    }

    private static void SetCacheFilePath(JsonObject root, string destinationPath)
    {
        if (!root.TryGetPropertyValue("experimental", out var experimentalNode) || experimentalNode is not JsonObject experimental)
        {
            return;
        }

        SetCachePathForExperimentalCacheFile(experimental, destinationPath);
    }

    private static void SetCachePathForExperimentalCacheFile(JsonObject experimental, string destinationPath)
    {
        if (!experimental.TryGetPropertyValue("cache_file", out var cacheFileNode) || cacheFileNode is not JsonObject cacheFile)
        {
            return;
        }

        var configuredPath = TryGetString(cacheFile, "path", out var rawPath) ? rawPath : null;
        cacheFile["path"] = RuntimeCachePath(configuredPath, destinationPath, "cache.db");
    }

    private static string RuntimeCachePath(string? configuredPath, string destinationPath, string defaultFileName)
    {
        var fileName = ResolveCacheFileName(configuredPath) ?? defaultFileName;
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return Path.GetFullPath(fileName);
        }

        return Path.Combine(destinationDirectory, fileName);
    }

    private static string? ResolveCacheFileName(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var trimmed = configuredPath.Trim();
        var expandedPath = ExpandUserHome(trimmed);
        expandedPath = Environment.ExpandEnvironmentVariables(expandedPath);
        var fileName = Path.GetFileName(expandedPath);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static string ExpandUserHome(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '~')
        {
            return path;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return path;
        }

        if (path.Length == 1)
        {
            return userProfile;
        }

        if (path[1] != '/' && path[1] != '\\')
        {
            return path;
        }

        var suffix = path.Substring(2);
        return string.IsNullOrWhiteSpace(suffix) ? userProfile : Path.Combine(userProfile, suffix);
    }

    private static string GetInboundModeLabel(InboundMode mode) => mode switch
    {
        InboundMode.Proxy => Localization.GetString("Field.InboundMode.Proxy"),
        InboundMode.Tun => Localization.GetString("Field.InboundMode.Tun"),
        _ => mode.ToStorageValue()
    };

    private static bool TryGetString(JsonObject element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetPropertyValue(name, out var property) || property is null)
        {
            return false;
        }

        if (property is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var raw))
        {
            value = raw;
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetInt32(out value);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            return int.TryParse(raw, out value);
        }

        return false;
    }

    private static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "127.0.0.1";
        }

        host = host.Trim();
        if (string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "[::]", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }

        return host;
    }
}
