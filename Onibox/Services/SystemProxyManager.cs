using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Onibox.Services;

public sealed class SystemProxyManager
{
    private const string InternetSettingsKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    private ProxySnapshot? _snapshot;

    public bool IsManaging => _snapshot is not null;

    public bool TryEnable(string host, int port, out string? error)
    {
        error = null;

        if (_snapshot is not null)
        {
            return true;
        }

        try
        {
            var snapshot = ReadSnapshot();
            if (IsCurrentProxyTarget(snapshot, host, port))
            {
                return true;
            }

            _snapshot = snapshot;
            ApplyProxy(host, port);
            NotifySettingsChanged();
            return true;
        }
        catch (Exception ex)
        {
            var snapshot = _snapshot;
            _snapshot = null;
            if (snapshot is not null)
            {
                try
                {
                    RestoreSnapshot(snapshot);
                    NotifySettingsChanged();
                }
                catch
                {
                    // ignore rollback errors
                }
            }
            error = ex.Message;
            return false;
        }
    }

    public bool TryRestore(out string? error)
    {
        error = null;

        if (_snapshot is null)
        {
            return true;
        }

        try
        {
            RestoreSnapshot(_snapshot);
            NotifySettingsChanged();
            _snapshot = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static ProxySnapshot ReadSnapshot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false);
        if (key is null)
        {
            return new ProxySnapshot(null, null);
        }

        var proxyEnable = ReadDword(key, "ProxyEnable");
        var proxyServer = ReadString(key, "ProxyServer");

        return new ProxySnapshot(proxyEnable, proxyServer);
    }

    private static void ApplyProxy(string host, int port)
    {
        using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey, true);
        if (key is null)
        {
            throw new InvalidOperationException(Localization.GetString("Error.SystemProxy.SettingsOpenFailed"));
        }

        var proxyServer = BuildProxyServer(host, port);
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
    }

    private static void RestoreSnapshot(ProxySnapshot snapshot)
    {
        using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey, true);
        if (key is null)
        {
            throw new InvalidOperationException(Localization.GetString("Error.SystemProxy.SettingsOpenFailed"));
        }

        if (snapshot.ProxyEnable.HasValue)
        {
            key.SetValue("ProxyEnable", snapshot.ProxyEnable.Value, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue("ProxyEnable", false);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ProxyServer))
        {
            key.SetValue("ProxyServer", snapshot.ProxyServer, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("ProxyServer", false);
        }
    }

    private static int? ReadDword(RegistryKey key, string name)
    {
        var value = key.GetValue(name);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(RegistryKey key, string name)
    {
        var value = key.GetValue(name) as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string BuildProxyServer(string host, int port)
    {
        var normalized = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        if (normalized.Contains(':') && !normalized.StartsWith("[", StringComparison.Ordinal))
        {
            normalized = $"[{normalized}]";
        }

        return $"{normalized}:{port}";
    }

    private static bool IsCurrentProxyTarget(ProxySnapshot snapshot, string host, int port)
    {
        if (snapshot.ProxyEnable != 1)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.ProxyServer))
        {
            return false;
        }

        var target = BuildProxyServer(host, port);
        return ProxyServerEquals(snapshot.ProxyServer, target);
    }

    private static bool ProxyServerEquals(string current, string target)
    {
        var normalizedCurrent = current.Trim();
        if (normalizedCurrent.Length == 0)
        {
            return false;
        }

        if (!normalizedCurrent.Contains('='))
        {
            return string.Equals(normalizedCurrent, target, StringComparison.OrdinalIgnoreCase);
        }

        var any = false;
        var segments = normalizedCurrent.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var pair = segment.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            if (pair.Length == 0)
            {
                continue;
            }

            var value = pair.Length == 2 ? pair[1] : pair[0];
            value = value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            any = true;
            if (!string.Equals(value, target, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return any;
    }

    private static void NotifySettingsChanged()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private sealed record ProxySnapshot(int? ProxyEnable, string? ProxyServer);
}
