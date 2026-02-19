using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Onibox.Services;

public sealed class SingBoxService
{
    private readonly SettingsStorage _storage;
    private Process? _process;
    private FileLogger? _logger;

    public event EventHandler? Exited;

    public bool IsRunning => _process is { HasExited: false };

    public string? LogPath => _logger?.LogPath;

    public SingBoxService(SettingsStorage storage)
    {
        _storage = storage;
    }

    public void Start(string configPath)
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            throw new FileNotFoundException(Localization.GetString("Error.ConfigFile.Missing"), configPath);
        }

        var exePath = Path.Combine(AppContext.BaseDirectory, "sing-box.exe");
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(Localization.GetString("Error.SingBoxExe.Missing"), exePath);
        }

        _storage.EnsureLogsDirectory();
        _logger?.Dispose();
        _logger = new FileLogger(_storage.SingBoxLogPath);
        _logger.Info("Starting sing-box.");
        _logger.Info($"Config: {configPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"run -c \"{configPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                _logger?.Write("STDOUT", args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                _logger?.Write("STDERR", args.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            try
            {
                _logger?.Info($"sing-box exited. ExitCode={process.ExitCode}");
            }
            catch
            {
                // ignore logging errors on exit
            }
            Exited?.Invoke(this, EventArgs.Empty);
        };

        if (!process.Start())
        {
            process.Dispose();
            _logger?.Error(Localization.GetString("Error.SingBox.StartFailed"));
            throw new InvalidOperationException(Localization.GetString("Error.SingBox.StartFailed"));
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
    }

    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            _logger?.Info("Stopping sing-box.");
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(2000))
                {
                    _logger?.Info("Force killing sing-box process.");
                    _process.Kill(true);
                    _process.WaitForExit(2000);
                }
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _logger?.Info("sing-box stopped.");
            _logger?.Dispose();
            _logger = null;
        }
    }
}
